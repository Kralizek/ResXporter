using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ResXporter.Providers;
using NUnit.Framework;

namespace ResXporter.Tests.Providers;

internal static class TestHelpers
{
    public static ResourceRow MakeRow(string key, params (CultureInfo culture, string value)[] values)
    {
        var row = new ResourceRow(new FileInfo(Path.Combine(Path.GetTempPath(), "Resources.resx")), "Resources", key);
        foreach (var (culture, value) in values)
        {
            row.Values.Add(culture, value);
        }
        return row;
    }

    public static string HashOf(string value)
    {
        var normalized = value.Replace("\r\n", "\n");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public class RequiresUpdateTests
{

    [Test]
    public void Returns_false_when_all_values_are_identical()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
            ["lang_x003a_fr-FR"] = "Bonjour"
        };
        var row = TestHelpers.MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.False);
    }

    [Test]
    public void Returns_true_when_default_value_differs()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Hello World"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }

    [Test]
    public void Returns_true_when_language_value_differs()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
            ["lang_x003a_fr-FR"] = "Bonjour"
        };
        var row = TestHelpers.MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Au revoir"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }

    [Test]
    public void Returns_true_when_language_field_is_missing_from_existing()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello"
        };
        var row = TestHelpers.MakeRow("Key",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("de-DE"), "Hallo"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }

    [Test]
    public void Returns_false_when_values_differ_only_in_crlf_vs_lf()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Line1\r\nLine2"
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Line1\nLine2"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.False);
    }

    [Test]
    public void Returns_true_when_values_differ_by_trailing_whitespace()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello "
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Hello"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }

    [Test]
    public void Returns_false_when_existing_is_empty_and_row_value_is_empty()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = ""
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, ""));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.False);
    }

    [Test]
    public void Returns_false_when_existing_field_is_absent_and_row_value_is_empty()
    {
        var existing = new Dictionary<string, string>();
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, ""));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.False);
    }

    [Test]
    public void Returns_true_when_existing_is_null_equivalent_but_row_has_value()
    {
        var existing = new Dictionary<string, string>();
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Hello"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }

    [Test]
    public void Returns_false_for_multiline_values_with_only_crlf_difference()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Line1\r\nLine2\r\nLine3"
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Line1\nLine2\nLine3"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.False);
    }

    [Test]
    public void Returns_true_when_existing_has_language_field_not_in_row()
    {
        var existing = new Dictionary<string, string>
        {
            ["lang_x003a_default"] = "Hello",
            ["lang_x003a_de-DE"] = "Hallo"
        };
        var row = TestHelpers.MakeRow("Key", (CultureInfo.InvariantCulture, "Hello"));

        Assert.That(MicrosoftListsProvider.RequiresUpdate(existing, row), Is.True);
    }
}

public class ExportAsyncTests
{
    private const string TenantId = "test-tenant";
    private const string ClientId = "test-client";
    private const string ClientSecret = "test-secret";
    private const string SiteId = "test-site";
    private const string ListId = "test-list";

    private static ExportSettings MakeSettings(bool updateExisting = false, bool initializeMissingLanguages = false) => new()
    {
        Arguments = new Dictionary<string, string>
        {
            ["clientId"] = ClientId,
            ["clientSecret"] = ClientSecret,
            ["tenantId"] = TenantId,
            ["siteId"] = SiteId,
            ["listId"] = ListId,
            ["updateExistingItems"] = updateExisting.ToString().ToLower(),
            ["initializeMissingLanguages"] = initializeMissingLanguages.ToString().ToLower()
        }
    };

    private static ResourceRow MakeRow(string key, params (CultureInfo culture, string value)[] values)
        => TestHelpers.MakeRow(key, values);

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

    [Test]
    public async Task Creates_new_item_when_key_is_not_in_existing_list()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse([]));
        handler.SetupCreateItemResponse(SiteId, ListId);

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("NewKey", (CultureInfo.InvariantCulture, "Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: false));

        var postRequests = handler.Requests
            .Where(r => r.Method == "POST" && r.Uri.Contains($"/lists/{ListId}/items"))
            .ToList();

        Assert.That(postRequests, Has.Count.EqualTo(1));
        Assert.That(postRequests[0].Body, Does.Contain("\"NewKey\""));
    }

    [Test]
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
                ["lang_x003a_fr-FR"] = "Bonjour",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Hello"),
                ["_sync_hash_lang_x003a_fr-FR"] = TestHelpers.HashOf("Bonjour")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.That(patchRequests, Is.Empty);
    }

    [Test]
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
                ["lang_x003a_default"] = "Old Hello",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Old Hello")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.That(patchRequests, Has.Count.EqualTo(1));
        Assert.That(patchRequests[0].Body, Does.Contain("New Hello"));
    }

    [Test]
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
                ["lang_x003a_fr-FR"] = "Bonjour",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Old Hello"),
                ["_sync_hash_lang_x003a_fr-FR"] = TestHelpers.HashOf("Bonjour")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "New Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchBody = handler.Requests.Single(r => r.Method == "PATCH").Body;
        Assert.That(patchBody, Is.Not.Null);
        Assert.That(patchBody, Does.Contain("lang_x003a_default"));
        Assert.That(patchBody, Does.Contain("New Hello"));
        Assert.That(patchBody, Does.Contain("LastSyncedAt"));
    }

    [Test]
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

        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.That(patchRequests, Is.Empty);
    }

    [Test]
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
                ["lang_x003a_default"] = "Line1\r\nLine2",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Line1\r\nLine2")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "Line1\nLine2"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.That(patchRequests, Is.Empty);
    }

    [Test]
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

        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "New Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: false));

        var patchRequests = handler.Requests
            .Where(r => r.Method == "PATCH")
            .ToList();

        Assert.That(patchRequests, Is.Empty);
    }

    [Test]
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
                ["lang_x003a_default"] = "Old Value",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Old Value")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "abc123");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "New Value"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequest = handler.Requests.Single(r => r.Method == "PATCH");
        Assert.That(patchRequest.Uri, Does.Contain("/items/abc123/fields"));
    }

    [Test]
    public async Task Sends_patch_and_clears_stale_language_field_when_translation_removed()
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
                ["lang_x003a_default"] = "Hello",
                ["lang_x003a_de-DE"] = "Hallo"
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        // Incoming row no longer contains de-DE
        var row = TestHelpers.MakeRow("Key1", (CultureInfo.InvariantCulture, "Hello"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.That(patchRequests, Has.Count.EqualTo(1));
        var patchBody = patchRequests[0].Body;
        Assert.That(patchBody, Does.Contain("lang_x003a_de-DE"));
        Assert.That(patchBody, Does.Contain("\"lang_x003a_de-DE\":\"\""));
    }

    [Test]
    public async Task Mixed_row_protects_manually_changed_language_and_updates_safe_language()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        // lang_x003a_default has no hash and a different value → will be protected
        // lang_x003a_fr-FR has a valid hash matching current value and a different source value → safe to update
        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "ManualEdit",
                ["lang_x003a_fr-FR"] = "Bonjour",
                ["_sync_hash_lang_x003a_fr-FR"] = TestHelpers.HashOf("Bonjour")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "SourceDefault"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Au revoir"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.That(patchRequests, Has.Count.EqualTo(1));
        var patchBody = patchRequests[0].Body!;

        // fr-FR is safe → included with new value
        Assert.That(patchBody, Does.Contain("lang_x003a_fr-FR"));
        Assert.That(patchBody, Does.Contain("Au revoir"));

        // default is protected → not overwritten
        Assert.That(patchBody, Does.Not.Contain("SourceDefault"));
        Assert.That(patchBody, Does.Not.Contain("ManualEdit"));
    }

    [Test]
    public async Task Creates_new_item_with_hash_fields()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse([]));
        handler.SetupCreateItemResponse(SiteId, ListId);

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("NewKey",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: false));

        var postBody = handler.Requests
            .Single(r => r.Method == "POST" && r.Uri.Contains($"/lists/{ListId}/items")).Body!;

        Assert.That(postBody, Does.Contain("lang_x003a_default"));
        Assert.That(postBody, Does.Contain("Hello"));
        Assert.That(postBody, Does.Contain("_sync_hash_lang_x003a_default"));
        Assert.That(postBody, Does.Contain(TestHelpers.HashOf("Hello")));
        Assert.That(postBody, Does.Contain("lang_x003a_fr-FR"));
        Assert.That(postBody, Does.Contain("Bonjour"));
        Assert.That(postBody, Does.Contain("_sync_hash_lang_x003a_fr-FR"));
        Assert.That(postBody, Does.Contain(TestHelpers.HashOf("Bonjour")));
    }

    [Test]
    public async Task Second_run_after_create_is_fully_idempotent()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        // Simulate what the item looks like after the first create (hashes already stored)
        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Hello",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Hello"),
                ["lang_x003a_fr-FR"] = "Bonjour",
                ["_sync_hash_lang_x003a_fr-FR"] = TestHelpers.HashOf("Bonjour")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("fr-FR"), "Bonjour"));

        await provider.ExportAsync([row], MakeSettings(updateExisting: true));

        var patchRequests = handler.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.That(patchRequests, Is.Empty);
    }
    [Test]
    public async Task Does_not_backfill_missing_language_when_flag_is_disabled()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        // Existing item has no Italian value and no Italian hash
        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Hello",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Hello")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("it-IT"), "Ciao"));

        // Flag disabled: empty current + missing hash → protected, no PATCH for it-IT
        await provider.ExportAsync([row], MakeSettings(updateExisting: true, initializeMissingLanguages: false));

        var patchRequests = handler.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.That(patchRequests, Is.Empty);
    }

    [Test]
    public async Task Backfills_missing_language_when_flag_is_enabled()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetupTokenResponse(TenantId);

        var now = DateTime.UtcNow;
        var syncedAt = now.AddHours(-1).ToString("o");
        var modifiedAt = now.AddHours(-2).ToString("o");

        // Existing item has no Italian value and no Italian hash
        var items = new[]
        {
            MakeListItem("item1", "Key1", modifiedAt, syncedAt, new Dictionary<string, string>
            {
                ["lang_x003a_default"] = "Hello",
                ["_sync_hash_lang_x003a_default"] = TestHelpers.HashOf("Hello")
            })
        };
        handler.SetupListItemsResponse(SiteId, ListId, ListItemsResponse(items));
        handler.SetupPatchFieldsResponse(SiteId, ListId, "item1");

        var http = new HttpClient(handler);
        var provider = new MicrosoftListsProvider(http);

        var row = TestHelpers.MakeRow("Key1",
            (CultureInfo.InvariantCulture, "Hello"),
            (CultureInfo.GetCultureInfo("it-IT"), "Ciao"));

        // Flag enabled: empty current + missing hash + non-empty source → backfill it-IT
        await provider.ExportAsync([row], MakeSettings(updateExisting: true, initializeMissingLanguages: true));

        var patchRequests = handler.Requests.Where(r => r.Method == "PATCH").ToList();
        Assert.That(patchRequests, Has.Count.EqualTo(1));
        var patchBody = patchRequests[0].Body!;
        Assert.That(patchBody, Does.Contain("lang_x003a_it-IT"));
        Assert.That(patchBody, Does.Contain("Ciao"));
        Assert.That(patchBody, Does.Contain("_sync_hash_lang_x003a_it-IT"));
        Assert.That(patchBody, Does.Contain(TestHelpers.HashOf("Ciao")));
    }
}

public class EvaluateLanguageFieldTests
{
    [Test]
    public void Missing_hash_and_aligned_value_initializes_hash()
    {
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "Hello", storedHash: null);

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.EqualTo(TestHelpers.HashOf("Hello")));
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Initialize));
    }

    [Test]
    public void Missing_hash_and_differing_value_protects_field()
    {
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "World", storedHash: null);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Empty_current_value_and_missing_hash_protects_field()
    {
        // An empty current value with no stored hash could be an intentional translator clear.
        // Without a hash to prove the field was never touched, we cannot safely overwrite it.
        var result = MicrosoftListsProvider.EvaluateLanguageField(null, "Hello", storedHash: null);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    // initializeMissingLanguages flag tests

    [Test]
    public void Flag_disabled_empty_current_missing_hash_non_empty_source_protects()
    {
        // Default behavior: flag off → empty current + missing hash + non-empty source is still protected
        var result = MicrosoftListsProvider.EvaluateLanguageField(null, "Hello", storedHash: null, initializeMissingLanguages: false);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Flag_enabled_empty_current_missing_hash_non_empty_source_initializes()
    {
        // Opt-in backfill: empty current + missing hash + non-empty source → safe + initialize hash
        var result = MicrosoftListsProvider.EvaluateLanguageField(null, "Hello", storedHash: null, initializeMissingLanguages: true);

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.EqualTo(TestHelpers.HashOf("Hello")));
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Initialize));
    }

    [Test]
    public void Flag_enabled_non_empty_current_missing_hash_different_source_still_protected()
    {
        // Flag does not weaken protection when current list value already has content
        var result = MicrosoftListsProvider.EvaluateLanguageField("Existing", "NewValue", storedHash: null, initializeMissingLanguages: true);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Flag_enabled_empty_current_invalid_hash_non_empty_source_still_protected()
    {
        // Flag only applies to missing hash (null/empty), not to corrupt/invalid hash values
        var result = MicrosoftListsProvider.EvaluateLanguageField(null, "Hello", storedHash: "not-a-valid-hash", initializeMissingLanguages: true);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Flag_enabled_empty_current_missing_hash_empty_source_initializes_hash()
    {
        // Both-empty case follows the normal aligned path (not the backfill path) because
        // normalizedSource is also empty. The result is still safe + initialize, but via aligned logic.
        var result = MicrosoftListsProvider.EvaluateLanguageField(null, null, storedHash: null, initializeMissingLanguages: true);

        // aligned (both empty) → initialize hash via normal aligned branch
        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Initialize));
    }

    [Test]
    public void Invalid_hash_and_aligned_value_repairs_hash()
    {
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "Hello", storedHash: "not-a-valid-hash");

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.EqualTo(TestHelpers.HashOf("Hello")));
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Repair));
    }

    [Test]
    public void Invalid_hash_and_differing_value_protects_field()
    {
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "World", storedHash: "not-a-valid-hash");

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Valid_hash_and_unchanged_field_is_safe_with_no_hash_update()
    {
        var hash = TestHelpers.HashOf("Hello");
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "Hello", storedHash: hash);

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Valid_hash_and_manually_changed_conflicting_field_is_protected()
    {
        // Stored hash matches "OriginalValue", but current is "ManualEdit" (different from source "SourceValue")
        var hash = TestHelpers.HashOf("OriginalValue");
        var result = MicrosoftListsProvider.EvaluateLanguageField("ManualEdit", "SourceValue", storedHash: hash);

        Assert.That(result.IsSafe, Is.False);
        Assert.That(result.HashToWrite, Is.Null);
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.None));
    }

    [Test]
    public void Valid_hash_and_reconciled_field_is_safe_and_hash_refreshed()
    {
        // Stored hash matches old value, but current value already matches source (manually reconciled)
        var oldHash = TestHelpers.HashOf("OldValue");
        var result = MicrosoftListsProvider.EvaluateLanguageField("Hello", "Hello", storedHash: oldHash);

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.EqualTo(TestHelpers.HashOf("Hello")));
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Refresh));
    }

    [Test]
    public void Valid_hash_and_unchanged_value_allows_safe_update_to_new_source()
    {
        // Stored hash matches current, but source has a new value → safe to update
        var hash = TestHelpers.HashOf("CurrentValue");
        var result = MicrosoftListsProvider.EvaluateLanguageField("CurrentValue", "NewValue", storedHash: hash);

        Assert.That(result.IsSafe, Is.True);
        Assert.That(result.HashToWrite, Is.EqualTo(TestHelpers.HashOf("NewValue")));
        Assert.That(result.HashUpdate, Is.EqualTo(HashUpdateKind.Refresh));
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
