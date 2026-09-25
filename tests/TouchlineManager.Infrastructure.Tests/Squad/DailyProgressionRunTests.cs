using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Squad.Training;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// The daily progression run over a real seeded world (master plan §7.2; `TRN-3`, `TRN-9`, `TRN-10`).
/// </summary>
/// <remarks>
/// <para>
/// This is the guarantee the pure calculator tests cannot make: that the run reaches every club and player
/// in the world, writes through the real tables, and is idempotent for a day so a retried job cannot develop
/// a player twice (master plan §15.2).
/// </para>
/// <para>
/// Its own collection, and therefore its own seeded world, because the run mutates player state that the
/// world module's seeding tests assert is exactly what the generator produced. Sharing their world would
/// make this test's outcome depend on which class xUnit happened to run first.
/// </para>
/// </remarks>
[Collection(Name)]
public sealed class DailyProgressionRunTests
{
    /// <summary>The collection name. Its own, so the run never races the world module's reads.</summary>
    public const string Name = "training";

    private readonly WorldFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public DailyProgressionRunTests(WorldFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task One_run_advances_every_player_and_re_running_the_same_day_does_nothing()
    {
        var day = new DateOnly(2026, 9, 25);

        // Six countries, one tier each, eighteen clubs apiece, twenty-two players apiece (WORLD-4, SQ-1).
        const int clubs = 6 * 18;
        const int players = clubs * 22;

        await using (var scope = _fixture.CreateScope())
        {
            var run = scope.ServiceProvider.GetRequiredService<RunDailyProgression>();
            var result = await run.ExecuteAsync(day, CancellationToken.None);

            result.Clubs.Should().Be(clubs);
            result.Players.Should().Be(players);
        }

        await AssertProgressedAsync(day);

        await using (var scope = _fixture.CreateScope())
        {
            // A retry of the same day finds every player already progressed and writes nothing.
            var repeat = await scope.ServiceProvider
                .GetRequiredService<RunDailyProgression>()
                .ExecuteAsync(day, CancellationToken.None);

            repeat.Players.Should().Be(0, "the run is idempotent for a day");
        }

        await using (var scope = _fixture.CreateScope())
        {
            var tomorrow = await scope.ServiceProvider
                .GetRequiredService<RunDailyProgression>()
                .ExecuteAsync(day.AddDays(1), CancellationToken.None);

            tomorrow.Players.Should().Be(players, "the next day progresses everyone again");
        }
    }

    private async Task AssertProgressedAsync(DateOnly day)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var stale = await db.PlayerStates.CountAsync(
            state => state.LastProgressionDate == null || state.LastProgressionDate != day);

        stale.Should().Be(0, "every player was stamped with the day the run was for (TRN-9)");

        var attributes = await db.PlayerAttributes.ToListAsync();

        attributes.Should().NotBeEmpty();
        attributes.Should().OnlyContain(row => row.ChecksumMatches(), "the write restamped the checksum");
        attributes.Should().OnlyContain(
            row => row.ToSet().IsWithinScale,
            "development never leaves the displayed scale (TRN-4)");

        var states = await db.PlayerStates.ToListAsync();

        states.Should().OnlyContain(state => state.DevelopmentRemainder >= 0, "TRN-10");
        states.Should().OnlyContain(state => state.DevelopmentRemainder < DailyProgression.DevelopmentBasis);
    }
}

/// <summary>Shares one seeded world with the progression run, isolated from the world module's collection.</summary>
[CollectionDefinition(DailyProgressionRunTests.Name)]
public sealed class TrainingCollection : ICollectionFixture<WorldFixture>
{
}
