using site2llms.Core.Models;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Contract for fetching raw page payloads.
/// </summary>
public interface IPageFetcher
{
    /// <summary>
    /// Fetches page content for the supplied URL.
    /// </summary>
    /// <param name="url">Absolute URL to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="PageContent"/> shell or null when page is unusable.</returns>
    Task<PageContent?> FetchAsync(Uri url, CancellationToken ct = default);
}
