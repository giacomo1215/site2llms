using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace site2llms.Core.Utils;

/// <summary>
/// Maintains a persistent Playwright browser context with stealth settings.
/// After solving JS challenges (e.g. SiteGround SGCaptcha) on the initial warm-up navigation,
/// all subsequent requests via <see cref="GetStringAsync"/> carry the same session/cookies.
/// Implements <see cref="IAsyncDisposable"/> for clean browser shutdown.
/// </summary>
public sealed class PlaywrightSession : IAsyncDisposable
{
    private readonly ILogger<PlaywrightSession> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private bool _disposed;

    public PlaywrightSession(ILogger<PlaywrightSession> logger)
    {
        _logger = logger;
    }

    /// <summary>Whether the warm-up detected and resolved a JS challenge page.</summary>
    public bool ChallengeWasResolved { get; private set; }

    /// <summary>Whether a challenge was detected but could not be solved.</summary>
    public bool ChallengeStillBlocked { get; private set; }

    /// <summary>
    /// Launches a stealth Chromium instance, navigates to <paramref name="rootUrl"/>,
    /// and waits for any challenge to resolve.
    /// </summary>
    public async Task WarmupAsync(
        string rootUrl,
        IReadOnlyList<CookieEntry>? existingCookies = null,
        CancellationToken ct = default)
    {
        var targetHost = new Uri(rootUrl).Host;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--disable-infobars",
                "--window-size=1920,1080"
            }
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-US",
            TimezoneId = "America/New_York"
        });

        await _context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        // Inject cookies that match the target domain.
        if (existingCookies is { Count: > 0 })
        {
            var relevant = existingCookies
                .Where(c => !string.IsNullOrWhiteSpace(c.Domain)
                            && (targetHost.EndsWith(c.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                                || c.Domain.TrimStart('.').EndsWith(targetHost, StringComparison.OrdinalIgnoreCase)))
                .Select(c => new Cookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = string.IsNullOrWhiteSpace(c.Path) ? "/" : c.Path
                })
                .ToList();

            if (relevant.Count > 0)
            {
                try
                {
                    await _context.AddCookiesAsync(relevant);
                }
                catch (PlaywrightException ex)
                {
                    _logger.LogWarning("Could not inject existing cookies: {Message}", ex.Message);
                }
            }
        }

        _page = await _context.NewPageAsync();

        await _page.GotoAsync(rootUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45_000
        });

        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
        {
            Timeout = 10_000
        });

        // Check for challenge and wait for it to resolve.
        var html = await _page.ContentAsync();
        var challengeLabel = ChallengeDetector.Detect(html);

        if (challengeLabel is not null)
        {
            _logger.LogInformation("Challenge detected ({Challenge}), waiting for resolution", challengeLabel);

            try
            {
                await _page.WaitForURLAsync(url => url != _page.Url, new PageWaitForURLOptions
                {
                    Timeout = 15_000
                });
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = 15_000
                });
            }
            catch (TimeoutException)
            {
                await Task.Delay(5_000, ct);
            }

            var postHtml = await _page.ContentAsync();
            var stillBlocked = ChallengeDetector.Detect(postHtml);

            if (stillBlocked is not null)
            {
                _logger.LogWarning("Challenge persists: {Challenge}", stillBlocked);
                ChallengeStillBlocked = true;
            }
            else
            {
                _logger.LogInformation("Challenge resolved — session ready");
                ChallengeWasResolved = true;
            }
        }
    }

    /// <summary>
    /// Fetches a URL using the browser context that solved the challenge.
    /// Uses <c>page.GotoAsync()</c> to carry all session cookies and TLS context.
    /// Returns the response body as a string, or null on failure.
    /// </summary>
    public async Task<PlaywrightResponse?> GetAsync(string url, CancellationToken ct = default)
    {
        if (_page is null || _disposed)
        {
            return null;
        }

        try
        {
            var response = await _page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });

            if (response is null)
            {
                return null;
            }

            var body = await response.TextAsync();
            var contentType = response.Headers.TryGetValue("content-type", out var ct2) ? ct2 : string.Empty;
            return new PlaywrightResponse((int)response.Status, body, contentType);
        }
        catch (PlaywrightException ex)
        {
            _logger.LogWarning("Playwright fetch failed for {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets the current page HTML content without navigating.
    /// </summary>
    public async Task<string?> GetPageContentAsync()
    {
        if (_page is null || _disposed) return null;
        return await _page.ContentAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_page is not null) await _page.CloseAsync();
        if (_context is not null) await _context.DisposeAsync();
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}

/// <summary>Response from a Playwright-based HTTP request.</summary>
public sealed record PlaywrightResponse(int StatusCode, string Body, string ContentType)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    public bool IsJson => ContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
}
