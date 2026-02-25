using System.ServiceModel.Syndication;
using System.Xml;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Discovers URLs from common RSS/Atom feed endpoints.
/// </summary>
public class RssDiscovery(HttpClient http) : IUrlDiscovery
{
    /// <summary>
    /// Attempts RSS discovery using known feed paths and returns feed item links.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
        var candidates = new[]
        {
            new Uri(root, "/feed/"),
            new Uri(root, "/rss"),
            new Uri(root, "/rss.xml"),
            new Uri(root, "/feed.xml")
        };

        foreach (var c in candidates)
        {
            try
            {
                // Parse syndication feed and extract first link per item.
                using var stream = await http.GetStreamAsync(c, ct);
                using var reader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(reader);
                if (feed?.Items is null) continue;

                var urls = feed.Items
                    .Select(i => i.Links.FirstOrDefault()?.Uri)
                    .Where(u => u is not null)
                    .Select(u => UrlUtils.Canonicalize(u!))
                    .Where(UrlUtils.IsHttp)
                    .Where(u => !options.SameHostOnly || u.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(u => u.AbsoluteUri)
                    .Select(u => new DiscoveredUrl(u, "rss", 0))
                    .Take(options.MaxPages)
                    .ToList();

                if (urls.Count > 0) return urls;
            }
            // Feed endpoint may not exist or may be malformed; continue to next candidate.
            catch { }
        }

        return Array.Empty<DiscoveredUrl>();
    }
}