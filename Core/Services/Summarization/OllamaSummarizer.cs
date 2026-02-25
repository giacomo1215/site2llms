using System.Text;
using System.Text.Json;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Summarization;

/// <summary>
/// Summarizer implementation backed by Ollama's <c>/api/generate</c> endpoint.
/// </summary>
public class OllamaSummarizer(HttpClient httpClient, string baseUrl, string model) : ISummarizer
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly string _model = model;

    /// <summary>
    /// Builds prompt, calls Ollama, and maps response to a summary artifact.
    /// </summary>
    public async Task<SummaryResult> SummarizeAsync(PageContent pageContent, CancellationToken ct = default)
    {
        // Compose strict-format prompt to keep output machine-friendly and consistent.
        var prompt = BuildPrompt(pageContent);
        var payload = new
        {
            model = _model,
            prompt,
            stream = false,
            temperature = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        // Ollama returns a JSON envelope with the generated text in "response".
        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);

        var markdown = json.RootElement.GetProperty("response").GetString()?.Trim() ?? string.Empty;

        // Cache identity is based on extracted source content, not generated text.
        var contentHash = HashUtils.Sha256(pageContent.ExtractedMarkdown);

        // Stable file names come from source URL slugs.
        var slug = UrlUtils.SlugFromUrl(pageContent.Url);
        var fileName = $"{slug}.md";

        return new SummaryResult(
            Url: pageContent.Url,
            Title: pageContent.Title,
            Markdown: markdown,
            ContentHash: contentHash,
            FileName: fileName,
            RelativeOutputPath: $"ai/pages/{fileName}"
        );
    }

    /// <summary>
    /// Produces a structured summarization prompt with explicit markdown sections.
    /// </summary>
    private static string BuildPrompt(PageContent pageContent)
    {
        return $"""
You are generating an AI-friendly page summary in markdown.
Return markdown only. Do not add commentary outside markdown sections.
Do not invent details. If unknown, write \"Not specified\".

Required structure:
# {pageContent.Title}

## TL;DR
- 2-4 bullets

## Key points
- 5-10 concrete bullets

## Useful context
- Content type: ...
- Location: ...
- Services/areas: ...
- Deliverables: ...
- Constraints/criteria: ...

## FAQ
- 5 to 8 Q/A pairs
Q: ...
A: ...

## Reference
- Source: {pageContent.Url}

Source title: {pageContent.Title}
Source URL: {pageContent.Url}

Extracted content to summarize:
{pageContent.ExtractedMarkdown}
""";
    }
}
