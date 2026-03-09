using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
//using AngleSharp.Dom;

namespace site2llms.Core.Utils;

/// <summary>
/// URL normalization and transformation helpers used throughout discovery/output.
/// </summary>
public static class UrlUtils
{
    
    #region Actions

    #region Transformations
    /// <summary>
    /// Normalizes the query string by removing tracking parameters and sorting the remaining parameters.
    /// </summary>
    /// <param name="query">The query string to normalize.</param>
    /// <returns>The normalized query string.</returns>
    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        var pairs = trimmed
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var idx = part.IndexOf('=');
                if (idx < 0)
                {
                    var keyOnly = Uri.UnescapeDataString(part);
                    return new KeyValuePair<string, string>(keyOnly, string.Empty);
                }

                var key = Uri.UnescapeDataString(part[..idx]);
                var value = Uri.UnescapeDataString(part[(idx + 1)..]);
                return new KeyValuePair<string, string>(key, value);
            })
            .Where(kvp => !IsTrackingParameter(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Value, StringComparer.Ordinal);

        return string.Join("&", pairs.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
    }

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

    #endregion

    #region Output    
    /// <summary>
    /// Canonicalizes URI by removing fragment so URL identity is stable for dedup/cache.
    /// </summary>
    public static Uri Canonicalize(Uri u)
    {
        // var b = new UriBuilder(u);
        // b.Fragment = "";
        // return b.Uri;

        var builder = new UriBuilder(u)
        {
            Fragment = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80) || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1; // Default port, can be omitted
        }

        var path = builder.Path;
        if (string.IsNullOrWhiteSpace(path)) path = "/";
        else if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');

        builder.Path = path;
        builder.Query = NormalizeQuery(builder.Query);

        return builder.Uri;

        // var parsed = QueryHelpers.ParseQuery(builder.Query);
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

    /// <summary>
    /// Builds a stable filename for a given URL by combining a slug from the path/query and a hash of the canonical URL.
    /// </summary>
    /// <param name="url">Input URL to generate filename for.</param>
    /// <returns>Stable filename string.</returns>
    public static string BuildStableFileName(Uri url)
    {
        var canonical = Canonicalize(url).AbsoluteUri;
        var slug = SlugFromUrl(url);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant()?[..8];

        return $"{slug}_{hash}.md";
    }
    #endregion  
    
    #endregion  
    
    #region Checks

    /// <summary>
    /// Returns true when URI uses HTTP/S protocol.
    /// </summary>
    public static bool IsHttp(Uri u) => u.Scheme is "http" or "https";
    
    /// <summary>
    /// Returns true when query parameter is a known tracking/analytics parameter we want to ignore for canonicalization.
    /// </summary>
    /// <param name="key">Query parameter key.</param>
    /// <returns>True when parameter is a known tracking/analytics parameter.</returns>
    private static bool IsTrackingParameter(string key)
    {
        return key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "fbclid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "gclid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(key, "msclkid", StringComparison.OrdinalIgnoreCase);
    }
    #endregion
}