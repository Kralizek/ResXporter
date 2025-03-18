using System.Globalization;

namespace ResXporter;

public enum Exporter
{
    JetBrainsCsv,
    MicrosoftLists
}

public enum Loader
{
    
}

public record ResourceRow(FileInfo BaseFile, string BaseName, string Key)
{
    public Dictionary<CultureInfo, string> Values { get; } = [];
}