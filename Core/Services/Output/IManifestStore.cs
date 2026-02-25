using site2llms.Core.Models;

namespace site2llms.Core.Services.Output;

/// <summary>
/// Contract for persisting and retrieving the per-host manifest used for cache-aware runs.
/// </summary>
public interface IManifestStore
{
    Task<Manifest> LoadAsync(Uri rootUrl, CancellationToken ct = default);
    Task SaveAsync(Uri rootUrl, Manifest manifest, CancellationToken ct = default);
}
