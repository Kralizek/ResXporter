using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using ResXporter.Providers;
using Xunit;

namespace ResXporter.Tests.Providers;

public class RequiresUpdateTests
{
    private static ResourceRow MakeRow(string key, params (CultureInfo culture, string value)[] values)
    {
        var row = new ResourceRow(new FileInfo("/tmp/Test.resx"), "Test", key);
        foreach (var (culture, value) in values)
        {
            row.Values.Add(culture, value);
        }
        return row;
    }

    [Fact]
    public void Returns_false_when_all_values_are_identical()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
            ["lang_x003a_fr-FR"] = "Bonjour"
        };
        var row = MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        Assert.False(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_true_when_default_value_differs()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
        };
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, "Hello World"));

        Assert.True(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_true_when_language_value_differs()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
            ["lang_x003a_fr-FR"] = "Bonjour"
        };
        var row = MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Au revoir"));

        Assert.True(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_true_when_language_field_is_missing_from_existing()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello"
        };
        var row = MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("de-DE"), "Hallo"));

        Assert.True(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_false_when_values_differ_only_in_crlf_vs_lf()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Line1\r\nLine2"
        };
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, "Line1\nLine2"));

        Assert.False(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_true_when_values_differ_by_trailing_whitespace()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello "
        };
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, "Hello"));

        Assert.True(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_false_when_existing_is_empty_and_row_value_is_empty()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = ""
        };
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, ""));

        Assert.False(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_false_when_existing_field_is_absent_and_row_value_is_empty()
    {
        var existing = new Dictionary<string, string>();
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, ""));

        Assert.False(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_true_when_existing_is_null_equivalent_but_row_has_value()
    {
        var existing = new Dictionary<string, string>();
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, "Hello"));

        Assert.True(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }

    [Fact]
    public void Returns_false_for_multiline_values_with_only_crlf_difference()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Line1\r\nLine2\r\nLine3"
        };
        var row = MakeRow("Key", (CultureInfo.InvariantCulture, "Line1\nLine2\nLine3"));

        Assert.False(MicrosoftListsProvider.RequiresUpdate(existing, row));
    }
}

public class ExportAsyncTests
{
    private const string TenantId = "test-tenant";
    private const string ClientId = "test-client";
    private const string ClientSecret = "test-secret";
    private const string SiteId = "test-site";
    private const string ListId = "test-list";

    private static ExportSettings MakeSettings(bool updateExisting = false) => new()
    {
        Arguments = new Dictionary<string, string>
        {
            ["clientId"] = ClientId,
            ["clientSecret"] = ClientSecret,
            ["tenantId"] = TenantId,
            ["siteId"] = SiteId,
            ["listId"] = ListId,
            ["updateExistingItems"] = updateExisting.ToString().ToLower()
        }
    };

    private static ResourceRow MakeRow(string key, params (CultureInfo culture, string value)[] values)
    {
        var row = new ResourceRow(new FileInfo("/tmp/Resources.resx"), "Resources", key);
        foreach (var (culture, value) in values)
        {
            row.Values.Add(culture, value);
        }
        return row;
    }

    private static string ListItemsResponse(IEnumerable<Dictionary<string, object>> items) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = items.ToArray() });

    private static Dictionary<string, object> MakeListItem(string id, string key, string lastModified, string lastSyncedAt, Dictionary<string, string>? langFields = null)
    {
        var fieldsDict = new Dictionary<string, object>
        {
            ["Title"] = key,
            ["LastSyncedAt"] = lastSyncedAt
        };

        if (langFields != null)
        {
            foreach (var (k, v) in langFields)
                fieldsDict[k] = v;
        }

        return new Dictionary<string, object>
        {
            ["id"] = id,
            ["@odata.etag"] = $"\"{id}-etag\"",
            ["lastModifiedDateTime"] = lastModified,
            ["fields"] = fieldsDict
        };
    }

    [Fact]
    public async Task Creates_new_item_when_key_is_not_in_existing_list()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse([]));
        handler.SetupCreateItemResponse(SiteId, ListId);

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("NewKey", (CultureInfo.InvariantCulture, "Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: false));

        var postRequests = handler.Requests
            .Where(r => r.Method == "POST" && r.Uri.Contains($"/lists/{ListId}/items"))
            .ToList();

        Assert.Single(postRequests);
        Assert.Contains("\"NewKey\"", postRequests[0].Body ?? "");
    }

    [Fact]
    public async Task Does_not_send_patch_when_existing_item_values_are_unchanged()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o"); // modified BEFORE last sync → not manually edited

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Hello",
                ["lang_x003a_fr-FR"] = "Bonjour"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.Empty(patchRequests);
    }

    [Fact]
    public async Task Sends_patch_when_existing_item_values_differ()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Old Hello"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.Single(patchRequests);
        Assert.Contains("New Hello", patchRequests[0].Body ?? "");
    }

    [Fact]
    public async Task Patch_payload_contains_updated_language_fields()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Old Hello",
                ["lang_x003a_fr-FR"] = "Bonjour"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1",
            (CultureInfo.InvariantCulture, "New Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchBody = handler.Requests.Single(r => r.Method == "PATCH").Body;
        Assert.NotNull(patchBody);
        Assert.Contains("lang_x003a_default", patchBody);
        Assert.Contains("New Hello", patchBody);
        Assert.Contains("LastSyncedAt", patchBody);
    }

    [Fact]
    public async Task Skips_item_when_manually_modified_after_last_sync()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-2).ToString("o");
        var modifiedAt = now.AddHours(-1).ToString("o"); // modified AFTER last sync → manually edited

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Old Hello"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.Empty(patchRequests);
    }

    [Fact]
    public async Task Does_not_send_patch_when_values_differ_only_by_crlf()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Line1\r\nLine2"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1", (CultureInfo.InvariantCulture, "Line1\nLine2"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.Empty(patchRequests);
    }

    [Fact]
    public async Task Does_not_update_when_updateExistingItems_is_false()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Old Hello"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: false));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.Empty(patchRequests);
    }

    [Fact]
    public async Task Patch_is_sent_to_correct_item_id_url()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        var items = new[]
        {
            MakeListItem("abc123", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Old Value"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "abc123");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = MakeRow("Key1", (CultureInfo.InvariantCulture, "New Value"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequest = handler.Requests.Single(r => r.Method == "PATCH");
        Assert.Contains($"/items/abc123/fields", patchRequest.Uri);
    }
}

// Infrastructure

public record RequestRecord(string Method, string Uri, string? Body);

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Matcher, Func<HttpRequestMessage, HttpResponseMessage> Handler)> _handlers = [];
    private readonly ConcurrentBag<RequestRecord> _requests = [];

    public IEnumerable<RequestRecord> Requests => _requests;

    public void AddHandler(Func<HttpRequestMessage, bool> matcher, Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handlers.Add((matcher, handler));

    public void AddResponse(Func<HttpRequestMessage, bool> matcher, HttpResponseMessage response)
        => _handlers.Add((matcher, _ => response));

    public void SetupTokenResponse(string tenantId) =>
        AddResponse(
            r => r.Method == HttpMethod.Post && r.RequestUri!.ToString().Contains(tenantId),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "fake_token" }), Encoding.UTF8, "application/json")
            });

    public void SetupListItemsResponse(string siteId, string listId, string json) =>
        AddResponse(
            r => r.Method == HttpMethod.Get && r.RequestUri!.ToString().Contains($"/sites/{siteId}/lists/{listId}/items"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

    public void SetupCreateItemResponse(string siteId, string listId) =>
        AddResponse(
            r => r.Method == HttpMethod.Post && r.RequestUri!.ToString().Contains($"/sites/{siteId}/lists/{listId}/items"),
            new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

    public void SetupPatchFieldsResponse(string siteId, string listId, string itemId) =>
        AddResponse(
            r => r.Method == HttpMethod.Patch && r.RequestUri!.ToString().Contains($"/items/{itemId}/fields"),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        _requests.Add(new RequestRecord(request.Method.Method, request.RequestUri?.ToString() ?? "", body));

        foreach (var (matcher, handler) in _handlers)
        {
            if (matcher(request))
                return handler(request);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No handler matched: {request.Method} {request.RequestUri}", Encoding.UTF8, "application/json")
        };
    }
}
