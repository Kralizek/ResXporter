using System.Globalization;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

using Spectre.Console;

namespace ResXporter.Exporters;

public class JetBrainsCsvExporter : IExporter
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture) { Delimiter = ";" };

    public async Task ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings)
    {
        var translationCultures = settings.Cultures
            .OrderBy(c => c.Name)
            .ToList();

        settings.Output.Create();

        var csvPath = Path.Combine(settings.Output.FullName, $"{nameof(Exporter.JetBrainsCsv)}-{(settings.OnlyMissing ? "partial" : "full")}.csv");

        await using var writer = new StreamWriter(csvPath, false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, Configuration);
        
        csv.WriteField("Path");
        csv.WriteField("Name");
        
        csv.WriteField("Default Culture");
        csv.WriteField("Comment");
        
        foreach (var culture in translationCultures)
        {
            csv.WriteField(culture.Name);
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
            
            csv.WriteField(row.Values[CultureInfo.InvariantCulture]);
            csv.WriteField(string.Empty);
            
            foreach (var culture in translationCultures)
            {
                row.Values.TryGetValue(culture, out var translation);
                csv.WriteField(translation ?? string.Empty);
                csv.WriteField(string.Empty);
            }
            
            await csv.NextRecordAsync();
        }

        await csv.FlushAsync();
        
        AnsiConsole.MarkupLine($"[gray]Exported {rows.Count} rows to {csvPath}[/]");
    }
}