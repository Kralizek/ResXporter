using System.Globalization;

namespace ResXporter.Loaders;

public interface ILoader
{
    IAsyncEnumerable<ResourceRow> FetchAsync(LoaderSettings settings);
}

public record LoaderSettings
{
    public Dictionary<string, string> Arguments { get; init; } = [];
}