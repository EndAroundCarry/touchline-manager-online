using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Ops;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Entities;

namespace TouchlineManager.Infrastructure.Tests.Ops;

/// <summary>
/// The feature-flag store over real PostgreSQL: an upsert keyed on <c>(scope, key)</c> whose value is a JSON
/// document (master plan §6.9, §13, `F-46`, ADR-0045).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureFlagPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public FeatureFlagPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_new_flag_is_stored_and_read_back_with_its_value()
    {
        await using var scope = _fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureFlagStore>();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var key = $"market.auctions_enabled.{Guid.NewGuid():N}";

        var written = store.Upsert(SetFeatureFlag.WorldScope, key, """{"enabled":false}""", null, Now);

        await db.SaveChangesAsync();

        written.Version.Should().Be(1);

        var read = await store.FindAsync(SetFeatureFlag.WorldScope, key, CancellationToken.None);

        read.Should().NotBeNull();
        read!.ValueJson.Should().Contain("\"enabled\"").And.Contain("false");
        read.RolloutMetadataJson.Should().BeNull();

        // The row is real jsonb, not text that happens to look like it.
        var jsonType = await db.Database
            .SqlQueryRaw<string>("select jsonb_typeof(value) as \"Value\" from ops.feature_flags where id = {0}", written.Id)
            .SingleAsync();

        jsonType.Should().Be("object");
    }

    [Fact]
    public async Task Setting_the_same_flag_again_updates_one_row_and_bumps_its_version()
    {
        await using var scope = _fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureFlagStore>();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var key = $"training.daily_progression.{Guid.NewGuid():N}";

        store.Upsert(SetFeatureFlag.WorldScope, key, "true", null, Now);
        await db.SaveChangesAsync();

        var updated = store.Upsert(SetFeatureFlag.WorldScope, key, "false", """{"reason":"incident"}""", Now);
        await db.SaveChangesAsync();

        updated.Version.Should().Be(2);

        var rows = await db.FeatureFlags.AsNoTracking()
            .Where(flag => flag.Scope == SetFeatureFlag.WorldScope && flag.Key == key)
            .ToListAsync();

        rows.Should().ContainSingle("the unique index keeps one row per switch");
        rows[0].ValueJson.Should().Be("false");
        rows[0].RolloutMetadataJson.Should().NotBeNull();
    }

    [Fact]
    public async Task A_scope_and_key_identify_at_most_one_flag()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var key = $"duplicate.{Guid.NewGuid():N}";

        db.FeatureFlags.Add(Flag(key, "true"));
        await db.SaveChangesAsync();

        db.FeatureFlags.Add(Flag(key, "false"));

        var act = async () => await db.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<DbUpdateException>();

        (exception.Which.InnerException as PostgresException)?.ConstraintName
            .Should().Be("ux_feature_flags_scope_key");
    }

    private static OpsFeatureFlag Flag(string key, string valueJson) => new()
    {
        Id = Guid.CreateVersion7(),
        Scope = SetFeatureFlag.WorldScope,
        Key = key,
        ValueJson = valueJson,
        Version = 1,
        CreatedAt = Now,
        UpdatedAt = Now,
    };
}
