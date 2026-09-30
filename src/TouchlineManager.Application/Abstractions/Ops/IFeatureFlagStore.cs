namespace TouchlineManager.Application.Abstractions.Ops;

/// <summary>One stored feature flag, as the operator's command reads and writes it.</summary>
/// <param name="Id">The flag's identity.</param>
/// <param name="Scope">The scope it applies to, e.g. <c>world</c>.</param>
/// <param name="Key">The flag's stable key.</param>
/// <param name="ValueJson">The flag's value, as a JSON document.</param>
/// <param name="RolloutMetadataJson">Optional rollout metadata, as a JSON document, or null.</param>
/// <param name="Version">The row's version, which increments on every change.</param>
public sealed record FeatureFlagSnapshot(
    Guid Id,
    string Scope,
    string Key,
    string ValueJson,
    string? RolloutMetadataJson,
    long Version);

/// <summary>
/// Persistence for the operator's feature flags (`ops.feature_flags`, master plan §6.9, §13).
/// </summary>
/// <remarks>
/// It stages and never saves, so a flag change commits with its audit row. The store is deliberately narrow:
/// this milestone only writes flags through the admin surface. Reading a flag to gate behaviour is the
/// incident-control milestone's work (F-51), which will read through this same port.
/// </remarks>
public interface IFeatureFlagStore
{
    /// <summary>Finds a flag by scope and key, or null when it is not set.</summary>
    /// <param name="scope">The scope, e.g. <c>world</c>.</param>
    /// <param name="key">The flag's key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FeatureFlagSnapshot?> FindAsync(string scope, string key, CancellationToken cancellationToken);

    /// <summary>Creates the flag or replaces its value, returning the stored row.</summary>
    /// <param name="scope">The scope, e.g. <c>world</c>.</param>
    /// <param name="key">The flag's key.</param>
    /// <param name="valueJson">The flag's value, as a JSON document.</param>
    /// <param name="rolloutMetadataJson">Optional rollout metadata, as a JSON document, or null.</param>
    /// <param name="now">The current instant.</param>
    FeatureFlagSnapshot Upsert(
        string scope,
        string key,
        string valueJson,
        string? rolloutMetadataJson,
        DateTimeOffset now);
}
