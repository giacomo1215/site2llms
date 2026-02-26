namespace site2llms.Core.Models;

/// <summary>
/// Runtime configuration for a complete summarization run.
/// </summary>
/// <param name="RootUrl">Entry-point URL for discovery (for example, the website home page).</param>
/// <param name="MaxPages">Upper bound on how many pages can be processed in one run.</param>
/// <param name="MaxDepth">Maximum BFS crawl depth used by crawl-based discovery fallback.</param>
/// <param name="SameHostOnly">When true, discovered URLs are restricted to the same host.</param>
/// <param name="DelayMs">Politeness delay between requests and pipeline iterations, in milliseconds.</param>
/// <param name="OllamaBaseUrl">Base URL of the Ollama API endpoint.</param>
/// <param name="OllamaModel">Model identifier to request from Ollama.</param>
/// <param name="CookieFilePath">Optional path to a Netscape/JSON cookie file for authenticated fetching.</param>
/// <param name="DryRun">When true, the pipeline discovers URLs but skips fetching, summarisation and output.</param>
public record CrawlOptions(
    string RootUrl,
    int MaxPages,
    int MaxDepth,
    bool SameHostOnly,
    int DelayMs,
    string OllamaBaseUrl,
    string OllamaModel,
    string? CookieFilePath = null,
    bool DryRun = false
);