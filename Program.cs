using Microsoft.Extensions.Logging;
using site2llms.Core.Pipeline;
using site2llms.Core.Services.Discovery;
using site2llms.Core.Services.Extraction;
using site2llms.Core.Services.Fetching;
using site2llms.Core.Services.Output;
using site2llms.Core.Services.Summarization;
using site2llms.Core.Services.WordPress;
using site2llms.Core.Utils;

// Bootstrap and composition root for the site2llms application.
using var loggerFactory = LoggerFactory.Create(builder =>
{
	builder.AddSimpleConsole(options =>
	{
		options.SingleLine = true;
		options.TimestampFormat = "HH:mm:ss ";
	});
	builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger("site2llms");
Helpers.Greet();

// Resolve options: CLI arguments first, interactive prompts as fallback.
var options = Helpers.ResolveOptions(args);
if (options is null)
{
    // --help was shown; nothing more to do.
    return;
}

// Cookie loading
var cookieLogger = loggerFactory.CreateLogger("Cookies");
var cookieContainer = CookieLoader.Load(options.CookieFilePath, cookieLogger);
var cookieEntries = CookieLoader.LoadAsList(options.CookieFilePath, cookieLogger);

// HTTP clients
using var webHttpClient = Helpers.BuildHttpClient(cookieContainer);
using var ollamaHttpClient = Helpers.BuildHttpClient();

// WordPress REST client
var wpLogger = loggerFactory.CreateLogger<WordPressRestClient>();
var wordPressRestClient = new WordPressRestClient(webHttpClient, wpLogger);

// Site protection probe
var playwrightSession = await Helpers.ProbeSiteProtectionAsync(
	webHttpClient, options.RootUrl, cookieEntries, wordPressRestClient, loggerFactory);

// Compose pipeline services
var discovery = new CompositeDiscovery(new IUrlDiscovery[]
{
	new WordPressRestDiscovery(wordPressRestClient, loggerFactory.CreateLogger<WordPressRestDiscovery>()),
	new SitemapDiscovery(webHttpClient),
	new CrawlDiscovery(webHttpClient)
});

// Fetcher with WordPress REST and headless fallback.
var fetcher = new HeadlessFallbackPageFetcher(
	new WordPressRestContentFetcher(new HttpPageFetcher(webHttpClient), wordPressRestClient),
	new HeadlessPageFetcher(cookieEntries, playwrightSession),
	loggerFactory.CreateLogger<HeadlessFallbackPageFetcher>());

// Heuristic content extractor and Ollama summarizer.
var extractor = new HeuristicContentExtractor();
var summarizer = new OllamaSummarizer(ollamaHttpClient, options.OllamaBaseUrl, options.OllamaModel);
var outputWriter = new FileOutputWriter(new LlmsTxtBuilder());
var manifestStore = new ManifestStore();

// Pipeline composition
var pipeline = new SummarizationPipeline(
	discovery, fetcher, extractor, summarizer, outputWriter, manifestStore,
	loggerFactory.CreateLogger<SummarizationPipeline>());

// Run
try
{
	var runResult = await pipeline.RunAsync(options);

	Console.WriteLine();
	Console.WriteLine("Run completed.");
	Console.WriteLine($"Discovered: {runResult.PagesDiscovered}");
	Console.WriteLine($"Processed:  {runResult.PagesProcessed}");
	Console.WriteLine($"Skipped:    {runResult.PagesSkipped} (cache hits: {runResult.PagesCached})");
	Console.WriteLine($"Failed:     {runResult.PagesFailed}");
	Console.WriteLine($"Output:     {Path.GetFullPath(runResult.OutputRoot)}");
}
catch (Exception ex)
{
	Console.Error.WriteLine($"Fatal error: {ex.Message}");
	Environment.ExitCode = 1;
}
finally
{
	if (playwrightSession is not null) await playwrightSession.DisposeAsync();
}