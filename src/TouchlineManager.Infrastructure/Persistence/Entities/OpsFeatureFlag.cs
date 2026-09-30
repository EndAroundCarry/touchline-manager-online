namespace TouchlineManager.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for a feature-flag row in <c>ops.feature_flags</c> (master plan §6.9, §13).
/// </summary>
/// <remarks>
/// This is the ops module's storage shape, not a domain aggregate: a flag is an operator's switch, and the
/// value is an opaque JSON document whose shape is the flag's own business. Reads and writes go through
/// <c>IFeatureFlagStore</c>.
/// </remarks>
public sealed class OpsFeatureFlag
{
    /// <summary>Gets or sets the flag identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the scope the flag applies to, e.g. <c>world</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the flag's stable key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the flag's value, as a JSON document.</summary>
    public string ValueJson { get; set; } = "null";

    /// <summary>Gets or sets optional rollout metadata, as a JSON document, or null.</summary>
    public string? RolloutMetadataJson { get; set; }

    /// <summary>Gets or sets the optimistic concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
