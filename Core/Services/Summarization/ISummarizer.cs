using site2llms.Core.Models;

namespace site2llms.Core.Services.Summarization;

/// <summary>
/// Contract for converting extracted page content into AI-friendly summaries.
/// </summary>
public interface ISummarizer
{
    /// <summary>
    /// Generates a summary artifact for one extracted page.
    /// </summary>
    /// <param name="pageContent">Extracted page content to summarize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary result containing markdown and output metadata.</returns>
    Task<SummaryResult> SummarizeAsync(PageContent pageContent, CancellationToken ct = default);
}
