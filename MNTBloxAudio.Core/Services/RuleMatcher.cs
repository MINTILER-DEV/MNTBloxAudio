using System.Text.RegularExpressions;

namespace MNTBloxAudio.Core.Services;

public static class RuleMatcher
{
    public static bool Matches(string pattern, string assetId)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(assetId))
        {
            return false;
        }

        var wildcardPattern = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(assetId, wildcardPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
