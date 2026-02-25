using AngleSharp.Html.Parser;
using site2llms.Core.Models;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// HTTP-based page fetcher that performs basic content-type and body heuristics.
/// </summary>
public class HttpPageFetcher(HttpClient httpClient) : IPageFetcher
{
    private readonly HtmlParser _parser = new();

    /// <summary>
    /// Downloads page content and returns a fetch shell for downstream extraction.
    /// </summary>
    public async Task<PageContent?> FetchAsync(Uri url, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync(url, ct);
        var html = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var isHtmlByHeader = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase);
        var isHtmlByBody = LooksLikeHtml(html);

        // Allow non-success status only when payload still looks like HTML
        // (some sites return content behind non-2xx responses).
        if (!response.IsSuccessStatusCode && !isHtmlByBody)
        {
            return null;
        }

        // Bail out early for binary/non-HTML responses.
        if (!isHtmlByHeader && !isHtmlByBody)
        {
            return null;
        }

        var document = await _parser.ParseDocumentAsync(html, ct);
        var title = document.Title?.Trim();

        return new PageContent(
            Url: url,
            Title: string.IsNullOrWhiteSpace(title) ? url.AbsolutePath.Trim('/').Replace('-', ' ') : title,
            ExtractedMarkdown: string.Empty,
            RawHtml: html,
            FetchedAt: DateTimeOffset.UtcNow,
            IsSkipped: false,
            SkipReason: null
        );
    }

    /// <summary>
    /// Lightweight body sniffing for HTML when headers are missing or incorrect.
    /// </summary>
    private static bool LooksLikeHtml(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var sampleLength = Math.Min(content.Length, 4096);
        var sample = content[..sampleLength];

        return sample.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("<head", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }
}
