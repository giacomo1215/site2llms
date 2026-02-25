namespace site2llms.Core.Models;

/// <summary>
/// Final AI summary artifact for one source page.
/// </summary>
/// <param name="Url">Source page URL.</param>
/// <param name="Title">Human-readable page title for display/indexing.</param>
/// <param name="Markdown">Generated markdown summary content.</param>
/// <param name="ContentHash">Hash of extracted input content used for caching.</param>
/// <param name="FileName">Output markdown file name.</param>
/// <param name="RelativeOutputPath">Relative storage path tracked in the manifest.</param>
public record SummaryResult(
	Uri Url,
	string Title,
	string Markdown,
	string ContentHash,
	string FileName,
	string RelativeOutputPath
);