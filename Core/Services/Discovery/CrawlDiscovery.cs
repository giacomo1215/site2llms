using AngleSharp.Html.Parser;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Breadth-first in-site crawler used as fallback discovery strategy.
/// </summary>
public class CrawlDiscovery(HttpClient http) : IUrlDiscovery
{
    /// <summary>
    /// Crawls links starting from the root URL, bounded by max depth and max pages.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
        var host = root.Host;
        var canonicalRoot = UrlUtils.Canonicalize(root);

        // Track visited URLs to avoid loops and duplicates.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // BFS queue keeps depth info for max-depth enforcement.
        var q = new Queue<DiscoveredUrl>();
        q.Enqueue(new DiscoveredUrl(canonicalRoot, "crawl", 0));
        seen.Add(canonicalRoot.AbsoluteUri);

        var parser = new HtmlParser();
        var outList = new List<DiscoveredUrl>();

        while (q.Count > 0 && outList.Count < options.MaxPages)
        {
            ct.ThrowIfCancellationRequested();
            var cur = q.Dequeue();
            outList.Add(cur);

            // Do not expand children once depth cap is reached.
            if (cur.Depth >= options.MaxDepth) continue;

            try
            {
                var html = await http.GetStringAsync(cur.Url, ct);
                var doc = await parser.ParseDocumentAsync(html, ct);

                // Extract, normalize, and filter links from this page.
                var links = doc.QuerySelectorAll("a[href]")
                    .Select(a => a.GetAttribute("href"))
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => UrlUtils.ToAbsolute(root, cur.Url, h!))
                    .Where(u => u is not null)
                    .Select(u => UrlUtils.Canonicalize(u!))
                    .Where(u => UrlUtils.IsHttp(u))
                    .Where(u => !options.SameHostOnly || u.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                    .DistinctBy(u => u.AbsoluteUri);

                foreach (var u in links)
                {
                    // Keep overall queue bounded by page budget.
                    if (outList.Count + q.Count >= options.MaxPages)
                    {
                        break;
                    }

                    if (seen.Add(u.AbsoluteUri))
                        q.Enqueue(new DiscoveredUrl(u, "crawl", cur.Depth + 1));
                }
            }
            // Any crawl page can fail independently without aborting full discovery.
            catch { }

            if (options.DelayMs > 0) await Task.Delay(options.DelayMs, ct);
        }

        return outList;
    }
}