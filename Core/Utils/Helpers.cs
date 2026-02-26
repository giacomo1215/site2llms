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

namespace site2llms.Core.Utils;

public static class Helpers
{
    /// <summary>
    /// Prints a stylized greeting message to the console on application start.
    /// </summary>
    public static void Greet()
    {
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
    }

    /// <summary>
    /// Resolves <see cref="CrawlOptions"/> from CLI arguments when supplied, or falls back to
    /// interactive prompts. Returns <c>null</c> when the process should exit (e.g. after --help).
    /// </summary>
    public static CrawlOptions? ResolveOptions(string[] args)
    {
        var result = CliParser.TryParse(args, out var cliOptions);

        return result switch
        {
            CliParseResult.Parsed => cliOptions,
            CliParseResult.Interactive => CollectOptions(),
            _ => null // Exit (help shown)
        };
    }

    /// <summary>
    /// Prompts the user for configuration options with defaults, and returns a CrawlOptions instance.
    /// </summary>
    /// <returns>A <see cref="CrawlOptions"/> instance configured with the values entered by the user.</returns>
    public static CrawlOptions CollectOptions()
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

    /// <summary>
    /// Probes the target site for common anti-bot protections by analyzing the HTML of the root page.
    /// If a challenge is detected, it attempts to resolve it using a headless Playwright session, which can then be used for subsequent requests to improve access and compatibility.
    /// </summary>
    /// <param name="webHttpClient"></param>
    /// <param name="rootUrl"></param>
    /// <param name="cookieEntries"></param>
    /// <param name="wordPressRestClient"></param>
    /// <param name="loggerFactory"></param>
    /// <returns>A <see cref="PlaywrightSession"/> instance if a challenge was resolved, otherwise null.</returns>
    public static async Task<PlaywrightSession?> ProbeSiteProtectionAsync(
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

    /// <summary>
    /// Builds a shared HttpClient instance with reasonable defaults for crawling, including user-agent, accept headers, and optional cookie support.
    /// </summary>
    /// <param name="cookies">Optional cookie container used by the underlying HTTP handler; if null, a new container is created.</param>
    /// <returns>A configured <see cref="HttpClient"/> instance with sensible defaults for crawling.</returns>
    public static HttpClient BuildHttpClient(CookieContainer? cookies = null)
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

    #region User Input Prompts
    /// <summary>
    /// Prompts the user for a string input with a default value. If the user enters nothing, the default is returned.
    /// </summary>
    /// <param name="label"></param>
    /// <param name="defaultValue"></param>
    public static string PromptString(string label, string defaultValue)
    {
        Console.Write($"{label} [{defaultValue}]: ");
        var input = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
    }

    /// <summary>
    /// Prompts the user for an integer input with a default value. If the user enters nothing, the default is returned. The method will continue to prompt until a valid positive integer is entered or the user accepts the default.
    /// </summary>
    /// <param name="label"></param>
    /// <param name="defaultValue"></param>
    public static int PromptInt(string label, int defaultValue)
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
    #endregion
}