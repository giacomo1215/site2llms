namespace site2llms.Core.Models;

/// <summary>
/// Final counters and output location returned by the pipeline.
/// </summary>
/// <param name="PagesDiscovered">Total number of URLs discovered before processing limit is applied.</param>
/// <param name="PagesProcessed">Pages that completed summarization and were written to disk.</param>
/// <param name="PagesSkipped">Pages skipped due to extraction heuristics or cache hits.</param>
/// <param name="PagesFailed">Pages that failed due to fetch/extract/summarize/output exceptions.</param>
/// <param name="PagesCached">Subset of skipped pages skipped specifically by content-hash cache hit.</param>
/// <param name="OutputRoot">Root output folder for this host run.</param>
public record RunResult(
	int PagesDiscovered,
	int PagesProcessed,
	int PagesSkipped,
	int PagesFailed,
	int PagesCached,
	string OutputRoot
);