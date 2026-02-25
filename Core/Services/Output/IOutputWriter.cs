using site2llms.Core.Models;

namespace site2llms.Core.Services.Output;

/// <summary>
/// Contract for persisting generated outputs (page markdown and llms.txt index).
/// </summary>
public interface IOutputWriter
{
    /// <summary>
    /// Writes a single summary markdown document and returns its path.
    /// </summary>
    /// <param name="rootUrl">Root site URL used for host folder resolution.</param>
    /// <param name="summaryResult">Summary to persist.</param>
    /// <param name="fetchedAt">Original fetch timestamp used in frontmatter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute/relative file path used by the writer implementation.</returns>
    Task<string> WriteSummaryAsync(Uri rootUrl, SummaryResult summaryResult, DateTimeOffset fetchedAt, CancellationToken ct = default);

    /// <summary>
    /// Writes or updates the llms.txt index file for all generated pages.
    /// </summary>
    /// <param name="rootUrl">Root site URL used for host folder resolution.</param>
    /// <param name="pages">Page summaries to index.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteLlmsTxtAsync(Uri rootUrl, IReadOnlyList<SummaryResult> pages, CancellationToken ct = default);
}
