using System.Text;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Output;

/// <summary>
/// File-system output writer for page markdown documents and llms.txt index.
/// </summary>
public class FileOutputWriter : IOutputWriter
{
    private readonly LlmsTxtBuilder _llmsTxtBuilder;

    /// <summary>
    /// Initializes writer with an <see cref="LlmsTxtBuilder"/> for index generation.
    /// </summary>
    public FileOutputWriter(LlmsTxtBuilder llmsTxtBuilder)
    {
        _llmsTxtBuilder = llmsTxtBuilder;
    }

    /// <summary>
    /// Writes one summary markdown file under <c>output/&lt;host&gt;/ai/pages</c>.
    /// </summary>
    public async Task<string> WriteSummaryAsync(Uri rootUrl, SummaryResult summaryResult, DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        var hostFolder = UrlUtils.SafeHost(rootUrl);
        var outputDir = Path.Combine("output", hostFolder, "ai", "pages");
        Directory.CreateDirectory(outputDir);

        var outputPath = Path.Combine(outputDir, summaryResult.FileName);

        // Persist frontmatter + markdown body to make pages self-describing.
        var content = BuildFrontmatter(summaryResult, fetchedAt) + "\n" + summaryResult.Markdown.Trim() + "\n";
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, ct);

        return outputPath;
    }

    /// <summary>
    /// Writes the host-level <c>llms.txt</c> index file.
    /// </summary>
    public async Task WriteLlmsTxtAsync(Uri rootUrl, IReadOnlyList<SummaryResult> pages, CancellationToken ct = default)
    {
        var hostFolder = UrlUtils.SafeHost(rootUrl);
        var hostDir = Path.Combine("output", hostFolder);
        Directory.CreateDirectory(hostDir);

        var llmsTxtPath = Path.Combine(hostDir, "llms.txt");
        var content = _llmsTxtBuilder.Build(rootUrl, pages);
        await File.WriteAllTextAsync(llmsTxtPath, content, Encoding.UTF8, ct);
    }

    /// <summary>
    /// Creates YAML frontmatter with provenance and caching metadata.
    /// </summary>
    private static string BuildFrontmatter(SummaryResult summaryResult, DateTimeOffset fetchedAt)
    {
        return $"""
---
title: "{TextUtils.EscapeYaml(summaryResult.Title)}"
source_url: "{TextUtils.EscapeYaml(summaryResult.Url.ToString())}"
fetched_at: "{fetchedAt.UtcDateTime:O}"
content_hash: "{summaryResult.ContentHash}"
generator: "site2llms + Ollama"
---
""";
    }
}
