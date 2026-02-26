using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Services.Discovery;
using site2llms.Core.Services.Extraction;
using site2llms.Core.Services.Fetching;
using site2llms.Core.Services.Output;
using site2llms.Core.Services.Summarization;

namespace site2llms.Core.Pipeline;

/// <summary>
/// End-to-end orchestration pipeline:
/// discover -> fetch -> extract -> summarize -> persist -> index.
/// </summary>
public class SummarizationPipeline(
    IUrlDiscovery discovery,
    IPageFetcher fetcher,
    IContentExtractor extractor,
    ISummarizer summarizer,
    IOutputWriter outputWriter,
    IManifestStore manifestStore,
    ILogger<SummarizationPipeline> logger)
{
    /// <summary>
    /// Executes one summarization run for the configured root URL.
    /// </summary>
    public async Task<RunResult> RunAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var rootUrl = new Uri(options.RootUrl);

        // Step 1: discover candidate pages.
        var discovered = await discovery.DiscoverAsync(options, ct);
        logger.LogInformation("Discovered {Count} pages", discovered.Count);

        // Dry-run: report discovered URLs and exit early.
        if (options.DryRun)
        {
            logger.LogInformation("Dry-run mode — listing discovered URLs (capped to {Max})", options.MaxPages);
            var index = 0;
            foreach (var item in discovered.Take(options.MaxPages))
            {
                index++;
                logger.LogInformation("  [{Index}] {Url}", index, item.Url);
            }

            var dryOutputRoot = Path.Combine("output", site2llms.Core.Utils.UrlUtils.SafeHost(rootUrl));
            return new RunResult(discovered.Count, 0, 0, 0, 0, dryOutputRoot);
        }

        // Step 2: load manifest for cache checks.
        var manifest = await manifestStore.LoadAsync(rootUrl, ct);

        // Collected summaries used later to build llms.txt index.
        var indexedPages = new List<SummaryResult>();

        // Run counters for telemetry and final console report.
        var processed = 0;
        var skipped = 0;
        var failed = 0;
        var cached = 0;

        foreach (var item in discovered.Take(options.MaxPages))
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("Processing: {Url}", item.Url);

            try
            {
                // Step 3: fetch raw page.
                var fetched = await fetcher.FetchAsync(item.Url, ct);
                if (fetched is null)
                {
                    failed++;
                    logger.LogWarning("Fetch returned no HTML content for {Url}", item.Url);
                    await Delay(options, ct);
                    continue;
                }

                // Step 4: extract readable markdown.
                var extracted = await extractor.ExtractAsync(fetched, ct);
                if (extracted.IsSkipped)
                {
                    skipped++;
                    logger.LogInformation("Skipped {Url}: {Reason}", item.Url, extracted.SkipReason);
                    await Delay(options, ct);
                    continue;
                }

                // Step 5: cache check based on extracted-content hash.
                var contentHash = site2llms.Core.Utils.HashUtils.Sha256(extracted.ExtractedMarkdown);
                if (manifest.Entries.TryGetValue(extracted.Url.AbsoluteUri, out var existing)
                    && string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(existing.RelativeOutputPath))
                {
                    cached++;
                    skipped++;
                    logger.LogInformation("Skipped {Url}: unchanged content (cache hit)", item.Url);

                    var fileName = Path.GetFileName(existing.RelativeOutputPath);
                    indexedPages.Add(new SummaryResult(
                        Url: extracted.Url,
                        Title: string.IsNullOrWhiteSpace(existing.Title) ? extracted.Title : existing.Title,
                        Markdown: string.Empty,
                        ContentHash: existing.ContentHash,
                        FileName: fileName,
                        RelativeOutputPath: existing.RelativeOutputPath.Replace("\\", "/")
                    ));

                    await Delay(options, ct);
                    continue;
                }

                // Step 6: generate summary and write output file.
                var summary = await summarizer.SummarizeAsync(extracted, ct);
                var outputPath = await outputWriter.WriteSummaryAsync(rootUrl, summary, extracted.FetchedAt, ct);

                // Step 7: refresh manifest entry with latest metadata.
                manifest.Entries[extracted.Url.AbsoluteUri] = new ManifestEntry
                {
                    Url = extracted.Url.AbsoluteUri,
                    ContentHash = summary.ContentHash,
                    RelativeOutputPath = summary.RelativeOutputPath,
                    LastGeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Title = summary.Title
                };

                indexedPages.Add(summary);
                processed++;
                logger.LogInformation("Saved: {OutputPath}", outputPath);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Failed processing {Url}", item.Url);
            }

            await Delay(options, ct);
        }

        // Persist updated manifest after all page attempts.
        await manifestStore.SaveAsync(rootUrl, manifest, ct);

        // Deduplicate by filename in case multiple URLs map to same slug.
        var uniqueIndex = indexedPages
            .GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Build/update host-level llms.txt index.
        await outputWriter.WriteLlmsTxtAsync(rootUrl, uniqueIndex, ct);

        var outputRoot = Path.Combine("output", site2llms.Core.Utils.UrlUtils.SafeHost(rootUrl));
        return new RunResult(discovered.Count, processed, skipped, failed, cached, outputRoot);
    }

    /// <summary>
    /// Applies inter-request delay configured by crawl options.
    /// </summary>
    private static Task Delay(CrawlOptions options, CancellationToken ct)
    {
        return options.DelayMs > 0 ? Task.Delay(options.DelayMs, ct) : Task.CompletedTask;
    }
}
