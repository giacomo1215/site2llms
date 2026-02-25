using System.Xml.Linq;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Discovers URLs from common sitemap endpoints.
/// Supports both <c>sitemapindex</c> and <c>urlset</c> documents.
/// </summary>
public class SitemapDiscovery(HttpClient http) : IUrlDiscovery
{
    /// <summary>
    /// Attempts sitemap discovery using well-known sitemap paths.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
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
                // Parse sitemap XML from candidate endpoint.
                var xml = await http.GetStringAsync(sm, ct);
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
                        all.AddRange(await ReadUrlset(sub, root, options, ct));
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
            catch { }
        }

        return Array.Empty<DiscoveredUrl>();
    }

    /// <summary>
    /// Reads one urlset sitemap and returns discovered URLs.
    /// </summary>
    private async Task<List<DiscoveredUrl>> ReadUrlset(Uri sitemapUrl, Uri root, CrawlOptions options, CancellationToken ct)
    {
        try
        {
            var xml = await http.GetStringAsync(sitemapUrl, ct);
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