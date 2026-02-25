using Microsoft.Playwright;

namespace site2llms.Core.Utils;

/// <summary>
/// Visits a site with a headless browser to solve JS-based challenges (e.g. SiteGround SGCaptcha)
/// and returns cookies that can be injected into an <see cref="System.Net.CookieContainer"/>
/// for subsequent plain-HTTP requests.
/// </summary>
public static class SiteWarmup
{
    /// <summary>
    /// Opens the root URL in a headless Chromium instance with stealth-like options,
    /// waits for challenge scripts to complete (including navigations/redirects),
    /// and returns all cookies set by the site.
    /// Returns an empty list when Playwright is unavailable or the page fails to load.
    /// </summary>
    public static async Task<IReadOnlyList<CookieEntry>> WarmupAsync(
        string rootUrl,
        IReadOnlyList<CookieEntry>? existingCookies = null,
        CancellationToken ct = default)
    {
        try
        {
            var targetHost = new Uri(rootUrl).Host;

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    // Stealth flags to reduce headless-detection surface.
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

            // Remove the navigator.webdriver flag that many challenge scripts check.
            await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            // Inject only cookies whose domain matches the target site.
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
                        await context.AddCookiesAsync(relevant);
                    }
                    catch (PlaywrightException ex)
                    {
                        Console.WriteLine($"  Could not inject existing cookies: {ex.Message} — proceeding without them.");
                    }
                }
            }

            var page = await context.NewPageAsync();

            // Navigate and wait for initial load.
            var response = await page.GotoAsync(rootUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 45_000
            });

            // Longer settle time for challenge scripts that set cookies asynchronously.
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
            {
                Timeout = 10_000
            });

            // Many challenge pages (SiteGround, Cloudflare) operate by:
            // 1. Running JS that computes a token  2. Setting cookies  3. Redirecting
            // Wait for a potential secondary navigation to complete.
            var html = await page.ContentAsync();
            var challengeDetected = ChallengeDetector.Detect(html);

            if (challengeDetected is not null)
            {
                Console.WriteLine($"  Challenge still present after first load ({challengeDetected}), waiting for it to resolve...");

                // Wait up to 15s for the challenge to trigger a navigation/redirect.
                try
                {
                    await page.WaitForURLAsync(url => url != page.Url, new PageWaitForURLOptions
                    {
                        Timeout = 15_000
                    });
                    // After redirect, wait for the destination to settle.
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                    {
                        Timeout = 15_000
                    });
                }
                catch (TimeoutException)
                {
                    // Challenge may resolve in-place (AJAX) without navigation.
                    // Give extra time for cookie-setting JS to finish.
                    await Task.Delay(5_000, ct);
                }

                var postChallengeHtml = await page.ContentAsync();
                var stillBlocked = ChallengeDetector.Detect(postChallengeHtml);
                if (stillBlocked is not null)
                {
                    Console.WriteLine($"  Challenge persists after wait: {stillBlocked}");
                    Console.WriteLine("  Tip: this site may block headless browsers entirely. Supply a cookie file from a real browser session.");
                }
                else
                {
                    Console.WriteLine("  Challenge resolved successfully.");
                }
            }

            // Extract all cookies the browser accumulated for this site.
            var browserCookies = await context.CookiesAsync();

            return browserCookies
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new CookieEntry(c.Name, c.Value, c.Domain, c.Path))
                .ToList();
        }
        catch (PlaywrightException ex)
        {
            Console.WriteLine($"  Headless warm-up failed: {ex.Message}");
            return Array.Empty<CookieEntry>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warm-up error: {ex.Message}");
            return Array.Empty<CookieEntry>();
        }
    }
}
