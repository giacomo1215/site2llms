using Microsoft.Playwright;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Manages a shared Playwright browser instance across the application lifetime.
/// The browser is lazily initialized on first use and disposed on application shutdown.
/// </summary>
public interface IBrowserManager : IAsyncDisposable
{
    /// <summary>
    /// Returns the shared browser instance, initializing it on first call.
    /// Thread-safe for concurrent access.
    /// </summary>
    Task<IBrowser> GetBrowserAsync();
}
