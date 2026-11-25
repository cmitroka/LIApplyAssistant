using System.Text.RegularExpressions;

namespace CjmApply.Core.Parsing;

public static partial class JobKeyBuilder
{
    public static string Build(string? jobTitle, string? company, string? location)
    {
        var raw = $"{jobTitle}{company}{location}";
        return AlphanumericOnly().Replace(raw, string.Empty).ToUpperInvariant();
    }

    [GeneratedRegex(@"[^A-Za-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex AlphanumericOnly();
}
