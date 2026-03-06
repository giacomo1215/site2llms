using site2llms.Core.Models;

namespace site2llms.Core.Utils;

/// <summary>
/// Parses command-line arguments into a <see cref="CrawlOptions"/> instance.
/// When no arguments are provided the caller should fall back to interactive prompts.
/// </summary>
public static class CliParser
{
    private const string DefaultOllamaUrl = "http://localhost:11434";
    private const string DefaultOllamaModel = "minimax-m2.5:cloud";
    private const int DefaultMaxPages = 200;
    private const int DefaultMaxDepth = 3;
    private const int DefaultDelayMs = 250;

    /// <summary>
    /// Attempts to parse the supplied command-line arguments.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="options">The resulting options when parsing succeeds.</param>
    /// <returns>
    /// A <see cref="CliParseResult"/> indicating whether the caller should continue
    /// with the returned options, fall back to interactive mode, or exit (e.g. after --help).
    /// </returns>
    public static CliParseResult TryParse(string[] args, out CrawlOptions? options)
    {
        options = null;

        if (args.Length == 0)
            return CliParseResult.Interactive;

        // --help / -h
        if (args.Any(a => a is "--help" or "-h"))
        {
            Helpers.PrintUsage();
            return CliParseResult.Exit;
        }

        string? rootUrl = null;
        int maxPages = DefaultMaxPages;
        int maxDepth = DefaultMaxDepth;
        int delayMs = DefaultDelayMs;
        bool sameHostOnly = true;
        string ollamaBaseUrl = DefaultOllamaUrl;
        string ollamaModel = DefaultOllamaModel;
        string? cookieFilePath = null;
        bool dryRun = false;
        var includePatterns = new List<string>();
        var excludePatterns = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "--url":
                    rootUrl = NextValue(args, ref i, arg);
                    break;

                case "--max-pages":
                    maxPages = NextInt(args, ref i, arg);
                    break;

                case "--max-depth":
                    maxDepth = NextInt(args, ref i, arg);
                    break;

                case "--delay":
                    delayMs = NextInt(args, ref i, arg);
                    break;

                case "--ollama-url":
                    ollamaBaseUrl = NextValue(args, ref i, arg);
                    break;

                case "--ollama-model":
                    ollamaModel = NextValue(args, ref i, arg);
                    break;

                case "--cookies":
                    cookieFilePath = NextValue(args, ref i, arg);
                    break;

                case "--same-host-only":
                    sameHostOnly = true;
                    break;

                case "--no-same-host":
                    sameHostOnly = false;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--include":
                    includePatterns.Add(NextValue(args, ref i, arg));
                    break;

                case "--exclude":
                    excludePatterns.Add(NextValue(args, ref i, arg));
                    break;

                default:
                    Error($"Unknown argument: {arg}");
                    break;
            }
        }

        // --url is mandatory in CLI mode.
        if (string.IsNullOrWhiteSpace(rootUrl))
        {
            Error("--url is required when using CLI arguments.");
        }

        // Validate cookie file if provided.
        if (cookieFilePath is not null && !File.Exists(cookieFilePath))
        {
            Console.Error.WriteLine($"Warning: cookie file not found at '{cookieFilePath}' — proceeding without cookies.");
            cookieFilePath = null;
        }

        options = new CrawlOptions(
            RootUrl: rootUrl!,
            MaxPages: maxPages,
            MaxDepth: maxDepth,
            SameHostOnly: sameHostOnly,
            DelayMs: delayMs,
            OllamaBaseUrl: ollamaBaseUrl,
            OllamaModel: ollamaModel,
            CookieFilePath: cookieFilePath,
            DryRun: dryRun,
            IncludePatterns: includePatterns,
            ExcludePatterns: excludePatterns
        );

        return CliParseResult.Parsed;
    }

    #region Helpers

    private static string NextValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            Error($"{flag} requires a value.");
        return args[++i];
    }

    private static int NextInt(string[] args, ref int i, string flag)
    {
        var raw = NextValue(args, ref i, flag);
        if (!int.TryParse(raw, out var value) || value <= 0)
            Error($"{flag} requires a positive integer, got '{raw}'.");
        return value;
    }

    private static void Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine("Run with --help for usage information.");
        Environment.Exit(1);
    }
    #endregion
}

/// <summary>
/// Result of CLI argument parsing.
/// </summary>
public enum CliParseResult
{
    /// <summary>No CLI arguments supplied — use interactive prompts.</summary>
    Interactive,
    /// <summary>Arguments parsed successfully.</summary>
    Parsed,
    /// <summary>Help was shown or the process should exit.</summary>
    Exit
}
