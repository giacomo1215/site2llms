using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Services.WordPress;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Discovery strategy that attempts WordPress REST listing before any non-WP fallback strategy.
/// </summary>
public class WordPressRestDiscovery(
    WordPressRestClient client,
    ILogger<WordPressRestDiscovery> logger) : IUrlDiscovery
{
    /// <summary>
    /// Detects REST availability and discovers page/post URLs via WP v2 endpoints when enabled.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
        var detection = await client.DetectAsync(root, ct);
        logger.LogInformation("WP REST detected: {Available}", detection.IsWordPressRestAvailable ? "yes" : "no");

        if (!detection.IsWordPressRestAvailable)
        {
            return Array.Empty<DiscoveredUrl>();
        }

        var result = await client.DiscoverAsync(options, ct);
        if (result.Disabled)
        {
            logger.LogWarning("WP REST disabled: {Reason}", result.DisabledReason ?? "unknown reason");
            return Array.Empty<DiscoveredUrl>();
        }

        logger.LogInformation("WP REST discovered {Pages} pages, {Posts} posts", result.PagesCount, result.PostsCount);
        return result.Items;
    }
}