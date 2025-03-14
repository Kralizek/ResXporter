using System.Globalization;

namespace ResXporter.Formats;

public interface IExportStrategy
{
    IAsyncEnumerable<FileInfo> ExportAsync(IReadOnlyList<ResourceRow> rows, ExportSettings settings);
}

public record ResourceRow(FileInfo BaseFile, string BaseName, string Key)
{
    public Dictionary<CultureInfo, string> Values { get; } = [];
}

public record ExportSettings
{
    public DirectoryInfo Output { get; init; } = default!;
    public bool OnlyMissing { get; init; } = false;
    
    public CultureInfo[] Cultures { get; init; } = [];
}