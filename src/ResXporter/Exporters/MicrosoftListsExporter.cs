﻿using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Spectre.Console;

namespace ResXporter.Exporters;

public class MicrosoftListsExporter(HttpClient http) : IExporter
{
    public async Task ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings)
    {
        var siteId = settings.Arguments.GetValue("siteId");
        var listId = settings.Arguments.GetValue("listId");

        var accessToken = await GetAccessToken(settings);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        foreach (var row in rows)
        {
            var existingItem = await FindExistingItem(siteId, listId, row.Key);

            if (existingItem is null)
            {
                await CreateNewListItem(siteId, listId, row);
                
                AnsiConsole.MarkupLine($"{row.Key} [green]created[/]");
            }
            else
            {
                DateTime lastModified = DateTime.Parse(existingItem["Modified"]);
                DateTime lastSyncedAt = DateTime.Parse(existingItem["LastSyncedAt"]);
                
                if (lastModified > lastSyncedAt)
                {
                    AnsiConsole.MarkupLine($"{row.Key} [yellow]skipped[/]");
                    
                    continue;
                }
                
                await UpdateListItem(siteId, listId, existingItem, row);
                
                AnsiConsole.MarkupLine($"{row.Key} [green]updated[/]");
            }
        }
    }
    
    private async Task<string> GetAccessToken(ExportSettings settings)
    {
        var clientId = settings.Arguments.GetValue("clientId");
        var clientSecret = settings.Arguments.GetValue("clientSecret");
        var tenantId = settings.Arguments.GetValue("tenantId");
        
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
    
    private async Task<Dictionary<string, string>?> FindExistingItem(string siteId, string listId, string key)
    {
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items?$filter=fields/ResourceKey eq '{key}'";
        
        using var response = await http.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;
        
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var items = json.RootElement.GetProperty("value").EnumerateArray().ToArray();

        if (items.Length == 0) return null;
        
        return new Dictionary<string, string>
        {
            ["Id"] = items[0].GetProperty("id").GetString() ?? string.Empty,
            ["Modified"] = items[0].GetProperty("fields").GetProperty("Modified").GetString() ?? string.Empty,
            ["LastSyncedAt"] = items[0].GetProperty("fields").GetProperty("LastSyncedAt").GetString() ?? string.Empty,
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
                ["ResourceKey"] = row.Key,
                ["Path"] = relativePath,
                ["LastSyncedAt"] = DateTime.UtcNow.ToString("o")
            }
        };
        
        foreach (var (culture, value) in row.Values)
        {
            var cultureKey = culture.Equals(CultureInfo.InvariantCulture) ? "DefaultCulture" : culture.Name;
            requestBody.fields.Add(cultureKey, value);
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items";
        using var response = await http.PostAsJsonAsync(url, requestBody, JsonSerializerOptions.Web);

        response.EnsureSuccessStatusCode();
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
            var cultureKey = culture.Equals(CultureInfo.InvariantCulture) ? "DefaultCulture" : culture.Name;
            requestBody.fields.Add(cultureKey, value);
        }
        
        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items/{item["Id"]}/fields";
        
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Content = JsonContent.Create(requestBody);

        request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(item["Etag"]));
        
        using var response = await http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
    }
}

file static class DictionaryExtensions
{
    public static TValue GetValue<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
    {
        return dictionary.TryGetValue(key, out var value) ? value : throw new Exception($"Argument not found: {key}");
    }
}