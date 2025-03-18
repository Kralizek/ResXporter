using System.Globalization;
using System.Text;

using CsvHelper;
using CsvHelper.Configuration;

namespace ResXporter.Loaders;

public class JetBrainsCsvLoader : ILoader
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture) { Delimiter = ";" };
    
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