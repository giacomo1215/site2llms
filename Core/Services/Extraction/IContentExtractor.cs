using site2llms.Core.Models;

namespace site2llms.Core.Services.Extraction;

/// <summary>
/// Contract for transforming raw HTML payloads into normalized markdown content.
/// </summary>
public interface IContentExtractor
{
    /// <summary>
    /// Extracts readable content from a fetched page.
    /// </summary>
    /// <param name="fetchedPage">Fetched page shell containing raw HTML and metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated page content with extraction result and skip metadata.</returns>
    Task<PageContent> ExtractAsync(PageContent fetchedPage, CancellationToken ct = default);
}
