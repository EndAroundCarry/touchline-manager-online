using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The feature-flag store: an upsert over <c>ops.feature_flags</c> (master plan §6.9, §13).
/// </summary>
/// <remarks>
/// It stages and never saves, so a flag change commits with the audit row the caller records in the same unit
/// of work. Setting a flag reads the tracked row so an update bumps its version rather than inserting a
/// second row for the same switch; the unique index on <c>(scope, key)</c> is the guarantee behind that.
/// </remarks>
internal sealed class FeatureFlagStore : IFeatureFlagStore
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the store.</summary>
    public FeatureFlagStore(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<FeatureFlagSnapshot?> FindAsync(
        string scope,
        string key,
        CancellationToken cancellationToken)
    {
        var flag = await _dbContext.FeatureFlags
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Scope == scope && candidate.Key == key, cancellationToken);

        return flag is null ? null : Snapshot(flag);
    }

    /// <inheritdoc />
    public FeatureFlagSnapshot Upsert(
        string scope,
        string key,
        string valueJson,
        string? rolloutMetadataJson,
        DateTimeOffset now)
    {
        var flag = _dbContext.FeatureFlags
            .SingleOrDefault(candidate => candidate.Scope == scope && candidate.Key == key);

        if (flag is null)
        {
            flag = new OpsFeatureFlag
            {
                Id = Guid.CreateVersion7(),
                Scope = scope,
                Key = key,
                ValueJson = valueJson,
                RolloutMetadataJson = rolloutMetadataJson,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _dbContext.FeatureFlags.Add(flag);
        }
        else
        {
            flag.ValueJson = valueJson;
            flag.RolloutMetadataJson = rolloutMetadataJson;
            flag.Version++;
            flag.UpdatedAt = now;
        }

        return Snapshot(flag);
    }

    private static FeatureFlagSnapshot Snapshot(OpsFeatureFlag flag) =>
        new(flag.Id, flag.Scope, flag.Key, flag.ValueJson, flag.RolloutMetadataJson, flag.Version);
}
