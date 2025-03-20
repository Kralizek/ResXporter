using System.Globalization;

namespace ResXporter.Providers;

public enum Provider
{
    JetBrainsCsv,
    MicrosoftLists
}

public record ResourceRow(FileInfo BaseFile, string BaseName, string Key)
{
    public Dictionary<CultureInfo, string> Values { get; } = [];
}

public interface IExporter
{
    Task ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings);
}

public record ExportSettings
{
    public bool OnlyMissing { get; init; } = false;
    
    public CultureInfo[] Cultures { get; init; } = [];

    public Dictionary<string, string> Arguments { get; init; } = [];
}

public interface ILoader
{
    IAsyncEnumerable<ResourceRow> FetchAsync(LoaderSettings settings);
}

public record LoaderSettings
{
    public Dictionary<string, string> Arguments { get; init; } = [];
}