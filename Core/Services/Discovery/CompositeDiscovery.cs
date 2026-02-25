using site2llms.Core.Models;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Tries discovery strategies in order and returns the first non-empty result set.
/// </summary>
public class CompositeDiscovery : IUrlDiscovery
{
    private readonly IReadOnlyList<IUrlDiscovery> _strategies;

    /// <summary>
    /// Creates a composite discovery pipeline where order defines priority.
    /// </summary>
    public CompositeDiscovery(IEnumerable<IUrlDiscovery> strategies) => _strategies = strategies.ToList();

    /// <summary>
    /// Runs strategies sequentially until one produces URLs.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        foreach (var s in _strategies)
        {
            // First successful strategy wins (sitemap > rss > crawl in Program.cs).
            var urls = await s.DiscoverAsync(options, ct);
            if (urls.Count > 0)
            {
                return urls
                    // Deduplicate canonical string form before returning to pipeline.
                    .DistinctBy(x => x.Url.AbsoluteUri)
                    .Take(options.MaxPages)
                    .ToList();
            }
        }

        // No strategy found anything usable.
        return Array.Empty<DiscoveredUrl>();
    }
}