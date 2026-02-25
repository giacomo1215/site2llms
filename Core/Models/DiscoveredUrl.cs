namespace site2llms.Core.Models
{
    /// <summary>
    /// Represents a URL discovered by one of the discovery strategies.
    /// </summary>
    /// <param name="Url">Canonical absolute URL that should be considered for processing.</param>
    /// <param name="DiscoveryMethod">Strategy label (for example: sitemap, rss, crawl).</param>
    /// <param name="Depth">Discovery depth used by crawl mode (0 for non-crawl sources).</param>
    public record DiscoveredUrl(Uri Url, string DiscoveryMethod, int Depth);
}