using System.Text.RegularExpressions;
using site2llms.Core.Models;

namespace site2llms.Core.Utils;

public static class UrlFilter
{
    public static bool IsAllowed(Uri url, CrawlOptions options)
    {
        var value = url.AbsoluteUri;

        // Check exclude patterns first, so they take precedence over include patterns.
        if (MatchesAny(value, options.ExcludePatterns)) return false;

        // No include patterns means all URLs are allowed (subject to exclude patterns).
        if (options.IncludePatterns is null || options.IncludePatterns.Count == 0) return true; 
        
        return MatchesAny(value, options.IncludePatterns);
    }

    private static bool MatchesAny(string value, IEnumerable<string>? patterns)
    { 
        if (patterns is null || patterns.Count() == 0) return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (MatchesPattern(value, pattern)) return true;
        }

        return false; 
    }

    private static bool MatchesPattern(string value, string pattern)
    {
         pattern = pattern.Trim();

        // Contains wildcard 
        if (pattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
        }

        // Case-insensitive contains
        return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}