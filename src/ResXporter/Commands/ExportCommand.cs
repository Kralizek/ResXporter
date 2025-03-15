using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources.NetStandard;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using ResXporter.Exporters;

using Spectre.Console;
using Spectre.Console.Cli;

namespace ResXporter.Commands;

public partial class ExportCommand(IServiceProvider serviceProvider) : AsyncCommand<ExportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the directory containing the .resx files.")]
        [CommandOption("--path <PATH>")]
        public DirectoryInfo Path { get; init; } = default!;
        
        [Description("List of .resx files to export.")]
        [CommandOption("--file <FILE>")]
        public string[]? Files { get; init; }
        
        [Description("Path to the directory where the .resx files will be exported.")]
        [CommandOption("--output <OUTPUT>")]
        public DirectoryInfo Output { get; init; } = default!;
        
        [Description("Export only keys that have at least one missing translation.")]
        [CommandOption("--only-missing")]
        [DefaultValue(false)]
        public bool OnlyMissing { get; init; }

        [Description("The exporters to be used to process the resx files.")]
        [CommandOption("--exporter <FORMAT>")]
        public Exporter[] Exporters { get; init; } = [];

        public override ValidationResult Validate()
        {
            if (!Path.Exists)
            {
                return ValidationResult.Error("Path does not exist.");
            }

            if (!Output.Parent?.Exists ?? false)
            {
                return ValidationResult.Error("Output directory does not exist.");
            }

            if (Files is not null or [])
            {
                foreach (var file in Files)
                {
                    var filePath = new FileInfo(System.IO.Path.Combine(Path.FullName, $"{file}.resx"));
                    if (!filePath.Exists)
                    {
                        return ValidationResult.Error($"File '{filePath}' does not exist in '{Path.FullName}'.");
                    }
                }
            }

            if (Exporters is [])
            {
                return ValidationResult.Error("No exporters specified.");
            }
            
            return base.Validate();
        }
    }

    [GeneratedRegex(@"^(?<baseName>.+?)(?:\.(?<culture>[a-z]{2}(-[A-Z]{2,4})?))?\.resx$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ResxFilePattern();

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var timestamp = Stopwatch.GetTimestamp();
        
        var files = CollectResourceFiles(settings).ToArray();

        var data = LoadFiles(files);

        var translationCultures = data.Keys.Select(c => c.Culture)
            .Where(c => c != null)
            .Distinct()
            .Cast<CultureInfo>()
            .ToArray();

        var rows = MergeResources(data);

        if (settings.OnlyMissing)
        {
            rows = rows.Where(row => IsMissingAtLeastOneTranslation(row, translationCultures));
        }

        var items = rows.ToArray();
        
        var exportSettings = new ExportSettings
        {
            Output = settings.Output,
            OnlyMissing = settings.OnlyMissing,
            Cultures = translationCultures
        };
        
        foreach (var exporter in settings.Exporters)
        {
            var exporterService = serviceProvider.GetRequiredKeyedService<IExporter>(exporter);
            await exporterService.ExportAsync(items, exportSettings);
        }
        
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        
        AnsiConsole.MarkupLine("[green]Export completed successfully![/]");
        
        AnsiConsole.MarkupLine($"[gray]Elapsed time: {elapsed:g}[/]");

        return 0;
    }

    private static IEnumerable<FileInfo> CollectResourceFiles(Settings settings)
    {
        if (settings.Files is not { Length: > 0 })
        {
            foreach (var file in Directory.EnumerateFiles(settings.Path.FullName, "*.resx", SearchOption.TopDirectoryOnly))
            {
                yield return new FileInfo(file);
            }

            yield break;
        }

        var wantedBaseNames = new HashSet<string>(settings.Files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(settings.Path.FullName, "*.resx", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            var match = ResxFilePattern().Match(fileName);

            if (!match.Success)
            {
                continue;
            }

            var baseName = match.Groups["baseName"].Value;

            if (wantedBaseNames.Contains(baseName))
            {
                yield return new FileInfo(file);
            }
        }
    }

    private static Dictionary<(FileInfo BaseFile, CultureInfo? Culture), Dictionary<string, string>> LoadFiles(IEnumerable<FileInfo> files)
    {
        var data = new Dictionary<(FileInfo BaseFile, CultureInfo? Culture), Dictionary<string, string>>();

        foreach (var file in files)
        {
            var match = ResxFilePattern().Match(file.Name);

            if (!match.Success)
            {
                continue;
            }
            
            var baseName = match.Groups["baseName"].Value;
            var cultureName = match.Groups["culture"].Success ? match.Groups["culture"].Value : null;

            var baseFilePath = Path.Combine(file.Directory!.FullName, $"{baseName}.resx");
            var baseFile = new FileInfo(baseFilePath);

            if (string.IsNullOrEmpty(cultureName))
            {
                data[(baseFile, Culture: null)] = LoadResourceFile(file);    
            }
            else if (TryGetCultureInfo(cultureName, out var culture))
            {
                data[(baseFile, culture)] = LoadResourceFile(file);
            }
        }

        return data;
    }

    private static Dictionary<string, string> LoadResourceFile(FileInfo file)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        using var reader = new ResXResourceReader(file.FullName);

        foreach (DictionaryEntry entry in reader)
        {
            var key = entry.Key.ToString();
            var value = entry.Value?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(key))
            {
                result[key] = value;
            }
        }
        
        return result;
    }
    
    private static IEnumerable<ResourceRow> MergeResources(Dictionary<(FileInfo BaseFile, CultureInfo? Culture), Dictionary<string, string>> data)
    {
        var results = new Dictionary<(string baseName, string key), ResourceRow>();
        
        foreach (var ((baseFile, culture), resources) in data)
        {
            var baseName = baseFile.Name;
            
            foreach (var (key, value) in resources)
            {
                var rowKey = (baseName, key);

                if (!results.TryGetValue(rowKey, out var row))
                {
                    row = new ResourceRow(baseFile, baseName, key);
                    
                    results.Add(rowKey, row);
                }
                
                row.Values.Add(culture ?? CultureInfo.InvariantCulture, value);
            }
        }

        return results.Values;
    }
    
    private static bool IsMissingAtLeastOneTranslation(ResourceRow row, IEnumerable<CultureInfo?> cultures)
    {
        return cultures.Any(culture => !row.Values.ContainsKey(culture ?? CultureInfo.InvariantCulture));
    }
    
    private static bool TryGetCultureInfo(string cultureName, [NotNullWhen(true)] out CultureInfo? culture)
    {
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName);
            return true;
        }
        catch
        {
            culture = null;
            return false;
        }
    }
}
