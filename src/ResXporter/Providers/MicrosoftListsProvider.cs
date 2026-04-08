using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Spectre.Console;

namespace ResXporter.Providers;

internal enum HashUpdateKind { None, Initialize, Repair, Refresh }

internal record LanguageFieldDecision(bool IsSafe, string? HashToWrite, HashUpdateKind HashUpdate);

public class MicrosoftListsProvider(HttpClient http) : IExporter, ILoader
{
    private const string LangPrefix = "lang_x003a_";
    private const string SyncHashPrefix = "_sync_hash_";

    private static string GetSyncHashKey(string langKey) => $"{SyncHashPrefix}{langKey}";

    private static string ComputeHash(string normalizedValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedValue));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsValidHash(string? hash)
        => hash is { Length: 64 } && hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    internal static LanguageFieldDecision EvaluateLanguageField(string? currentListValue, string? sourceValue, string? storedHash)
    {
        var normalizedCurrent = NormalizeValue(currentListValue);
        var normalizedSource = NormalizeValue(sourceValue);
        var aligned = normalizedCurrent.Equals(normalizedSource, StringComparison.Ordinal);

        if (!IsValidHash(storedHash))
        {
            var updateKind = string.IsNullOrEmpty(storedHash) ? HashUpdateKind.Initialize : HashUpdateKind.Repair;
            return aligned
                ? new(true, ComputeHash(normalizedSource), updateKind)
                : new(false, null, HashUpdateKind.None);
        }

        var currentHash = ComputeHash(normalizedCurrent);
        if (currentHash.Equals(storedHash, StringComparison.Ordinal))
        {
            return aligned
                ? new(true, null, HashUpdateKind.None)
                : new(true, ComputeHash(normalizedSource), HashUpdateKind.Refresh);
        }

        return aligned
            ? new(true, ComputeHash(normalizedCurrent), HashUpdateKind.Refresh)
            : new(false, null, HashUpdateKind.None);
    }

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
        
        var existingItems = await LoadExistingItems(siteId, listId);

        var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        var initializedHashCount = 0;
        var repairedHashCount = 0;
        var protectedCount = 0;

        await Parallel.ForEachAsync(rows.OrderBy(c => c.Key), options, async (row, _) =>
        {
            if (!existingItems.TryGetValue(row.Key, out var existingItem))
            {
                await CreateNewListItem(siteId, listId, row);
            }
            else if (updateExistingItems)
            {
                var patch = BuildSyncPatch(existingItem, row, out var rowInitialized, out var rowRepaired, out var rowProtected);

                Interlocked.Add(ref initializedHashCount, rowInitialized);
                Interlocked.Add(ref repairedHashCount, rowRepaired);
                Interlocked.Add(ref protectedCount, rowProtected);

                if (patch.Count == 0)
                {
                    AnsiConsole.MarkupLine($"{row.Key} [grey]unchanged[/]");
                    return;
                }

                await UpdateListItem(siteId, listId, existingItem, row, patch);
            }
        });

        if (initializedHashCount > 0)
            AnsiConsole.MarkupLine($"[blue]{initializedHashCount} hash field(s) initialized[/]");
        if (repairedHashCount > 0)
            AnsiConsole.MarkupLine($"[blue]{repairedHashCount} hash field(s) repaired[/]");
        if (protectedCount > 0)
            AnsiConsole.MarkupLine($"[yellow]{protectedCount} field(s) protected from overwrite[/]");
    }
    
    private async Task<Dictionary<string, Dictionary<string, string>>> LoadExistingItems(string siteId, string listId)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();

        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items?$expand=fields";

        do
        {
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            var items = json.RootElement.GetProperty("value").EnumerateArray().ToArray();

            foreach (var item in items)
            {
                var fields = item.GetProperty("fields");
                var key = fields.GetProperty("Title").GetString();
                
                var values = new Dictionary<string, string>
                {
                    ["Id"] = item.GetProperty("id").GetString() ?? string.Empty,
                    ["Modified"] = item.GetProperty("lastModifiedDateTime").GetString() ?? string.Empty,
                    ["LastSyncedAt"] = fields.GetProperty("LastSyncedAt").GetString() ?? string.Empty,
                    ["ResourceKey"] = key!,
                    ["Etag"] = item.GetProperty("@odata.etag").GetString() ?? string.Empty
                };

                foreach (var prop in fields.EnumerateObject())
                {
                    if (prop.Name.StartsWith(LangPrefix, StringComparison.Ordinal) || prop.Name.StartsWith(SyncHashPrefix, StringComparison.Ordinal))
                    {
                        values[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }

                result.Add(key!, values);
            }
            
            url = json.RootElement.TryGetProperty("@odata.nextLink", out var nextLink) ? nextLink.GetString() : null;

        } while (!string.IsNullOrEmpty(url));

        return result;
    }

    internal static bool RequiresUpdate(Dictionary<string, string> existingFields, ResourceRow row)
    {
        foreach (var (culture, value) in row.Values)
        {
            existingFields.TryGetValue(GetCultureKey(culture), out var existingValue);

            if (!NormalizeValue(existingValue).Equals(NormalizeValue(value), StringComparison.Ordinal))
            {
                return true;
            }
        }

        var incomingKeys = row.Values.Keys.Select(GetCultureKey).ToHashSet();

        return existingFields.Keys.Any(k => k.StartsWith(LangPrefix, StringComparison.Ordinal) && !incomingKeys.Contains(k));
    }

    private static string GetCultureKey(CultureInfo culture)
        => culture.Equals(CultureInfo.InvariantCulture) ? $"{LangPrefix}default" : $"{LangPrefix}{culture.Name}";

    private static string NormalizeValue(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n");

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
            var langKey = GetCultureKey(culture);
            requestBody.fields.Add(langKey, value);
            requestBody.fields.Add(GetSyncHashKey(langKey), ComputeHash(NormalizeValue(value)));
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
    
    private static Dictionary<string, object> BuildSyncPatch(
        Dictionary<string, string> existingFields,
        ResourceRow row,
        out int initializedCount,
        out int repairedCount,
        out int protectedCount)
    {
        initializedCount = 0;
        repairedCount = 0;
        protectedCount = 0;

        var patch = new Dictionary<string, object>();
        var incomingLangKeys = new HashSet<string>();

        foreach (var (culture, value) in row.Values)
        {
            var langKey = GetCultureKey(culture);
            incomingLangKeys.Add(langKey);

            existingFields.TryGetValue(langKey, out var currentListValue);
            existingFields.TryGetValue(GetSyncHashKey(langKey), out var storedHash);

            var decision = EvaluateLanguageField(currentListValue, value, storedHash);

            if (decision.IsSafe && decision.HashToWrite is not null)
            {
                if (!NormalizeValue(currentListValue).Equals(NormalizeValue(value), StringComparison.Ordinal))
                {
                    patch[langKey] = value;
                }

                patch[GetSyncHashKey(langKey)] = decision.HashToWrite;

                if (decision.HashUpdate == HashUpdateKind.Initialize)
                    initializedCount++;
                else if (decision.HashUpdate == HashUpdateKind.Repair)
                    repairedCount++;
            }
            else if (!decision.IsSafe)
            {
                protectedCount++;
            }
        }

        foreach (var key in existingFields.Keys)
        {
            if (key.StartsWith(LangPrefix, StringComparison.Ordinal) && !incomingLangKeys.Contains(key))
            {
                patch[key] = string.Empty;
                patch[GetSyncHashKey(key)] = string.Empty;
            }
        }

        return patch;
    }

    private async Task UpdateListItem(string siteId, string listId, Dictionary<string, string> item, ResourceRow row, Dictionary<string, object> fieldPatch)
    {
        var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), row.BaseFile.FullName);
        relativePath = Path.ChangeExtension(relativePath, null);
        relativePath = relativePath.Replace("\\", "/");

        var fields = new Dictionary<string, object>(fieldPatch)
        {
            ["Path"] = relativePath,
            ["LastSyncedAt"] = DateTime.UtcNow.ToString("o")
        };

        var url = $"https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/items/{item["Id"]}/fields";
        
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Content = JsonContent.Create(fields);

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
                    if (!prop.Name.StartsWith(LangPrefix, StringComparison.Ordinal)) continue;
                    
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