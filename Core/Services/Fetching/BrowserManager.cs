using Microsoft.Playwright;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Singleton browser lifecycle manager. Lazily launches a Chromium instance with stealth
/// settings on first use and keeps it alive for the duration of the application run.
/// Concurrent callers safely share the same browser instance.
/// </summary>
public sealed class BrowserManager : IBrowserManager
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null) return _browser;

        await _initLock.WaitAsync();
        try
        {
            if (_browser is not null) return _browser;

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
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
        _initLock.Dispose();
    }
}
