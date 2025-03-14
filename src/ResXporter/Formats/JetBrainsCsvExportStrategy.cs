using System.Globalization;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

namespace ResXporter.Formats;

public class JetBrainsCsvExportStrategy(TimeProvider timeProvider) : IExportStrategy
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture) { Delimiter = ";" };

    public async IAsyncEnumerable<FileInfo> ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings)
    {
        var cultures = rows
            .SelectMany(r => r.Values.Keys)
            .Distinct()
            .ToHashSet();

        var orderedCultures = cultures
            .OrderBy(c => c.Equals(CultureInfo.InvariantCulture) ? 0 : 1)
            .ThenBy(c => c.Name)
            .ToList();

        settings.Output.Create();

        var csvPath = Path.Combine(settings.Output.FullName, $"{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}-{(settings.OnlyMissing ? "partial" : "full")}.csv");

        await using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, Configuration);
        
        csv.WriteField("Path");
        csv.WriteField("Name");
        
        foreach (var culture in orderedCultures)
        {
            var columnName = culture.Equals(CultureInfo.InvariantCulture) ? "Default Culture" : culture.Name;
            
            csv.WriteField(columnName);
            csv.WriteField("Comment");
        }
        
        await csv.NextRecordAsync();

        foreach (var row in rows.OrderBy(c => c.Key))
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), row.BaseFile.FullName);
            relativePath = Path.ChangeExtension(relativePath, null);
            relativePath = relativePath.Replace("\\", "/");
            
            csv.WriteField(relativePath);
            csv.WriteField(row.Key);
            
            foreach (var culture in orderedCultures)
            {
                row.Values.TryGetValue(culture, out var translation);
                csv.WriteField(translation ?? string.Empty);
                csv.WriteField(string.Empty);
            }
            
            await csv.NextRecordAsync();
        }

        await csv.FlushAsync();
        
        yield return new FileInfo(csvPath);
    }
}