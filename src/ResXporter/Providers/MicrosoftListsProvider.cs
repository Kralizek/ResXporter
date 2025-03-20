using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Spectre.Console;

namespace ResXporter.Providers;

public class MicrosoftListsProvider(HttpClient http) : IExporter, ILoader
{
    private const string LangPrefix = "lang_x003a_";

    private async Task<string> GetAccessToken(string clientId, string clientSecret, string tenantId)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
        var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "https://graph.microsoft.com/.default",
            ["client_secret"] = clientSecret,
            ["grant_type"] = "client_credentials"
        });

        var response = await http.PostAsync(tokenUrl, requestBody);
        response.EnsureSuccessStatusCode();

        var responseToken = await response.Content.ReadAsStringAsync();
        var tokenJson = JsonDocument.Parse(responseToken);
        
        return tokenJson.RootElement.GetProperty("access_token").GetString() ?? throw new Exception("Failed to obtain access token.");
    }
    
    public async Task ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings)
    {
        var clientId = settings.Arguments.GetRequiredValue("clientId");
        var clientSecret = settings.Arguments.GetRequiredValue("clientSecret");
        var tenantId = settings.Arguments.GetRequiredValue("tenantId");
        
        var siteId = settings.Arguments.GetRequiredValue("siteId");
        var listId = settings.Arguments.GetRequiredValue("listId");
        var updateExistingItems = settings.Arguments.GetBooleanValue("updateExistingItems");

        var accessToken = await GetAccessToken(clientId, clientSecret, tenantId);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        await Parallel.ForEachAsync(rows.OrderBy(c => c.Key), options, async (row, _) =>
        {
            var existingItem = await FindExistingItem(siteId, listId, row.Key);

            if (existingItem is null)
            {
                await CreateNewListItem(siteId, listId, row);
            }
            else if (updateExistingItems)
            {
                DateTime lastModified = DateTime.Parse(existingItem["Modified"]);
                DateTime lastSyncedAt = DateTime.Parse(existingItem["LastSyncedAt"]);
                
                if (lastModified > lastSyncedAt)
                {
                    AnsiConsole.MarkupLine($"{row.Key} [yellow]skipped[/]");

                    return;
                }
                
                await UpdateListItem(siteId, listId, existingItem, row);
            }
        });
    }
    
    private async Task<Dictionary<string, string>?> FindExistingItem(string siteId, string listId, string key)
    {
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items?" +
                  "$expand=fields($select=Title,LastSyncedAt)" +
                  "&$select=id,lastModifiedDateTime" +
                  $"&$filter=fields/Title eq '{key}'";

        using var response = await http.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;
        
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var items = json.RootElement.GetProperty("value").EnumerateArray().ToArray();

        if (items.Length == 0) return null;
        
        return new Dictionary<string, string>
        {
            ["Id"] = items[0].GetProperty("id").GetString() ?? string.Empty,
            ["Modified"] = items[0].GetProperty("lastModifiedDateTime").GetString() ?? string.Empty,
            ["LastSyncedAt"] = items[0].GetProperty("fields").GetProperty("LastSyncedAt").GetString() ?? string.Empty,
            ["ResourceKey"] = items[0].GetProperty("fields").GetProperty("Title").GetString() ?? string.Empty,
            ["Etag"] = items[0].GetProperty("@odata.etag").GetString() ?? string.Empty
        };
    }
    
    private async Task CreateNewListItem(string siteId, string listId, ResourceRow row)
    {
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), row.BaseFile.FullName);
        relativePath = Path.ChangeExtension(relativePath, null);
        relativePath = relativePath.Replace("\\", "/");
        
        var requestBody = new
        {
            fields = new Dictionary<string, object>()
            {
                ["Title"] = row.Key,
                ["Path"] = relativePath,
                ["LastSyncedAt"] = DateTime.UtcNow.ToString("o")
            }
        };
        
        foreach (var (culture, value) in row.Values)
        {
            var cultureKey = culture.Equals(CultureInfo.InvariantCulture) ? $"{LangPrefix}default" : $"{LangPrefix}{culture.Name}";
            requestBody.fields.Add(cultureKey, value);
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items";
        using var response = await http.PostAsJsonAsync(url, requestBody, JsonSerializerOptions.Web);

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Failed to create item {row.Key}[/]: {response.ReasonPhrase}");

            return;
        }
        
        AnsiConsole.MarkupLine($"{row.Key} [green]created[/]");
    }
    
    private async Task UpdateListItem(string siteId, string listId, Dictionary<string, string> item, ResourceRow row)
    {
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), row.BaseFile.FullName);
        relativePath = Path.ChangeExtension(relativePath, null);
        relativePath = relativePath.Replace("\\", "/");
        
        var requestBody = new
        {
            fields = new Dictionary<string, object>
            {
                ["Path"] = relativePath,
                ["LastSyncedAt"] = DateTime.UtcNow.ToString("o")
            }
        };
        
        foreach (var (culture, value) in row.Values)
        {
            var cultureKey = culture.Equals(CultureInfo.InvariantCulture) ? $"{LangPrefix}default" : $"{LangPrefix}{culture.Name}";
            requestBody.fields.Add(cultureKey, value);
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items/{item["Id"]}/fields";
        
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Content = JsonContent.Create(requestBody.fields);

        if (item.TryGetValue("Etag", out var etag) && !string.IsNullOrEmpty(etag))
        {
            request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(etag));
        }
        
        using var response = await http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]Failed to update item {row.Key}[/]: {response.ReasonPhrase}");

            return;
        }
        
        AnsiConsole.MarkupLine($"{row.Key} [green]updated[/]");
    }

    public async IAsyncEnumerable<ResourceRow> FetchAsync(LoaderSettings settings)
    {
        var clientId = settings.Arguments.GetRequiredValue("clientId");
        var clientSecret = settings.Arguments.GetRequiredValue("clientSecret");
        var tenantId = settings.Arguments.GetRequiredValue("tenantId");
        
        var siteId = settings.Arguments.GetRequiredValue("siteId");
        var listId = settings.Arguments.GetRequiredValue("listId");
        
        var accessToken = await GetAccessToken(clientId, clientSecret, tenantId);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items?$expand=fields";

        do
        {
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
        
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var items = json.RootElement.GetProperty("value").EnumerateArray();
        
            AnsiConsole.MarkupLine($"{items.Count()} item(s) found");

            foreach (var item in items)
            {
                var fields = item.GetProperty("fields");
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{fields.GetProperty("Path").GetString()}.resx");
                var baseFile = new FileInfo(filePath);
                var baseName = Path.GetFileNameWithoutExtension(baseFile.Name);
            
                var key = fields.GetProperty("Title").GetString();
            
                var row = new ResourceRow(baseFile, baseName, key!);

                foreach (var prop in fields.EnumerateObject())
                {
                    if (!prop.Name.StartsWith(LangPrefix)) continue;
                    
                    var cultureName = prop.Name.Replace(LangPrefix, string.Empty);
                    
                    if (cultureName.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = prop.Value.GetString();
                        row.Values.Add(CultureInfo.InvariantCulture, value!);
                        continue;
                    }

                    if (TryGetCultureInfo(cultureName, out var culture))
                    {
                        var value = prop.Value.GetString();

                        if (!string.IsNullOrEmpty(value))
                        {
                            row.Values.Add(culture, value);
                        }
                    }
                }
            
                yield return row;
            }
            
            url = json.RootElement.TryGetProperty("@odata.nextLink", out var nextLink) ? nextLink.GetString() : null;
            
        } while (!string.IsNullOrEmpty(url));
    }
}