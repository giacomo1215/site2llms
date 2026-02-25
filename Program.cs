using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Pipeline;
using site2llms.Core.Services.Discovery;
using site2llms.Core.Services.Extraction;
using site2llms.Core.Services.Fetching;
using site2llms.Core.Services.Output;
using site2llms.Core.Services.Summarization;
using site2llms.Core.Services.WordPress;
using site2llms.Core.Utils;
using System.Net;

// ── Bootstrap ──────────────────────────────────────────────────────────────────
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
Console.WriteLine("                                                                                             ");
Console.WriteLine("                                               .-''-.    .---..---.                          ");
Console.WriteLine("           .--.               __.....__      .' .-.  )   |   ||   | __  __   ___             ");
Console.WriteLine("           |__|           .-''         '.   / .'  / /    |   ||   ||  |/  `.'   `.          ");
Console.WriteLine("           .--.     .|   /     .-''\"'-.  `.(_/   / /     |   ||   ||   .-.  .-.   '         ");
Console.WriteLine("           |  |   .' |_ /     /________\\   \\    / /      |   ||   ||  |  |  |  |  |         ");
Console.WriteLine("       _   |  | .'     ||                  |   / /       |   ||   ||  |  |  |  |  |     _   ");
Console.WriteLine("     .' |  |  |'--.  .-'\\    .-------------'  . '        |   ||   ||  |  |  |  |  |   .' |  ");
Console.WriteLine("    .   | /|  |   |  |   \\    '-.____...---. / /    _.-')|   ||   ||  |  |  |  |  |  .   | /");
Console.WriteLine("  .'.'| |//|__|   |  |    `.             .'.' '  _.'.-'' |   ||   ||__|  |__|  |__|.'.'| |//");
Console.WriteLine(".'.'.-'  /        |  '.'    `''-...... -' /  /.-'_.'     '---''---'              .'.'.-'  / ");
Console.WriteLine(".'   \\_.'         |   /                  /    _.'                                .'   \\_.'  ");
Console.WriteLine("                  `'-'                  ( _.-'                                               ");
Console.WriteLine("site2llms - Universal website summarizer");
Console.WriteLine("Made by: Giacomo Giorgi");
Console.WriteLine("GitHub: https://github.com/giacomo1215/site2llms");
Console.WriteLine("                                                                                             ");


// ── Collect options ────────────────────────────────────────────────────────────
var options = CollectOptions();

// ── Cookie loading ─────────────────────────────────────────────────────────────
var cookieLogger = loggerFactory.CreateLogger("Cookies");
var cookieContainer = CookieLoader.Load(options.CookieFilePath, cookieLogger);
var cookieEntries = CookieLoader.LoadAsList(options.CookieFilePath, cookieLogger);

// ── HTTP clients ───────────────────────────────────────────────────────────────
using var webHttpClient = BuildHttpClient(cookieContainer);
using var ollamaHttpClient = BuildHttpClient();

// ── WordPress REST client ──────────────────────────────────────────────────────
var wpLogger = loggerFactory.CreateLogger<WordPressRestClient>();
var wordPressRestClient = new WordPressRestClient(webHttpClient, wpLogger);

// ── Site protection probe ──────────────────────────────────────────────────────
var playwrightSession = await ProbeSiteProtectionAsync(
	webHttpClient, options.RootUrl, cookieEntries, wordPressRestClient, loggerFactory);

// ── Compose pipeline services ──────────────────────────────────────────────────
var discovery = new CompositeDiscovery(new IUrlDiscovery[]
{
	new WordPressRestDiscovery(wordPressRestClient, loggerFactory.CreateLogger<WordPressRestDiscovery>()),
	new SitemapDiscovery(webHttpClient),
	new CrawlDiscovery(webHttpClient)
});

var fetcher = new HeadlessFallbackPageFetcher(
	new WordPressRestContentFetcher(new HttpPageFetcher(webHttpClient), wordPressRestClient),
	new HeadlessPageFetcher(cookieEntries, playwrightSession),
	loggerFactory.CreateLogger<HeadlessFallbackPageFetcher>());

var extractor = new HeuristicContentExtractor();
var summarizer = new OllamaSummarizer(ollamaHttpClient, options.OllamaBaseUrl, options.OllamaModel);
var outputWriter = new FileOutputWriter(new LlmsTxtBuilder());
var manifestStore = new ManifestStore();

var pipeline = new SummarizationPipeline(
	discovery, fetcher, extractor, summarizer, outputWriter, manifestStore,
	loggerFactory.CreateLogger<SummarizationPipeline>());

// ── Run ────────────────────────────────────────────────────────────────────────
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
	if (playwrightSession is not null)
	{
		await playwrightSession.DisposeAsync();
	}
}

// ── Helper methods ─────────────────────────────────────────────────────────────

static CrawlOptions CollectOptions()
{
	var rootUrl = PromptString("Root URL", "https://example.com");
	var maxPages = PromptInt("Max pages", 200);
	var maxDepth = PromptInt("Max depth for crawl fallback", 3);
	var delayMs = PromptInt("Delay ms between requests", 250);
	var ollamaBaseUrl = PromptString("Ollama base URL", "http://localhost:11434");
	var ollamaModel = PromptString("Ollama model", "minimax-m2.5:cloud");
	var cookieFile = PromptString("Cookie file (Netscape/JSON, blank to skip)", "");
	var cookieFilePath = string.IsNullOrWhiteSpace(cookieFile) ? null : cookieFile;

	if (cookieFilePath is not null && !File.Exists(cookieFilePath))
	{
		Console.WriteLine($"Warning: cookie file not found at '{cookieFilePath}' — proceeding without cookies.");
		cookieFilePath = null;
	}

	if (cookieFilePath is not null)
	{
		Console.WriteLine($"Cookies loaded from: {cookieFilePath}");
	}

	return new CrawlOptions(
		RootUrl: rootUrl,
		MaxPages: maxPages,
		MaxDepth: maxDepth,
		SameHostOnly: true,
		DelayMs: delayMs,
		OllamaBaseUrl: ollamaBaseUrl,
		OllamaModel: ollamaModel,
		CookieFilePath: cookieFilePath
	);
}

static async Task<PlaywrightSession?> ProbeSiteProtectionAsync(
	HttpClient webHttpClient,
	string rootUrl,
	IReadOnlyList<CookieEntry> cookieEntries,
	WordPressRestClient wordPressRestClient,
	ILoggerFactory loggerFactory)
{
	var probeLogger = loggerFactory.CreateLogger("SiteProbe");
	PlaywrightSession? session = null;
	probeLogger.LogInformation("Probing site accessibility");

	try
	{
		using var probeResponse = await webHttpClient.GetAsync(rootUrl);
		var probeHtml = await probeResponse.Content.ReadAsStringAsync();
		var challengeLabel = ChallengeDetector.Detect(probeHtml);

		if (challengeLabel is not null)
		{
			probeLogger.LogWarning("Site protection detected: {Challenge} — launching headless browser session", challengeLabel);
			session = new PlaywrightSession(loggerFactory.CreateLogger<PlaywrightSession>());
			await session.WarmupAsync(rootUrl, cookieEntries);

			if (session.ChallengeWasResolved)
			{
				wordPressRestClient.Session = session;
				probeLogger.LogInformation("Headless session active — WP REST and page fetches will use the browser");
			}
			else if (session.ChallengeStillBlocked)
			{
				probeLogger.LogWarning("Headless browser could not solve the challenge. Tip: supply a cookie file from a real browser session");
			}
		}
		else
		{
			probeLogger.LogInformation("No site protection detected — proceeding directly");
		}
	}
	catch (Exception ex)
	{
		probeLogger.LogWarning("Warm-up probe failed ({Message}) — proceeding without warm-up", ex.Message);
	}

	return session;
}

// Creates a browser-like HTTP client profile to improve compatibility with real-world sites.
static HttpClient BuildHttpClient(CookieContainer? cookies = null)
{
	var handler = new SocketsHttpHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
		UseCookies = cookies is not null,
		CookieContainer = cookies ?? new CookieContainer()
	};

	var client = new HttpClient(handler)
	{
		Timeout = TimeSpan.FromSeconds(90)
	};

	client.DefaultRequestHeaders.UserAgent.ParseAdd("site2llms/1.0 (+contact)");
	client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");
	client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
	client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
	return client;
}

// Prompts for a string value, falling back to default on empty input.
static string PromptString(string label, string defaultValue)
{
	Console.Write($"{label} [{defaultValue}]: ");
	var input = Console.ReadLine()?.Trim();
	return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
}

// Prompts for a positive integer; repeats until valid or default is accepted.
static int PromptInt(string label, int defaultValue)
{
	while (true)
	{
		Console.Write($"{label} [{defaultValue}]: ");
		var input = Console.ReadLine()?.Trim();
		if (string.IsNullOrWhiteSpace(input))
		{
			return defaultValue;
		}

		if (int.TryParse(input, out var parsed) && parsed > 0)
		{
			return parsed;
		}

		Console.WriteLine("Please enter a positive number.");
	}
}
