using System.Threading.Channels;
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
    private enum FetchOutcome { Success, FetchFailed, ExtractionSkipped }

    private record FetchResult(
        DiscoveredUrl Source,
        PageContent? Extracted,
        FetchOutcome Outcome);

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

            var dryOutputRoot = Path.Combine("output", Utils.UrlUtils.SafeHost(rootUrl));
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

        var urlsToProcess = discovered.Take(options.MaxPages).ToList();

        // Channel-based producer-consumer pipeline:
        // Producer: parallel fetch + extract (I/O-bound)
        // Consumer: sequential cache check + summarize + write (LLM/GPU-bound)
        var channel = Channel.CreateBounded<FetchResult>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

        var producerTask = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    urlsToProcess,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = options.MaxConcurrency,
                        CancellationToken = ct
                    },
                    async (item, token) =>
                    {
                        logger.LogInformation("Fetching: {Url}", item.Url);
                        try
                        {
                            var fetched = await fetcher.FetchAsync(item.Url, token);
                            if (fetched is null)
                            {
                                logger.LogWarning("Fetch returned no HTML content for {Url}", item.Url);
                                await channel.Writer.WriteAsync(
                                    new FetchResult(item, null, FetchOutcome.FetchFailed), token);
                                return;
                            }

                            var extracted = await extractor.ExtractAsync(fetched, token);
                            if (extracted.IsSkipped)
                            {
                                logger.LogInformation("Skipped {Url}: {Reason}", item.Url, extracted.SkipReason);
                                await channel.Writer.WriteAsync(
                                    new FetchResult(item, extracted, FetchOutcome.ExtractionSkipped), token);
                                return;
                            }

                            await channel.Writer.WriteAsync(
                                new FetchResult(item, extracted, FetchOutcome.Success), token);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed fetching/extracting {Url}", item.Url);
                            await channel.Writer.WriteAsync(
                                new FetchResult(item, null, FetchOutcome.FetchFailed), token);
                        }

                        await Delay(options, token);
                    });
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        // Consumer: sequential processing (cache check → summarize → write).
        var consumed = 0;
        await foreach (var result in channel.Reader.ReadAllAsync(ct))
        {
            switch (result.Outcome)
            {
                case FetchOutcome.FetchFailed:
                    failed++;
                    break;

                case FetchOutcome.ExtractionSkipped:
                    skipped++;
                    break;

                case FetchOutcome.Success:
                {
                    var extracted = result.Extracted!;
                    var contentHash = Utils.HashUtils.Sha256(extracted.ExtractedMarkdown);

                    if (manifest.Entries.TryGetValue(extracted.Url.AbsoluteUri, out var existing)
                        && string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(existing.RelativeOutputPath))
                    {
                        cached++;
                        skipped++;
                        logger.LogInformation("Skipped {Url}: unchanged content (cache hit)", extracted.Url);

                        // Load persisted markdown to preserve full content in llms-full.txt.
                        var cachedMarkdown = await LoadCachedMarkdownAsync(rootUrl, existing.RelativeOutputPath, ct);

                        var fileName = Path.GetFileName(existing.RelativeOutputPath);
                        indexedPages.Add(new SummaryResult(
                            Url: extracted.Url,
                            Title: string.IsNullOrWhiteSpace(existing.Title) ? extracted.Title : existing.Title,
                            Markdown: cachedMarkdown,
                            ContentHash: existing.ContentHash,
                            FileName: fileName,
                            RelativeOutputPath: existing.RelativeOutputPath.Replace("\\", "/")
                        ));
                        break;
                    }

                    try
                    {
                        var summary = await summarizer.SummarizeAsync(extracted, ct);
                        var outputPath = await outputWriter.WriteSummaryAsync(rootUrl, summary, extracted.FetchedAt, ct);

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
                        logger.LogError(ex, "Failed processing {Url}", extracted.Url);
                    }
                    break;
                }
            }

            // Periodic manifest checkpoint to avoid losing all progress on crash.
            consumed++;
            if (consumed % 10 == 0)
            {
                await manifestStore.SaveAsync(rootUrl, manifest, ct);
                logger.LogDebug("Manifest checkpoint saved ({Count} items consumed)", consumed);
            }
        }

        // Ensure producer completes and propagate any unobserved exceptions.
        await producerTask;

        // Persist updated manifest after all page attempts.
        await manifestStore.SaveAsync(rootUrl, manifest, ct);

        // Deduplicate by filename in case multiple URLs map to same slug.
        var uniqueIndex = indexedPages
            .GroupBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Build/update host-level llms.txt index.
        await outputWriter.WriteLlmsTxtAsync(rootUrl, uniqueIndex, ct);

        if (options.GenerateLlmsFullTxt)
        {
            await outputWriter.WriteLlmsFullTxtAsync(rootUrl, uniqueIndex, ct);
        }

        var outputRoot = Path.Combine("output", site2llms.Core.Utils.UrlUtils.SafeHost(rootUrl));
        return new RunResult(discovered.Count, processed, skipped, failed, cached, outputRoot);
    }

    /// <summary>
    /// Reads a cached markdown file and strips YAML frontmatter to return only the body content.
    /// </summary>
    private static async Task<string> LoadCachedMarkdownAsync(Uri rootUrl, string relativeOutputPath, CancellationToken ct)
    {
        var hostFolder = Utils.UrlUtils.SafeHost(rootUrl);
        var fullPath = Path.Combine("output", hostFolder, relativeOutputPath);

        if (!File.Exists(fullPath))
            return string.Empty;

        var content = await File.ReadAllTextAsync(fullPath, ct);
        return StripFrontmatter(content);
    }

    /// <summary>
    /// Removes YAML frontmatter (delimited by --- lines) from a markdown document.
    /// </summary>
    private static string StripFrontmatter(string content)
    {
        if (!content.TrimStart().StartsWith("---"))
            return content.Trim();

        var lines = content.Split('\n');
        var foundOpening = false;
        var bodyStartLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r').Trim() != "---") continue;

            if (!foundOpening)
            {
                foundOpening = true;
            }
            else
            {
                bodyStartLine = i + 1;
                break;
            }
        }

        return bodyStartLine == 0
            ? content.Trim()
            : string.Join('\n', lines.Skip(bodyStartLine)).Trim();
    }

    /// <summary>
    /// Applies inter-request delay configured by crawl options.
    /// </summary>
    private static Task Delay(CrawlOptions options, CancellationToken ct)
    {
        return options.DelayMs > 0 ? Task.Delay(options.DelayMs, ct) : Task.CompletedTask;
    }
}
