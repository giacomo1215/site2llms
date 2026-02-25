using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Fetching;

/// <summary>
/// Uses a primary fetcher first, then retries with headless browser when response looks blocked/insufficient.
/// Reports the specific challenge type clearly when detected.
/// </summary>
public class HeadlessFallbackPageFetcher(
    IPageFetcher primaryFetcher,
    IPageFetcher headlessFetcher,
    ILogger<HeadlessFallbackPageFetcher> logger) : IPageFetcher
{
    public async Task<PageContent?> FetchAsync(Uri url, CancellationToken ct = default)
    {
        var primary = await primaryFetcher.FetchAsync(url, ct);

        // Diagnose primary response.
        var challengeLabel = ChallengeDetector.Detect(primary?.RawHtml);
        var isThin = ChallengeDetector.IsTooThin(primary?.RawHtml);

        if (challengeLabel is null && !isThin && primary is not null)
        {
            return primary;
        }

        // Log the specific reason for fallback.
        if (challengeLabel is not null)
        {
            logger.LogWarning("Protection detected: {Challenge} — retrying with headless browser", challengeLabel);
        }
        else if (isThin)
        {
            logger.LogWarning("Response too thin ({Length} bytes) — retrying with headless browser", primary?.RawHtml?.Length ?? 0);
        }
        else
        {
            logger.LogWarning("Primary fetch failed — retrying with headless browser");
        }

        var headless = await headlessFetcher.FetchAsync(url, ct);

        // Check whether headless also hit a challenge.
        var headlessChallenge = ChallengeDetector.Detect(headless?.RawHtml);
        if (headlessChallenge is not null)
        {
            logger.LogWarning("Headless browser also blocked: {Challenge}. Tip: supply a cookie file from a real browser session", headlessChallenge);
        }
        else if (ChallengeDetector.IsTooThin(headless?.RawHtml))
        {
            logger.LogWarning("Headless browser returned thin content ({Length} bytes). Tip: supply a cookie file from a real browser session", headless?.RawHtml?.Length ?? 0);
        }

        return headless ?? primary;
    }
}