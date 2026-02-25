using site2llms.Core.Models;
using site2llms.Core.Services.WordPress;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Fetcher wrapper that prefers WordPress REST rendered content for WP-discovered URLs.
/// </summary>
public class WordPressRestContentFetcher(IPageFetcher fallbackFetcher, WordPressRestClient wpRestClient) : IPageFetcher
{
    /// <summary>
    /// Returns REST-rendered HTML when present in WP cache; otherwise uses the fallback fetcher.
    /// </summary>
    public async Task<PageContent?> FetchAsync(Uri url, CancellationToken ct = default)
    {
        if (wpRestClient.TryGetCached(url, out var wpItem))
        {
            return new PageContent(
                Url: wpItem.Url,
                Title: wpItem.Title,
                ExtractedMarkdown: string.Empty,
                RawHtml: WrapAsArticle(wpItem.RenderedHtml),
                FetchedAt: wpItem.ModifiedAtUtc,
                IsSkipped: false,
                SkipReason: null
            );
        }

        return await fallbackFetcher.FetchAsync(url, ct);
    }

    private static string WrapAsArticle(string html)
    {
        return $"<article>{html}</article>";
    }
}