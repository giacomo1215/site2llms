namespace site2llms.Core.Models;

/// <summary>
/// Represents a page as it moves through fetch/extract steps.
/// The same record is progressively enriched using immutable "with" updates.
/// </summary>
/// <param name="Url">Absolute URL of the source page.</param>
/// <param name="Title">Resolved page title (from HTML title or fallback).</param>
/// <param name="ExtractedMarkdown">Main textual content converted to markdown.</param>
/// <param name="RawHtml">Original fetched HTML payload.</param>
/// <param name="FetchedAt">UTC timestamp when the page was fetched.</param>
/// <param name="IsSkipped">True when downstream processing should skip this page.</param>
/// <param name="SkipReason">Optional human-readable reason for skip decisions.</param>
public record PageContent(
    Uri Url,
    string Title,
    string ExtractedMarkdown,
    string RawHtml,
    DateTimeOffset FetchedAt,
    bool IsSkipped,
    string? SkipReason
);