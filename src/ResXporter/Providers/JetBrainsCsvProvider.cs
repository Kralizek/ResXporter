using System.Globalization;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

using Spectre.Console;

namespace ResXporter.Providers;

public class JetBrainsCsvProvider : IExporter, ILoader
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
        string defaultFileName = $"{nameof(Provider.JetBrainsCsv)}-{(settings.OnlyMissing ? "partial" : "full")}.csv";

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

    public async IAsyncEnumerable<ResourceRow> FetchAsync(LoaderSettings settings)
    {
        var inputFile = GetInputFile(settings);

        if (!inputFile.Exists)
        {
            throw new FileNotFoundException($"CSV file not found: {inputFile.FullName}");
        }

        using var reader = new StreamReader(inputFile.FullName, Encoding.UTF8);
        using var csv = new CsvReader(reader, Configuration);
        
        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            throw new InvalidOperationException("CSV file is empty or has an invalid format.");
        }
        
        var cultureColumns = GetCultureColumns(csv.HeaderRecord!);

        await foreach (var row in ReadRowsAsync(csv, cultureColumns))
        {
            yield return row;
        }
    }
    
    private static async IAsyncEnumerable<ResourceRow> ReadRowsAsync(CsvReader csv, IReadOnlyList<(CultureInfo Culture, int ColumnIndex)> cultureColumns)
    {
        while (await csv.ReadAsync())
        {
            var path = csv.GetField(0) ?? string.Empty;
            var name = csv.GetField(1) ?? string.Empty;
            var defaultTranslation = csv.GetField(2) ?? string.Empty;

            var baseFile = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), $"{path}.resx"));
            var resourceRow = new ResourceRow(baseFile, Path.GetFileNameWithoutExtension(baseFile.Name), name);
            resourceRow.Values[CultureInfo.InvariantCulture] = defaultTranslation;

            foreach (var (culture, columnIndex) in cultureColumns)
            {
                var translation = csv.GetField(columnIndex) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(translation))
                {
                    resourceRow.Values[culture] = translation;
                }
            }

            yield return resourceRow;
        }
    }
    
    private static FileInfo GetInputFile(LoaderSettings settings)
    {
        if (settings.Arguments.TryGetValue("input", out var input))
        {
            var fullPath = Path.GetFullPath(input);
            return new FileInfo(fullPath);
        }
        
        throw new ArgumentException("Input file not specified.");
    }

    private static IReadOnlyList<(CultureInfo Culture, int ColumnIndex)> GetCultureColumns(string[] headers)
    {
        var cultures = new List<(CultureInfo, int)>();

        for (var i = 4; i < headers.Length; i++)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(headers[i]);
                cultures.Add((culture, i));
            }
            catch (CultureNotFoundException)
            {
                continue;
            }
        }

        return cultures;
    }
}