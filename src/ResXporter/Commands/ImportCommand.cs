using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources.NetStandard;

using Microsoft.Extensions.DependencyInjection;

using ResXporter.Loaders;

using Spectre.Console;
using Spectre.Console.Cli;

namespace ResXporter.Commands;

public class ImportCommand(IServiceProvider serviceProvider) : AsyncCommand<ImportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to the directory containing the resource files.")]
        [CommandOption("-p|--path <PATH>")]
        public DirectoryInfo Path { get; init; } = default!;
        
        [Description("The type of the loader to use.")]
        [CommandOption("-l|--loader <LOADER>")]
        public Loader Loader { get; init; } = default!;
        
        [Description("Update existing translations.")]
        [CommandOption("--update-existing")]
        [DefaultValue(false)]
        public bool UpdateExisting { get; init; }
        
        [Description("Additional arguments for the selected loader (key=value).")]
        [CommandOption("-a|--loader-arg <KEY=VALUE>")]
        public string[]? LoaderArgs { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var rows = await FetchRowsAsync(settings);

        var lookup = PivotRows(rows);

        var existingFiles = Directory.EnumerateFiles(settings.Path.FullName, "*.resx", SearchOption.AllDirectories)
            .Select(fileName => new FileInfo(fileName))
            .Select(file => (File: file, Info: ParseFileName(file)))
            .ToDictionary(f => f.Info, f => f.File);

        foreach (var baseName in lookup.Keys.Select(c => c.BaseName).Distinct())
        {
            var defaultFileKey = (baseName, CultureInfo.InvariantCulture);

            if (!existingFiles.ContainsKey(defaultFileKey) && lookup.ContainsKey(defaultFileKey))
            {
                var defaultFilePath = Path.Combine(settings.Path.FullName, $"{baseName}.resx");
                using var writer = new ResXResourceWriter(defaultFilePath);
                writer.Generate();
                writer.Close();
                
                var file = new FileInfo(defaultFilePath);
                existingFiles.Add(defaultFileKey, file);
                
                AnsiConsole.MarkupLine($"[yellow]Created missing file: {defaultFilePath}[/]");
            }
        }

        foreach (var ((baseName, culture), translations) in lookup)
        {
            if (!existingFiles.TryGetValue((baseName, culture), out var file))
            {
                if (translations.Count == 0)
                {
                    continue;
                }
                
                var fileName = culture.Equals(CultureInfo.InvariantCulture) ? $"{baseName}.resx" : $"{baseName}.{culture.Name}.resx";
                var filePath = Path.Combine(settings.Path.FullName, fileName);
                
                using var writer = new ResXResourceWriter(filePath);
                writer.Generate();
                writer.Close();
                
                file = new FileInfo(filePath);
                existingFiles.Add((baseName, culture), file);
                
                AnsiConsole.MarkupLine($"[yellow]Created missing file: {filePath}[/]");
            }
            
            var existingEntries = LoadExistingEntries(file);
            var changed = false;
            
            using var resxWriter = new ResXResourceWriter(file.FullName);
            var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in existingEntries)
            {
                resxWriter.AddResource(key, value);
            }

            foreach (var (key, value) in translations)
            {
                if (existingEntries.TryGetValue(key, out var existValue))
                {
                    if (settings.UpdateExisting && existValue != value)
                    {
                        resxWriter.AddResource(key, value);
                        addedKeys.Add(key);
                        changed = true;
                    }
                }
                else if (!addedKeys.Contains(key))
                {
                    resxWriter.AddResource(key, value);
                    changed = true;
                }
            }
            
            resxWriter.Generate();
            resxWriter.Close();
            
            if (changed)
            {
                AnsiConsole.MarkupLine($"[green]Updated file: {file.FullName}[/]");
            }
        }

        return 0;
    }

    private async Task<IReadOnlyList<ResourceRow>> FetchRowsAsync(Settings settings)
    {
        var loader = serviceProvider.GetRequiredKeyedService<ILoader>(settings.Loader);
        
        var args = settings.LoaderArgs?
            .Select(arg => arg.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase) ?? [];

        var loaderSettings = new LoaderSettings
        {
            Arguments = args
        };

        var rows = await loader.FetchAsync(loaderSettings).ToListAsync();

        return rows;
    }

    private static Dictionary<(string BaseName, CultureInfo Culture), Dictionary<string, string>> PivotRows(IReadOnlyList<ResourceRow> rows)
    {
        var lookup = rows
            .SelectMany(row => row.Values, (row, value) => new { row.BaseName, Culture = value.Key, row.Key, value.Value })
            .GroupBy(item => (item.BaseName, item.Culture))
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            );

        return lookup;
    }

    private static Dictionary<string, string> LoadExistingEntries(FileInfo file)
    {
        var existingEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!file.Exists || file.Length == 0)
        {
            return existingEntries;
        }

        try
        {
            using var reader = new ResXResourceReader(file.FullName);
        
            foreach (DictionaryEntry entry in reader)
            {
                existingEntries.Add(entry.Key.ToString()!, entry.Value?.ToString() ?? String.Empty);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load file '{file.FullName}'[/]: {ex.Message}");
        }
        
        return existingEntries;
    }

    private static (string BaseName, CultureInfo Culture) ParseFileName(FileInfo file)
    {
        var match = ResxFilePattern().Match(file.Name);
        
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid resource file name '{file.Name}'.");
        }
        
        var baseName = match.Groups["baseName"].Value;
        var cultureName = match.Groups["culture"].Success ? match.Groups["culture"].Value : null;
        var culture = cultureName == null ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureName);
        
        return (baseName, culture);
    }
}