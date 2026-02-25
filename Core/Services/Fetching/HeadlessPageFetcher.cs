using site2llms.Core.Models;
using site2llms.Core.Utils;
using Microsoft.Playwright;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Fetches page HTML through a headless browser to support JS-rendered sites/challenge pages.
/// When a <see cref="PlaywrightSession"/> is provided, reuses the session that already solved
/// any site protection challenge. Otherwise falls back to creating a fresh browser instance
/// with stealth settings.
/// </summary>
public class HeadlessPageFetcher(
    IReadOnlyList<CookieEntry>? cookies = null,
    PlaywrightSession? session = null) : IPageFetcher
{
    public async Task<PageContent?> FetchAsync(Uri url, CancellationToken ct = default)
    {
        // Prefer the persistent session when available (challenge already solved).
        if (session is not null)
        {
            return await FetchViaSessionAsync(url, ct);
        }

        return await FetchStandaloneAsync(url, ct);
    }

    private async Task<PageContent?> FetchViaSessionAsync(Uri url, CancellationToken ct)
    {
        try
        {
            var response = await session!.GetAsync(url.AbsoluteUri, ct);
            if (response is null || string.IsNullOrWhiteSpace(response.Body))
            {
                return null;
            }

            // For HTML responses, parse the title from the page after navigation.
            var pageHtml = await session.GetPageContentAsync() ?? response.Body;
            var title = ExtractTitleFromHtml(pageHtml) ?? url.AbsolutePath.Trim('/').Replace('-', ' ');

            return new PageContent(
                Url: url,
                Title: title,
                ExtractedMarkdown: string.Empty,
                RawHtml: pageHtml,
                FetchedAt: DateTimeOffset.UtcNow,
                IsSkipped: false,
                SkipReason: null
            );
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<PageContent?> FetchStandaloneAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
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

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                Locale = "en-US",
                TimezoneId = "America/New_York"
            });

            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            // Filter cookies to the target domain to avoid invalid-cookie errors.
            if (cookies is { Count: > 0 })
            {
                var targetHost = url.Host;
                var relevant = cookies
                    .Where(c => !string.IsNullOrWhiteSpace(c.Domain)
                                && (targetHost.EndsWith(c.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                                    || c.Domain.TrimStart('.').EndsWith(targetHost, StringComparison.OrdinalIgnoreCase)))
                    .Select(c => new Microsoft.Playwright.Cookie
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
                        await context.AddCookiesAsync(relevant);
                    }
                    catch (PlaywrightException)
                    {
                        // Continue without cookies if injection fails.
                    }
                }
            }

            var page = await context.NewPageAsync();
            await page.GotoAsync(url.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45000
            });

            // Wait for challenge resolution if one is detected.
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
            {
                Timeout = 10000
            });

            var html = await page.ContentAsync();
            var challengeLabel = ChallengeDetector.Detect(html);
            if (challengeLabel is not null)
            {
                try
                {
                    await page.WaitForURLAsync(u => u != page.Url, new PageWaitForURLOptions { Timeout = 15_000 });
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
                }
                catch (TimeoutException)
                {
                    await Task.Delay(3_000, ct);
                }

                html = await page.ContentAsync();
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            var title = await page.TitleAsync();
            return new PageContent(
                Url: url,
                Title: string.IsNullOrWhiteSpace(title) ? url.AbsolutePath.Trim('/').Replace('-', ' ') : title,
                ExtractedMarkdown: string.Empty,
                RawHtml: html,
                FetchedAt: DateTimeOffset.UtcNow,
                IsSkipped: false,
                SkipReason: null
            );
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static string? ExtractTitleFromHtml(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, @"<title[^>]*>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }
}