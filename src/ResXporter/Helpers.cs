global using static ResXporter.Helpers;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ResXporter;

public static partial class Helpers
{
    [GeneratedRegex(@"^(?<baseName>.+?)(?:\.(?<culture>[a-z]{2}(-[A-Z]{2,4})?))?\.resx$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex ResxFilePattern();
    
    public static string GetRequiredValue(this IDictionary<string, string> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"The value for key '{key}' is required.");
        }

        return value;
    }
    
    public static string? GetOptionalValue(this IDictionary<string, string> dictionary, string key)
    {
        dictionary.TryGetValue(key, out var value);

        return value;
    }
    
    public static bool GetBooleanValue(this IDictionary<string, string> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var stringValue) && bool.TryParse(stringValue, out var value) && value;
    }
    
    public static bool TryGetCultureInfo(string cultureName, [NotNullWhen(true)] out CultureInfo? culture)
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