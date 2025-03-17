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
        
        var outputFile = GetOutputFile(settings);

        await using var writer = new StreamWriter(outputFile.FullName, false, Encoding.UTF8);
        await using var csv = new CsvWriter(writer, Configuration);
        
        await WriteHeader(csv, translationCultures);

        foreach (var row in rows.OrderBy(c => c.Key))
        {
            await WriteRow(csv, row, translationCultures);
        }

        await csv.FlushAsync();
        
        AnsiConsole.MarkupLine($"[gray]Exported {rows.Count} rows to {outputFile.FullName}[/]");
    }

    private static async Task WriteRow(CsvWriter csv, ResourceRow row, List<CultureInfo> translationCultures)
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

    private static async Task WriteHeader(CsvWriter csv, List<CultureInfo> translationCultures)
    {
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
    }

    private static FileInfo GetOutputFile(ExportSettings settings)
    {
        string defaultFileName = $"{nameof(Exporter.JetBrainsCsv)}-{(settings.OnlyMissing ? "partial" : "full")}.csv";

        var outputArgument = settings.Arguments.TryGetValue("output", out var customOutput)
            ? customOutput
            : Path.Combine(Directory.GetCurrentDirectory(), defaultFileName);
        
        var fullOutputPath = Path.GetFullPath(outputArgument);
        var isDirectory = Directory.Exists(fullOutputPath) || fullOutputPath.EndsWith(Path.DirectorySeparatorChar);

        var outputFilePath = isDirectory switch
        {
            true => Path.Combine(fullOutputPath, defaultFileName),
            false => fullOutputPath
        };
        
        var outputFile = new FileInfo(outputFilePath);
        outputFile.Directory?.Create();
        return outputFile;
    }
}