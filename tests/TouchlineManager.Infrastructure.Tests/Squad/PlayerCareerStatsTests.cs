using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Tests.World;

namespace TouchlineManager.Infrastructure.Tests.Squad;

/// <summary>
/// A player's career, aggregated from the season statistics against real PostgreSQL 17 (`STA-2`).
/// </summary>
/// <remarks>
/// Its own world, because it writes season statistics that the shared world collection's reads could see. The
/// statistics are inserted directly rather than simulated: the point is the aggregation across seasons, not
/// the matchday publication (which its own suite covers), so one line is written for each of two seasons and
/// the query is asked to sum them.
/// </remarks>
public sealed class PlayerCareerStatsTests : IAsyncLifetime, IDisposable
{
    private readonly CareerFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task A_players_career_sums_every_season_and_lists_them_newest_first()
    {
        Guid playerId;
        Guid clubId;
        var now = _fixture.Clock.UtcNow;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var contract = await db.PlayerContracts
                .Where(candidate => candidate.Status == ContractStatus.Active)
                .Select(candidate => new { candidate.PlayerId, candidate.ClubId })
                .FirstAsync();

            playerId = contract.PlayerId;
            clubId = contract.ClubId;

            var divisionSeasonOneId = await (
                from entry in db.ClubSeasonEntries
                join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                where entry.ClubId == clubId
                select divisionSeason.Id)
                .FirstAsync();

            var world = await db.GameWorlds.SingleAsync();
            var seasonOneId = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.Id == divisionSeasonOneId)
                .Select(divisionSeason => divisionSeason.SeasonId)
                .FirstAsync();
            var seasonOne = await db.Seasons.SingleAsync(season => season.Id == seasonOneId);

            var seasonTwo = Season.Create(
                Guid.CreateVersion7(),
                world.Id,
                seasonOne.SequenceNumber + 1,
                seasonOne.GameYear + 1,
                seasonOne.RuleSetVersion,
                DateOnly.FromDateTime(seasonOne.StartsAt.UtcDateTime),
                now);
            db.Seasons.Add(seasonTwo);

            var divisionId = await db.DivisionSeasons
                .Where(divisionSeason => divisionSeason.Id == divisionSeasonOneId)
                .Select(divisionSeason => divisionSeason.DivisionId)
                .FirstAsync();

            var divisionSeasonTwo = DivisionSeason.Create(
                Guid.CreateVersion7(),
                divisionId,
                seasonTwo.Id,
                "career-schedule-seed",
                "career-tie-seed",
                "career-tie-hash",
                now);
            db.DivisionSeasons.Add(divisionSeasonTwo);

            db.PlayerSeasonStats.Add(PlayerSeasonStat.Create(
                Guid.CreateVersion7(),
                divisionSeasonOneId,
                new PlayerSeasonStatLine
                {
                    PlayerId = playerId,
                    ClubId = clubId,
                    Appearances = 10,
                    Starts = 9,
                    MinutesPlayed = 800,
                    Goals = 4,
                    Assists = 2,
                    Shots = 20,
                    ShotsOnTarget = 10,
                    Saves = 0,
                    YellowCards = 1,
                    RedCards = 0,
                    RatingBasisPointsTotal = 72_000,
                    RatedAppearances = 10,
                },
                now));

            db.PlayerSeasonStats.Add(PlayerSeasonStat.Create(
                Guid.CreateVersion7(),
                divisionSeasonTwo.Id,
                new PlayerSeasonStatLine
                {
                    PlayerId = playerId,
                    ClubId = clubId,
                    Appearances = 8,
                    Starts = 8,
                    MinutesPlayed = 700,
                    Goals = 6,
                    Assists = 3,
                    Shots = 18,
                    ShotsOnTarget = 12,
                    Saves = 0,
                    YellowCards = 0,
                    RedCards = 0,
                    RatingBasisPointsTotal = 64_000,
                    RatedAppearances = 8,
                },
                now));

            await db.SaveChangesAsync(CancellationToken.None);
        }

        await using (var verify = _fixture.CreateScope())
        {
            var player = await verify.ServiceProvider.GetRequiredService<ISquadQueries>()
                .GetPlayerAsync(playerId, CancellationToken.None);

            player.Should().NotBeNull();

            var career = player!.Career;
            career.Should().NotBeNull("STA-2: a player who has appeared has a career");
            career!.SeasonsPlayed.Should().Be(2);

            career.Totals.Appearances.Should().Be(18);
            career.Totals.Goals.Should().Be(10);
            career.Totals.MinutesPlayed.Should().Be(1_500);

            // The rating is recomputed from the summed basis points and rated appearances, not averaged from
            // the season averages (TRN-8).
            career.Totals.AverageRatingBasisPoints.Should().Be((72_000 + 64_000) / 18);

            career.Seasons.Should().HaveCount(2);
            career.Seasons[0].SeasonNumber.Should().Be(2, "the most recent season is first");
            career.Seasons[0].Stats.Goals.Should().Be(6);
            career.Seasons[0].ClubName.Should().NotBeNullOrWhiteSpace();
            career.Seasons[1].SeasonNumber.Should().Be(1);
            career.Seasons[1].Stats.Goals.Should().Be(4);
        }
    }
}

/// <summary>The career tests' own seeded world.</summary>
public sealed class CareerFixture : WorldFixture
{
    /// <summary>Creates the fixture against its own database.</summary>
    public CareerFixture()
        : base("touchline_career")
    {
    }
}
