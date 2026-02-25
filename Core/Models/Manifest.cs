namespace site2llms.Core.Models;

/// <summary>
/// Persistent cache index keyed by source URL.
/// Each entry stores the last generated output metadata for that URL.
/// </summary>
public class Manifest
{
    /// <summary>
    /// URL-keyed map of generated artifacts and content hashes.
    /// Uses case-insensitive comparison for defensive matching.
    /// </summary>
    public Dictionary<string, ManifestEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Metadata persisted per source page so unchanged pages can be skipped on future runs.
/// </summary>
public class ManifestEntry
{
    /// <summary>
    /// Absolute source URL used as the identity key.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of extracted content used for cache hit detection.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Relative path of the generated page markdown file.
    /// </summary>
    public string RelativeOutputPath { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp (ISO-8601) when the summary was last generated.
    /// </summary>
    public string LastGeneratedAt { get; set; } = string.Empty;

    /// <summary>
    /// Last known title for this page, reused by index generation on cache hits.
    /// </summary>
    public string Title { get; set; } = string.Empty;
}
