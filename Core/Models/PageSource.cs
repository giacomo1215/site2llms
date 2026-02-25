namespace site2llms.Core.Models;

/// <summary>
/// Lightweight descriptor of a discovered source URL.
/// Maintained for compatibility with discovery-oriented flows.
/// </summary>
/// <param name="Url">Absolute URL candidate.</param>
/// <param name="DiscoveryMethod">Strategy that found the URL.</param>
/// <param name="Depth">Depth used by crawl strategy.</param>
public record PageSource(
    Uri Url,
    string DiscoveryMethod,
    int Depth
);