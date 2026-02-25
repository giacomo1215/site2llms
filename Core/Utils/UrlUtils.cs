using System.Text.RegularExpressions;

namespace site2llms.Core.Utils;

/// <summary>
/// URL normalization and transformation helpers used throughout discovery/output.
/// </summary>
public static class UrlUtils
{
    /// <summary>
    /// Returns true when URI uses HTTP/S protocol.
    /// </summary>
    public static bool IsHttp(Uri u) => u.Scheme is "http" or "https";

    /// <summary>
    /// Converts a link href into an absolute URI while ignoring non-navigational schemes.
    /// </summary>
    /// <param name="siteRoot">Site root URL (reserved for future filtering behavior).</param>
    /// <param name="current">Current page URL used for relative resolution.</param>
    /// <param name="href">Raw href attribute value.</param>
    /// <returns>Resolved absolute URI or null when href should be ignored/invalid.</returns>
    public static Uri? ToAbsolute(Uri siteRoot, Uri current, string href)
    {
        href = href.Trim();
        if (href.StartsWith("#")) return null;
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
        if (href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs;
            return new Uri(current, href);
        }
        catch { return null; }
    }

    /// <summary>
    /// Canonicalizes URI by removing fragment so URL identity is stable for dedup/cache.
    /// </summary>
    public static Uri Canonicalize(Uri u)
    {
        var b = new UriBuilder(u);
        b.Fragment = "";
        return b.Uri;
    }

    /// <summary>
    /// Converts host into a filesystem-friendly folder segment.
    /// </summary>
    public static string SafeHost(Uri u) => u.Host.Replace(":", "_");

    /// <summary>
    /// Produces a deterministic filename slug from URL path and query.
    /// </summary>
    /// <remarks>
    /// This keeps only lowercase alphanumeric, underscore and hyphen, then flattens slashes.
    /// </remarks>
    public static string SlugFromUrl(Uri u)
    {
        var path = u.AbsolutePath.Trim('/').ToLowerInvariant();
        var query = u.Query.TrimStart('?').ToLowerInvariant();
        var combined = string.IsNullOrWhiteSpace(query) ? path : $"{path}_{query}";

        if (string.IsNullOrWhiteSpace(combined)) combined = "home";

        combined = Regex.Replace(combined, @"[^a-z0-9/_-]+", "_");
        combined = combined.Replace("/", "_");
        combined = Regex.Replace(combined, @"_{2,}", "_").Trim('_');

        return string.IsNullOrWhiteSpace(combined) ? "home" : combined;
    }
}