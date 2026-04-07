using System.Globalization;
using System.Text;

using NUnit.Framework;

using ResXporter.Providers;

namespace ResXporter.Tests.Providers.JetBrains;

public class JetBrainsCsvProviderExportTests
{
    private static ResourceRow MakeRow(string key, params (CultureInfo culture, string value)[] values)
    {
        var row = new ResourceRow(new FileInfo(Path.Combine(Path.GetTempPath(), "Resources.resx")), "Resources", key);
        foreach (var (culture, value) in values)
            row.Values.Add(culture, value);
        return row;
    }

    private static ExportSettings MakeSettings(string outputPath, bool onlyMissing = false, CultureInfo[]? cultures = null) =>
        new()
        {
            OnlyMissing = onlyMissing,
            Cultures = cultures ?? [],
            Arguments = new Dictionary<string, string> { ["output"] = outputPath }
        };

    [Test]
    public async Task Export_creates_csv_with_header_row()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var provider = new JetBrainsCsvProvider();
            var row = MakeRow("Greeting", (CultureInfo.InvariantCulture, "Hello"));
            var cultures = new[] { CultureInfo.GetCultureInfo("fr-FR") };

            await provider.ExportAsync([row], MakeSettings(outputFile, cultures: cultures));

            var lines = await File.ReadAllLinesAsync(outputFile);
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(lines[0], Does.Contain("Path"));
            Assert.That(lines[0], Does.Contain("Name"));
            Assert.That(lines[0], Does.Contain("Default Culture"));
            Assert.That(lines[0], Does.Contain("fr-FR"));
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Test]
    public async Task Export_writes_row_with_key_and_default_value()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var provider = new JetBrainsCsvProvider();
            var row = MakeRow("Greeting", (CultureInfo.InvariantCulture, "Hello"));

            await provider.ExportAsync([row], MakeSettings(outputFile));

            var content = await File.ReadAllTextAsync(outputFile);
            Assert.That(content, Does.Contain("Greeting"));
            Assert.That(content, Does.Contain("Hello"));
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Test]
    public async Task Export_writes_translation_for_each_culture()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var provider = new JetBrainsCsvProvider();
            var frFR = CultureInfo.GetCultureInfo("fr-FR");
            var row = MakeRow("Greeting",
                (CultureInfo.InvariantCulture, "Hello"),
                (frFR, "Bonjour"));

            await provider.ExportAsync([row], MakeSettings(outputFile, cultures: [frFR]));

            var content = await File.ReadAllTextAsync(outputFile);
            Assert.That(content, Does.Contain("Bonjour"));
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Test]
    public async Task Export_writes_empty_string_for_missing_translation()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var provider = new JetBrainsCsvProvider();
            var frFR = CultureInfo.GetCultureInfo("fr-FR");
            var row = MakeRow("Greeting", (CultureInfo.InvariantCulture, "Hello")); // no fr-FR translation

            await provider.ExportAsync([row], MakeSettings(outputFile, cultures: [frFR]));

            var lines = await File.ReadAllLinesAsync(outputFile);
            // Data row: Path;Name;Hello;;;<empty fr-FR>;;
            Assert.That(lines.Length, Is.EqualTo(2)); // header + 1 data row
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Test]
    public async Task Export_sorts_rows_by_key()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var provider = new JetBrainsCsvProvider();
            var rows = new[]
            {
                MakeRow("Zebra", (CultureInfo.InvariantCulture, "Z")),
                MakeRow("Apple", (CultureInfo.InvariantCulture, "A")),
                MakeRow("Mango", (CultureInfo.InvariantCulture, "M")),
            };

            await provider.ExportAsync(rows, MakeSettings(outputFile));

            var lines = await File.ReadAllLinesAsync(outputFile);
            // Skip header; rows should be alphabetically sorted
            Assert.That(lines[1], Does.Contain("Apple"));
            Assert.That(lines[2], Does.Contain("Mango"));
            Assert.That(lines[3], Does.Contain("Zebra"));
        }
        finally
        {
            File.Delete(outputFile);
        }
    }
}

public class JetBrainsCsvProviderFetchTests
{
    private static async Task<string> WriteCsvAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        return path;
    }

    private static LoaderSettings MakeSettings(string inputPath) =>
        new() { Arguments = new Dictionary<string, string> { ["input"] = inputPath } };

    [Test]
    public async Task Fetch_returns_rows_from_csv()
    {
        var csv =
            "Path;Name;Default Culture;Comment;fr-FR;Comment\n" +
            "Resources;Greeting;Hello;;Bonjour;\n";
        var path = await WriteCsvAsync(csv);
        try
        {
            var provider = new JetBrainsCsvProvider();
            var rows = await provider.FetchAsync(MakeSettings(path)).ToListAsync();

            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Key, Is.EqualTo("Greeting"));
            Assert.That(rows[0].Values[CultureInfo.InvariantCulture], Is.EqualTo("Hello"));
            Assert.That(rows[0].Values[CultureInfo.GetCultureInfo("fr-FR")], Is.EqualTo("Bonjour"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Fetch_skips_empty_translations()
    {
        var csv =
            "Path;Name;Default Culture;Comment;fr-FR;Comment\n" +
            "Resources;Greeting;Hello;;;\n";
        var path = await WriteCsvAsync(csv);
        try
        {
            var provider = new JetBrainsCsvProvider();
            var rows = await provider.FetchAsync(MakeSettings(path)).ToListAsync();

            Assert.That(rows[0].Values.ContainsKey(CultureInfo.GetCultureInfo("fr-FR")), Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Fetch_ignores_comment_columns()
    {
        var csv =
            "Path;Name;Default Culture;Comment;fr-FR;Comment\n" +
            "Resources;Key1;Value;;Trans;This is a comment\n";
        var path = await WriteCsvAsync(csv);
        try
        {
            var provider = new JetBrainsCsvProvider();
            var rows = await provider.FetchAsync(MakeSettings(path)).ToListAsync();

            // Should have the fr-FR translation
            Assert.That(rows[0].Values.ContainsKey(CultureInfo.GetCultureInfo("fr-FR")), Is.True);
            Assert.That(rows[0].Values[CultureInfo.GetCultureInfo("fr-FR")], Is.EqualTo("Trans"));
            // Default culture value is correct
            Assert.That(rows[0].Values[CultureInfo.InvariantCulture], Is.EqualTo("Value"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Fetch_handles_multiple_cultures()
    {
        var csv =
            "Path;Name;Default Culture;Comment;fr-FR;Comment;de-DE;Comment\n" +
            "Resources;Greeting;Hello;;Bonjour;;Hallo;\n";
        var path = await WriteCsvAsync(csv);
        try
        {
            var provider = new JetBrainsCsvProvider();
            var rows = await provider.FetchAsync(MakeSettings(path)).ToListAsync();

            Assert.That(rows[0].Values[CultureInfo.GetCultureInfo("fr-FR")], Is.EqualTo("Bonjour"));
            Assert.That(rows[0].Values[CultureInfo.GetCultureInfo("de-DE")], Is.EqualTo("Hallo"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Fetch_throws_when_file_does_not_exist()
    {
        var provider = new JetBrainsCsvProvider();
        var settings = MakeSettings("/nonexistent/path/file.csv");

        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await provider.FetchAsync(settings).ToListAsync());
    }

    [Test]
    public void Fetch_throws_when_input_argument_is_missing()
    {
        var provider = new JetBrainsCsvProvider();
        var settings = new LoaderSettings { Arguments = [] };

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await provider.FetchAsync(settings).ToListAsync());
    }
}

// Helper to materialise IAsyncEnumerable without System.Linq.Async dependency
internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
