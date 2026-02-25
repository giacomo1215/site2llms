using site2llms.Core.Models;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Contract for URL discovery strategies (sitemap, RSS, crawl, etc.).
/// </summary>
public interface IUrlDiscovery
{
    /// <summary>
    /// Discovers candidate page URLs for a run.
    /// </summary>
    /// <param name="options">Run options that constrain strategy behavior.</param>
    /// <param name="ct">Cancellation token propagated from the pipeline.</param>
    /// <returns>List of discovered URLs (possibly empty).</returns>
    Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default);
}