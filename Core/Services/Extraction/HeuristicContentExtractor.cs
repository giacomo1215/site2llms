using AngleSharp.Html.Parser;
using site2llms.Core.Models;
using site2llms.Core.Utils;
using ReverseMarkdown;

namespace site2llms.Core.Services.Extraction;

/// <summary>
/// Extracts the most likely main content region from HTML and converts it to markdown.
/// </summary>
public class HeuristicContentExtractor : IContentExtractor
{
    private readonly HtmlParser _parser = new();

    // ReverseMarkdown converter configured for permissive HTML handling and GitHub-flavored output.
    private readonly Converter _converter = new(new Config
    {
        UnknownTags = Config.UnknownTagsOption.Bypass,
        GithubFlavored = true,
        RemoveComments = true
    });

    /// <summary>
    /// Applies heuristic content extraction and marks pages as skipped when readability is too low.
    /// </summary>
    public async Task<PageContent> ExtractAsync(PageContent fetchedPage, CancellationToken ct = default)
    {
        var doc = await _parser.ParseDocumentAsync(fetchedPage.RawHtml, ct);

        // Prioritize semantic content containers before falling back to body.
        var candidate = doc.QuerySelector("main")
            ?? doc.QuerySelector("article")
            ?? doc.QuerySelector("[role='main']")
            ?? doc.QuerySelector(".content, .entry-content, .post-content")
            ?? doc.Body;

        if (candidate is null)
        {
            return fetchedPage with
            {
                IsSkipped = true,
                SkipReason = "No readable content block found"
            };
        }

        // Remove common boilerplate/non-content tags from the candidate subtree.
        foreach (var node in candidate.QuerySelectorAll("script,style,noscript,nav,footer,header,aside").ToList())
        {
            node.Remove();
        }

        // Primary conversion path: HTML fragment -> markdown.
        var markdown = _converter.Convert(candidate.InnerHtml ?? string.Empty);
        markdown = TextUtils.CleanMarkdown(markdown);

        // Fallback: plain text extraction when converter yields empty output.
        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = TextUtils.CleanMarkdown(candidate.TextContent ?? string.Empty);
        }

        // Very short content is usually navigation/noise: skip downstream summarization.
        if (markdown.Length < 50)
        {
            return fetchedPage with
            {
                ExtractedMarkdown = markdown,
                IsSkipped = true,
                SkipReason = "Extracted markdown too short (<50 chars)"
            };
        }

        return fetchedPage with
        {
            ExtractedMarkdown = markdown,
            IsSkipped = false,
            SkipReason = null
        };
    }
}
