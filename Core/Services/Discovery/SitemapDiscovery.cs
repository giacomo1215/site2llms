using System.Net;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Discovers URLs from common sitemap endpoints.
/// Supports both <c>sitemapindex</c> and <c>urlset</c> documents.
/// </summary>
public class SitemapDiscovery(HttpClient http, ILogger<SitemapDiscovery> logger, PlaywrightSession? session = null) : IUrlDiscovery
{
    private readonly ILogger<SitemapDiscovery> _logger = logger;

    /// <summary>
    /// Attempts sitemap discovery using well-known sitemap paths.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
        var useBrowserForSitemaps = false;
        var candidates = new[]
        {
            new Uri(root, "/sitemap.xml"),
            new Uri(root, "/sitemap_index.xml"),
            new Uri(root, "/wp-sitemap.xml"),
        };

        foreach (var sm in candidates)
        {
            try
            {
                var result = await LoadSitemapXmlAsync(sm, useBrowserForSitemaps, ct);
                if (result.Blocked && !useBrowserForSitemaps && session is not null)
                {
                    useBrowserForSitemaps = true;
                    result = await LoadSitemapXmlAsync(sm, true, ct);
                }

                var xml = result.Xml;
                if (string.IsNullOrWhiteSpace(xml)) continue;

                var doc = XDocument.Parse(xml);

                // If this is a sitemap index, recurse into each sub-sitemap.
                var subs = doc.Descendants().Where(x => x.Name.LocalName == "sitemap")
                    .Select(x => x.Descendants().FirstOrDefault(d => d.Name.LocalName == "loc")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => new Uri(v!))
                    .ToList();

                if (subs.Count > 0)
                {
                    var all = new List<DiscoveredUrl>();
                    foreach (var sub in subs)
                    {
                        all.AddRange(await ReadUrlset(sub, root, options, useBrowserForSitemaps, ct));
                    }

                    // Return bounded, de-duplicated URL list.
                    return all
                        .DistinctBy(x => x.Url.AbsoluteUri)
                        .Take(options.MaxPages)
                        .ToList();
                }

                // Otherwise treat the document as a direct URL set.
                var urls = doc.Descendants().Where(x => x.Name.LocalName == "url")
                    .Select(x => x.Descendants().FirstOrDefault(d => d.Name.LocalName == "loc")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => TryBuild(v!, root, options))
                    .Where(u => u is not null)
                    .Select(u => new DiscoveredUrl(u!, "sitemap", 0))
                    .DistinctBy(x => x.Url.AbsoluteUri)
                    .Take(options.MaxPages)
                    .ToList();

                if (urls.Count > 0) return urls;
            }
            // Any malformed/missing sitemap should not stop fallback strategies.
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sitemap discovery failed for {SitemapUrl}", sm);
            }
        }

        return Array.Empty<DiscoveredUrl>();
    }

    /// <summary>
    /// Reads one urlset sitemap and returns discovered URLs.
    /// </summary>
    private async Task<List<DiscoveredUrl>> ReadUrlset(Uri sitemapUrl, Uri root, CrawlOptions options, bool useBrowserForSitemaps, CancellationToken ct)
    {
        try
        {
            var xml = (await LoadSitemapXmlAsync(sitemapUrl, useBrowserForSitemaps, ct)).Xml;
            if (string.IsNullOrWhiteSpace(xml))
            {
                return [];
            }

            var doc = XDocument.Parse(xml);

            return doc.Descendants().Where(x => x.Name.LocalName == "url")
                .Select(x => x.Descendants().FirstOrDefault(d => d.Name.LocalName == "loc")?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => TryBuild(v!, root, options))
                .Where(u => u is not null)
                .Select(u => new DiscoveredUrl(u!, "sitemap", 0))
                .ToList();
        }
        // Keep sitemap ingestion resilient: one bad sub-sitemap should not fail the whole set.
        catch { return []; }
    }

    /// <summary>
    /// Loads sitemap XML via HTTP first, then falls back to Playwright when the site blocks plain requests.
    /// </summary>
    private async Task<(string? Xml, bool Blocked)> LoadSitemapXmlAsync(Uri sitemapUrl, bool forceBrowser, CancellationToken ct)
    {
        if (!forceBrowser)
        {
            using var response = await http.GetAsync(sitemapUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.IsSuccessStatusCode)
            {
                return (await response.Content.ReadAsStringAsync(ct), false);
            }

            if (!IsBlocked(response.StatusCode) || session is null)
            {
                return (null, false);
            }

            _logger.LogInformation("Sitemap blocked with HTTP {StatusCode}; switching remaining sitemap requests to Playwright", (int)response.StatusCode);
        }

        if (session is null)
        {
            return (null, forceBrowser);
        }

        var pwResponse = await session.GetAsync(sitemapUrl.AbsoluteUri, ct);
        if (pwResponse is null || !pwResponse.IsSuccess || string.IsNullOrWhiteSpace(pwResponse.Body))
        {
            return (null, forceBrowser);
        }

        return (pwResponse.Body, false);
    }

    private static bool IsBlocked(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.ServiceUnavailable;
    }

    /// <summary>
    /// Validates and canonicalizes a sitemap URL candidate according to run options.
    /// </summary>
    private static Uri? TryBuild(string value, Uri root, CrawlOptions options)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url))
        {
            return null;
        }

        url = UrlUtils.Canonicalize(url);
        if (!UrlUtils.IsHttp(url))
        {
            return null;
        }

        if (options.SameHostOnly && !url.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return url;
    }
}