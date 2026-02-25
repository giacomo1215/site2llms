using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using site2llms.Core.Models;

namespace site2llms.Core.Services.WordPress;

/// <summary>
/// Lightweight harness for validating WordPress REST detection/discovery behavior.
/// </summary>
public static class WordPressRestClientHarness
{
    /// <summary>
    /// Live check helper for manual verification against a real WP root URL.
    /// </summary>
    public static async Task<WordPressDiscoveryResult> RunLiveCheckAsync(string rootUrl, CancellationToken ct = default)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new WordPressRestClient(httpClient, NullLogger<WordPressRestClient>.Instance);

        var options = new CrawlOptions(
            RootUrl: rootUrl,
            MaxPages: 10,
            MaxDepth: 1,
            SameHostOnly: true,
            DelayMs: 0,
            OllamaBaseUrl: "http://localhost:11434",
            OllamaModel: "test"
        );

        var detection = await client.DetectAsync(new Uri(rootUrl), ct);
        if (!detection.IsWordPressRestAvailable)
        {
            return new WordPressDiscoveryResult([], 0, 0, true, detection.Reason);
        }

        return await client.DiscoverAsync(options, ct);
    }

    /// <summary>
    /// Deterministic mocked run for CI/local validation without network calls.
    /// </summary>
    public static async Task<WordPressDiscoveryResult> RunMockedAsync(CancellationToken ct = default)
    {
        var root = new Uri("https://examplewp.test/");
        using var httpClient = new HttpClient(new FakeWordPressHandler())
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var client = new WordPressRestClient(httpClient, NullLogger<WordPressRestClient>.Instance);
        var options = new CrawlOptions(
            RootUrl: root.ToString(),
            MaxPages: 5,
            MaxDepth: 1,
            SameHostOnly: true,
            DelayMs: 0,
            OllamaBaseUrl: "http://localhost:11434",
            OllamaModel: "test"
        );

        var detection = await client.DetectAsync(root, ct);
        if (!detection.IsWordPressRestAvailable)
        {
            throw new InvalidOperationException("Mocked WP REST detection failed unexpectedly.");
        }

        var result = await client.DiscoverAsync(options, ct);
        if (result.Items.Count == 0)
        {
            throw new InvalidOperationException("Mocked WP REST discovery should return at least one item.");
        }

        return result;
    }

    private sealed class FakeWordPressHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;

            if (pathAndQuery.StartsWith("/wp-json/", StringComparison.OrdinalIgnoreCase)
                && !pathAndQuery.Contains("/wp/v2/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Json("""
                    {
                      "name": "Example WP",
                      "routes": { "/wp/v2": {} },
                      "namespaces": ["wp/v2"]
                    }
                    """));
            }

            if (pathAndQuery.Contains("/wp-json/wp/v2/pages", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Json("""
                    [
                      {
                        "link": "https://examplewp.test/about/",
                        "title": { "rendered": "About" },
                        "content": { "rendered": "<p>About content from WordPress REST API.</p>" },
                        "excerpt": { "rendered": "<p>About excerpt</p>" },
                        "modified": "2026-02-01T10:00:00"
                      }
                    ]
                    """, totalPages: 1));
            }

            if (pathAndQuery.Contains("/wp-json/wp/v2/posts", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(Json("""
                    [
                      {
                        "link": "https://examplewp.test/hello-world/",
                        "title": { "rendered": "Hello World" },
                        "content": { "rendered": "<p>Hello World content with enough length for markdown extraction.</p>" },
                        "excerpt": { "rendered": "<p>Hello excerpt</p>" },
                        "modified": "2026-02-02T10:00:00"
                      }
                    ]
                    """, totalPages: 1));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent(string.Empty)
            });
        }

        private static HttpResponseMessage Json(string payload, int? totalPages = null)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (totalPages.HasValue)
            {
                message.Headers.TryAddWithoutValidation("X-WP-TotalPages", totalPages.Value.ToString());
                message.Headers.TryAddWithoutValidation("X-WP-Total", "2");
            }

            return message;
        }
    }
}