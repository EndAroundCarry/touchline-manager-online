using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.Ops;

/// <summary>
/// The read-only reader over real PostgreSQL: the incident flag, interpreted leniently and cached briefly
/// (master plan §13, `F-51`, ADR-0047).
/// </summary>
/// <remarks>
/// Each test establishes the flag state it asserts and drops the cache first, because the collection shares
/// one database and one memory cache. The row is written and read through the same store the operator uses,
/// so the test exercises the real encoding of the value.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ReadOnlyModeReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ReadOnlyModeReaderTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_unset_flag_is_not_read_only()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        await ClearAsync(db);
        readOnly.Invalidate();

        var state = await readOnly.GetStateAsync(CancellationToken.None);

        state.Enabled.Should().BeFalse();
        state.Message.Should().BeNull();
    }

    [Fact]
    public async Task An_enabled_flag_reads_back_with_its_message()
    {
        await using var scope = _fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureFlagStore>();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        store.Upsert(
            IncidentFlags.WorldScope,
            IncidentFlags.ReadOnly,
            """{"enabled":true,"message":"Read-only while we repair the ledger."}""",
            null,
            Now);

        await db.SaveChangesAsync();
        readOnly.Invalidate();

        var state = await readOnly.GetStateAsync(CancellationToken.None);

        state.Enabled.Should().BeTrue();
        state.Message.Should().Be("Read-only while we repair the ledger.");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("""{"enabled":false,"message":"paused"}""")]
    [InlineData("""{"enabled":"yes"}""")]
    [InlineData("""{"other":true}""")]
    [InlineData("123")]
    public async Task A_disabled_or_misshapen_value_is_not_read_only(string valueJson)
    {
        await using var scope = _fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureFlagStore>();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        store.Upsert(IncidentFlags.WorldScope, IncidentFlags.ReadOnly, valueJson, null, Now);
        await db.SaveChangesAsync();
        readOnly.Invalidate();

        var state = await readOnly.GetStateAsync(CancellationToken.None);

        state.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task The_state_is_cached_until_it_is_invalidated()
    {
        await using var scope = _fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureFlagStore>();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        store.Upsert(
            IncidentFlags.WorldScope,
            IncidentFlags.ReadOnly,
            """{"enabled":true}""",
            null,
            Now);
        await db.SaveChangesAsync();

        readOnly.Invalidate();

        (await readOnly.GetStateAsync(CancellationToken.None)).Enabled.Should().BeTrue();

        // The switch is turned back off in the store, but the reader keeps serving its cached answer.
        store.Upsert(
            IncidentFlags.WorldScope,
            IncidentFlags.ReadOnly,
            """{"enabled":false}""",
            null,
            Now);
        await db.SaveChangesAsync();

        (await readOnly.GetStateAsync(CancellationToken.None)).Enabled.Should().BeTrue(
            "the read is cached until it is invalidated or expires");

        // Invalidating — what the flag command does in the process that reads it — drops the cache.
        readOnly.Invalidate();

        (await readOnly.GetStateAsync(CancellationToken.None)).Enabled.Should().BeFalse();
    }

    private static async Task ClearAsync(TouchlineManagerDbContext db)
    {
        var existing = await db.FeatureFlags.SingleOrDefaultAsync(
            flag => flag.Scope == IncidentFlags.WorldScope && flag.Key == IncidentFlags.ReadOnly);

        if (existing is not null)
        {
            db.FeatureFlags.Remove(existing);
            await db.SaveChangesAsync();
        }
    }
}
