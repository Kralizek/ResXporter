using System.Globalization;

namespace ResXporter.Exporters;

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