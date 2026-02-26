using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.WordPress;

/// <summary>
/// WordPress REST helper that detects API availability, discovers posts/pages, and caches rendered content.
/// When a <see cref="PlaywrightSession"/> is set, routes requests through the headless browser
/// to bypass site protection that blocks plain HTTP (e.g. SiteGround SGCaptcha).
/// </summary>
public class WordPressRestClient(HttpClient httpClient, ILogger<WordPressRestClient> logger)
{
    private readonly Dictionary<string, WordPressRestItem> _contentCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan[] RetryBackoff =
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1000)
    };

    /// <summary>
    /// Optional Playwright session for bypassing challenge-protected sites.
    /// Set this before calling <see cref="DetectAsync"/> or <see cref="DiscoverAsync"/>.
    /// </summary>
    public PlaywrightSession? Session { get; set; }

    /// <summary>
    /// Checks whether the target root exposes WordPress REST APIs.
    /// </summary>
    public async Task<WordPressDetectionResult> DetectAsync(Uri rootUrl, CancellationToken ct = default)
    {
        var checks = new[]
        {
            new Uri(rootUrl, "/wp-json/"),
            new Uri(rootUrl, "/?rest_route=/")
        };

        string? challengeLabel = null;

        foreach (var endpoint in checks)
        {
            var response = await SendWithRetryAsync(endpoint, ct);
            if (response is null)
            {
                continue;
            }
            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    // Read body to check for challenge pages (e.g. SiteGround CAPTCHA).
                    var body = await response.Content.ReadAsStringAsync(ct);
                    challengeLabel ??= ChallengeDetector.Detect(body);
                    if (challengeLabel is not null)
                    {
                        logger.LogWarning("WP REST blocked by site protection: {Challenge} (HTTP {StatusCode} from {Endpoint})", challengeLabel, (int)response.StatusCode, endpoint);
                    }
                    return new WordPressDetectionResult(false, challengeLabel ?? $"HTTP {(int)response.StatusCode} from {endpoint}", challengeLabel is not null);
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode) continue;

                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                {
                    // Non-JSON success response — likely a challenge/interstitial page.
                    var body = await response.Content.ReadAsStringAsync(ct);
                    challengeLabel ??= ChallengeDetector.Detect(body);
                    if (challengeLabel is not null)
                    {
                        logger.LogWarning("WP REST blocked by site protection: {Challenge} (non-JSON response from {Endpoint})", challengeLabel, endpoint);
                    }
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                try
                {
                    using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (json.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var hasRoutes = json.RootElement.TryGetProperty("routes", out _);
                    var hasNamespace = json.RootElement.TryGetProperty("namespace", out _)
                        || json.RootElement.TryGetProperty("namespaces", out _);

                    if (hasRoutes || hasNamespace)
                    {
                        return new WordPressDetectionResult(true, null, false);
                    }
                }
                catch (JsonException)
                {
                    // Ignore HTML/challenge pages masquerading as JSON.
                }
            }
        }

        var reason = challengeLabel is not null
            ? $"WP REST blocked by site protection: {challengeLabel}"
            : "WP REST route not detected";

        // When blocked by challenge and a Playwright session is available, retry via headless browser.
        if (challengeLabel is not null && Session is { ChallengeWasResolved: true })
        {
            logger.LogInformation("Retrying WP REST detection via headless browser session");
            return await DetectViaSessionAsync(rootUrl, ct);
        }

        return new WordPressDetectionResult(false, reason, challengeLabel is not null);
    }

    /// <summary>
    /// Detects WP REST via the Playwright session that already solved the site challenge.
    /// </summary>
    private async Task<WordPressDetectionResult> DetectViaSessionAsync(Uri rootUrl, CancellationToken ct)
    {
        var checks = new[]
        {
            new Uri(rootUrl, "/wp-json/").AbsoluteUri,
            new Uri(rootUrl, "/?rest_route=/").AbsoluteUri
        };

        foreach (var endpoint in checks)
        {
            var response = await Session!.GetAsync(endpoint, ct);
            if (response is null) continue;

            if (!response.IsSuccess || !response.IsJson) continue;

            try
            {
                using var json = JsonDocument.Parse(response.Body);
                if (json.RootElement.ValueKind != JsonValueKind.Object) continue;

                var hasRoutes = json.RootElement.TryGetProperty("routes", out _);
                var hasNamespace = json.RootElement.TryGetProperty("namespace", out _)
                    || json.RootElement.TryGetProperty("namespaces", out _);

                if (hasRoutes || hasNamespace)
                {
                    logger.LogInformation("WP REST detected via headless browser");
                    return new WordPressDetectionResult(true, null, false);
                }
            }
            catch (JsonException)
            {
                // Not valid JSON, try next endpoint.
            }
        }

        return new WordPressDetectionResult(false, "WP REST not detected even via headless browser", true);
    }

    /// <summary>
    /// Discovers pages and posts through WP REST and fills an in-memory cache for fetch-stage usage.
    /// </summary>
    public async Task<WordPressDiscoveryResult> DiscoverAsync(CrawlOptions options, CancellationToken ct = default)
    {
        var root = new Uri(options.RootUrl);
        _contentCache.Clear();

        var pages = await FetchCollectionAsync(root, options, "pages", ct);
        if (pages.Disabled)
        {
            return new WordPressDiscoveryResult([], pages.Count, 0, true, pages.DisabledReason);
        }

        var remaining = Math.Max(0, options.MaxPages - pages.Items.Count);
        var posts = remaining > 0
            ? await FetchCollectionAsync(root, options with { MaxPages = remaining }, "posts", ct)
            : new WordPressCollectionResult([], 0, false, null);

        if (posts.Disabled)
        {
            return new WordPressDiscoveryResult([], pages.Count, posts.Count, true, posts.DisabledReason);
        }

        var combined = pages.Items
            .Concat(posts.Items)
            .DistinctBy(i => i.Url.AbsoluteUri)
            .Take(options.MaxPages)
            .ToList();

        foreach (var item in combined)
        {
            _contentCache[item.Url.AbsoluteUri] = item;
        }

        var discovered = combined
            .Select(i => new DiscoveredUrl(i.Url, "wordpress-rest", 0))
            .ToList();

        return new WordPressDiscoveryResult(discovered, pages.Count, posts.Count, false, null);
    }

    /// <summary>
    /// Tries to get cached rendered content for a URL discovered via WP REST.
    /// </summary>
    public bool TryGetCached(Uri url, out WordPressRestItem item)
    {
        return _contentCache.TryGetValue(url.AbsoluteUri, out item!);
    }

    private async Task<WordPressCollectionResult> FetchCollectionAsync(Uri root, CrawlOptions options, string type, CancellationToken ct)
    {
        // When a Playwright session is active (challenge site), use browser for REST requests.
        if (Session is { ChallengeWasResolved: true })
        {
            return await FetchCollectionViaSessionAsync(root, options, type, ct);
        }

        return await FetchCollectionViaHttpAsync(root, options, type, ct);
    }

    private async Task<WordPressCollectionResult> FetchCollectionViaSessionAsync(Uri root, CrawlOptions options, string type, CancellationToken ct)
    {
        var page = 1;
        var items = new List<WordPressRestItem>();

        while (items.Count < options.MaxPages)
        {
            var endpoint = $"{root.GetLeftPart(UriPartial.Authority)}/wp-json/wp/v2/{type}?per_page=100&page={page}&_fields=link,title,content,excerpt,modified,yoast_head_json,protected,type";

            var response = await Session!.GetAsync(endpoint, ct);
            if (response is null || !response.IsSuccess)
            {
                break;
            }

            if ((response.StatusCode == 403 || response.StatusCode == 401))
            {
                return new WordPressCollectionResult([], items.Count, true, $"HTTP {response.StatusCode} via headless");
            }

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(response.Body);
            }
            catch (JsonException)
            {
                break;
            }

            using (json)
            {
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
                {
                    break;
                }

                foreach (var entry in json.RootElement.EnumerateArray())
                {
                    var mapped = TryMapItem(entry, root, options);
                    if (mapped is null) continue;

                    items.Add(mapped);
                    if (items.Count >= options.MaxPages) break;
                }
            }

            // Headless navigation doesn't expose X-WP-TotalPages header, so paginate until empty.
            page++;

            if (options.DelayMs > 0)
            {
                await Task.Delay(options.DelayMs, ct);
            }
        }

        return new WordPressCollectionResult(items, items.Count, false, null);
    }

    private async Task<WordPressCollectionResult> FetchCollectionViaHttpAsync(Uri root, CrawlOptions options, string type, CancellationToken ct)
    {
        var page = 1;
        var items = new List<WordPressRestItem>();
        var totalCollected = 0;

        while (items.Count < options.MaxPages)
        {
            var endpoint = new Uri(root, $"/wp-json/wp/v2/{type}?per_page=100&page={page}&_fields=link,title,content,excerpt,modified,yoast_head_json,protected,type");

            using var response = await SendWithRetryAsync(endpoint, ct);
            if (response is null)
            {
                break;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                return new WordPressCollectionResult([], totalCollected, true, $"HTTP {(int)response.StatusCode} from {endpoint}");
            }

            if (!response.IsSuccessStatusCode)
            {
                break;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            JsonDocument json;
            try
            {
                json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException)
            {
                break;
            }

            using (json)
            {
                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
                {
                    break;
                }

                foreach (var entry in json.RootElement.EnumerateArray())
                {
                    var mapped = TryMapItem(entry, root, options);
                    if (mapped is null)
                    {
                        continue;
                    }

                    items.Add(mapped);
                    if (items.Count >= options.MaxPages)
                    {
                        break;
                    }
                }

                totalCollected += json.RootElement.GetArrayLength();
            }

            var totalPages = 0;
            var hasTotalPages = response.Headers.TryGetValues("X-WP-TotalPages", out var totalPagesValues)
                && int.TryParse(totalPagesValues.FirstOrDefault(), out totalPages);
            if (hasTotalPages && page >= totalPages)
            {
                break;
            }

            page++;

            if (options.DelayMs > 0)
            {
                await Task.Delay(options.DelayMs, ct);
            }
        }

        return new WordPressCollectionResult(items, items.Count, false, null);
    }

    private static WordPressRestItem? TryMapItem(JsonElement entry, Uri root, CrawlOptions options)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (entry.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "attachment", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (entry.TryGetProperty("protected", out var protectedElement)
            && protectedElement.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (!entry.TryGetProperty("link", out var linkElement) || string.IsNullOrWhiteSpace(linkElement.GetString()))
        {
            return null;
        }

        if (!Uri.TryCreate(linkElement.GetString(), UriKind.Absolute, out var url))
        {
            return null;
        }

        url = UrlUtils.Canonicalize(url);
        if (!UrlUtils.IsHttp(url))
        {
            return null;
        }

        if (options.SameHostOnly && !url.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = GetNestedString(entry, "title", "rendered");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = url.AbsolutePath.Trim('/').Replace('-', ' ');
        }

        var renderedHtml = GetNestedString(entry, "content", "rendered");
        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            renderedHtml = GetNestedString(entry, "excerpt", "rendered");
        }

        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            renderedHtml = GetNestedString(entry, "yoast_head_json", "og_description");
            if (!string.IsNullOrWhiteSpace(renderedHtml))
            {
                renderedHtml = $"<p>{WebUtility.HtmlEncode(renderedHtml)}</p>";
            }
        }

        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            return null;
        }

        var modified = TryParseDate(entry, "modified");
        return new WordPressRestItem(url, title, SanitizeHtml(renderedHtml), modified);
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(Uri endpoint, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= RetryBackoff.Length; attempt++)
        {
            var response = await httpClient.GetAsync(endpoint, ct);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (attempt >= RetryBackoff.Length)
                {
                    return response;
                }

                response.Dispose();
                await Task.Delay(RetryBackoff[attempt], ct);
                continue;
            }

            return response;
        }

        return null;
    }

    private static DateTimeOffset TryParseDate(JsonElement entry, string propertyName)
    {
        if (!entry.TryGetProperty(propertyName, out var value))
        {
            return DateTimeOffset.UtcNow;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static string GetNestedString(JsonElement root, string propertyName, string nestedPropertyName)
    {
        if (!root.TryGetProperty(propertyName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!parent.TryGetProperty(nestedPropertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    }

    private static string SanitizeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutScripts = Regex.Replace(html, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(withoutScripts, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Result of probing a WordPress site for REST API availability and any blocking challenges.
/// </summary>
public sealed record WordPressDetectionResult(bool IsWordPressRestAvailable, string? Reason, bool IsBlockedByChallenge);

/// <summary>
/// Discovery output used by the WP REST discovery strategy.
/// </summary>
public sealed record WordPressDiscoveryResult(
    IReadOnlyList<DiscoveredUrl> Items,
    int PagesCount,
    int PostsCount,
    bool Disabled,
    string? DisabledReason
);

/// <summary>
/// One content item fetched from WP REST endpoints.
/// </summary>
public sealed record WordPressRestItem(Uri Url, string Title, string RenderedHtml, DateTimeOffset ModifiedAtUtc);

internal sealed record WordPressCollectionResult(
    IReadOnlyList<WordPressRestItem> Items,
    int Count,
    bool Disabled,
    string? DisabledReason
);