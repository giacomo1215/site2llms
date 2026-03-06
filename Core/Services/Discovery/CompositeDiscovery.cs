using site2llms.Core.Models;

namespace site2llms.Core.Services.Discovery;

/// <summary>
/// Runs discovery strategies in order, merging their results.
/// Earlier strategies have precedence when the same URL is found multiple times.
/// </summary>
public class CompositeDiscovery : IUrlDiscovery
{
    private readonly IReadOnlyList<IUrlDiscovery> _strategies;


    public CompositeDiscovery(IEnumerable<IUrlDiscovery> strategies) => _strategies = strategies.ToList();


    public async Task<IReadOnlyList<DiscoveredUrl>> DiscoverAsync(
        CrawlOptions options,
        CancellationToken ct = default)
    {
        var merged = new List<DiscoveredUrl>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var strategy in _strategies)
        {
            if (merged.Count >= options.MaxPages)
                break;

            var urls = await strategy.DiscoverAsync(options, ct);

            foreach (var url in urls)
            {
                if (seen.Add(url.Url.AbsoluteUri))
                {
                    merged.Add(url);

                    if (merged.Count >= options.MaxPages) break;
                }
            }
        }

        return merged;
    }
}