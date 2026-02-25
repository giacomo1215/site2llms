using System.Text.Json;
using site2llms.Core.Models;
using site2llms.Core.Utils;

namespace site2llms.Core.Services.Output;

/// <summary>
/// Persists and retrieves the per-host manifest used for cache-aware runs.
/// </summary>
public class ManifestStore : IManifestStore
{
    // Shared JSON options for both read and write to keep schema behavior consistent.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads manifest from disk, returning an empty manifest when missing or null.
    /// </summary>
    public async Task<Manifest> LoadAsync(Uri rootUrl, CancellationToken ct = default)
    {
        var path = GetManifestPath(rootUrl);
        if (!File.Exists(path))
        {
            return new Manifest();
        }

        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, ct);
        return manifest ?? new Manifest();
    }

    /// <summary>
    /// Saves manifest to disk under the host output folder.
    /// </summary>
    public async Task SaveAsync(Uri rootUrl, Manifest manifest, CancellationToken ct = default)
    {
        var path = GetManifestPath(rootUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct);
    }

    /// <summary>
    /// Resolves manifest path for the provided site root URL.
    /// </summary>
    public static string GetManifestPath(Uri rootUrl)
    {
        var hostFolder = UrlUtils.SafeHost(rootUrl);
        return Path.Combine("output", hostFolder, "manifest.json");
    }
}
