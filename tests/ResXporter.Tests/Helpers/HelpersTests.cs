using System.Globalization;

using NUnit.Framework;

namespace ResXporter.Tests.Helpers;

public class GetRequiredValueTests
{
    [Test]
    public void Returns_value_when_key_exists()
    {
        var dict = new Dictionary<string, string> { ["key"] = "value" };
        Assert.That(dict.GetRequiredValue("key"), Is.EqualTo("value"));
    }

    [Test]
    public void Throws_when_key_is_missing()
    {
        var dict = new Dictionary<string, string>();
        Assert.Throws<ArgumentException>(() => dict.GetRequiredValue("missing"));
    }

    [Test]
    public void Exception_message_contains_key_name()
    {
        var dict = new Dictionary<string, string>();
        var ex = Assert.Throws<ArgumentException>(() => dict.GetRequiredValue("myKey"));
        Assert.That(ex!.Message, Does.Contain("myKey"));
    }
}

public class GetOptionalValueTests
{
    [Test]
    public void Returns_value_when_key_exists()
    {
        var dict = new Dictionary<string, string> { ["key"] = "hello" };
        Assert.That(dict.GetOptionalValue("key"), Is.EqualTo("hello"));
    }

    [Test]
    public void Returns_null_when_key_is_missing()
    {
        var dict = new Dictionary<string, string>();
        Assert.That(dict.GetOptionalValue("missing"), Is.Null);
    }
}

public class GetBooleanValueTests
{
    [Test]
    public void Returns_true_when_value_is_true_string()
    {
        var dict = new Dictionary<string, string> { ["flag"] = "true" };
        Assert.That(dict.GetBooleanValue("flag"), Is.True);
    }

    [Test]
    public void Returns_true_when_value_is_True_mixed_case()
    {
        var dict = new Dictionary<string, string> { ["flag"] = "True" };
        Assert.That(dict.GetBooleanValue("flag"), Is.True);
    }

    [Test]
    public void Returns_false_when_value_is_false_string()
    {
        var dict = new Dictionary<string, string> { ["flag"] = "false" };
        Assert.That(dict.GetBooleanValue("flag"), Is.False);
    }

    [Test]
    public void Returns_false_when_key_is_missing()
    {
        var dict = new Dictionary<string, string>();
        Assert.That(dict.GetBooleanValue("flag"), Is.False);
    }

    [Test]
    public void Returns_false_when_value_is_not_a_boolean()
    {
        var dict = new Dictionary<string, string> { ["flag"] = "yes" };
        Assert.That(dict.GetBooleanValue("flag"), Is.False);
    }
}

public class TryGetCultureInfoTests
{
    [Test]
    public void Returns_true_and_culture_for_valid_culture_name()
    {
        var result = ResXporter.Helpers.TryGetCultureInfo("fr-FR", out var culture);
        Assert.That(result, Is.True);
        Assert.That(culture, Is.Not.Null);
        Assert.That(culture!.Name, Is.EqualTo("fr-FR"));
    }

    [Test]
    public void Returns_true_for_two_letter_culture()
    {
        var result = ResXporter.Helpers.TryGetCultureInfo("de", out var culture);
        Assert.That(result, Is.True);
        Assert.That(culture, Is.Not.Null);
    }

    [Test]
    public void Returns_false_for_invalid_culture_name()
    {
        // Names with invalid characters always throw CultureNotFoundException
        var result = ResXporter.Helpers.TryGetCultureInfo("INVALID!!CULTURE", out var culture);
        Assert.That(result, Is.False);
        Assert.That(culture, Is.Null);
    }
}

public class ResxFilePatternTests
{
    [TestCase("Resources.resx", "Resources", null)]
    [TestCase("Resources.fr-FR.resx", "Resources", "fr-FR")]
    [TestCase("MyApp.Strings.resx", "MyApp.Strings", null)]
    [TestCase("MyApp.Strings.de-DE.resx", "MyApp.Strings", "de-DE")]
    [TestCase("Localization.zh-Hans.resx", "Localization", "zh-Hans")]
    public void Matches_resx_file_names(string fileName, string expectedBaseName, string? expectedCulture)
    {
        var match = ResXporter.Helpers.ResxFilePattern().Match(fileName);
        Assert.That(match.Success, Is.True);
        Assert.That(match.Groups["baseName"].Value, Is.EqualTo(expectedBaseName));

        if (expectedCulture is null)
        {
            Assert.That(match.Groups["culture"].Success, Is.False);
        }
        else
        {
            Assert.That(match.Groups["culture"].Value, Is.EqualTo(expectedCulture));
        }
    }

    [TestCase("notaresx.txt")]
    [TestCase("noextension")]
    public void Does_not_match_non_resx_files(string fileName)
    {
        var match = ResXporter.Helpers.ResxFilePattern().Match(fileName);
        Assert.That(match.Success, Is.False);
    }
}
