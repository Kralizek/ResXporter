global using static ResXporter.Helpers;

using System.Text.RegularExpressions;

namespace ResXporter;

public static partial class Helpers
{
    [GeneratedRegex(@"^(?<baseName>.+?)(?:\.(?<culture>[a-z]{2}(-[A-Z]{2,4})?))?\.resx$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    public static partial Regex ResxFilePattern();
}