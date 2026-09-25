using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Infrastructure.Tests.World;

/// <summary>
/// The Stage 6 schedule as it lands in a real, freshly seeded PostgreSQL 17 database (`CAL-8`, `CAL-9`).
/// </summary>
/// <remarks>
/// The generator's own tests prove the algorithm; only this can prove the seeder put a complete, legal,
/// reproducible fixture list against every division-season — and that the database's own constraints
/// refuse the malformed rows the domain would never write in the first place.
/// </remarks>
[Collection(WorldCollection.Name)]
public sealed class SeasonSchedulePersistenceTests : WorldTestBase
{
    /// <summary>Initializes the tests.</summary>
    public SeasonSchedulePersistenceTests(WorldFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Every_division_season_has_thirty_four_rounds_and_a_full_fixture_list()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var divisionSeasons = await db.DivisionSeasons.ToListAsync();

        divisionSeasons.Should().HaveCount(6, "one division-season per country in the seeded world");

        foreach (var divisionSeason in divisionSeasons)
        {
            var matchdays = await db.Matchdays
                .Where(matchday => matchday.DivisionSeasonId == divisionSeason.Id)
                .ToListAsync();

            matchdays.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
            matchdays.Select(matchday => matchday.RoundNumber).Should().BeEquivalentTo(Enumerable.Range(1, 34));
            matchdays.Should().OnlyContain(
                matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending,
                "nothing has been played yet");

            foreach (var matchday in matchdays)
            {
                matchday.LockAt.Should().Be(
                    matchday.KickoffAt.AddMinutes(-WorldRuleSet.TeamSheetLockMinutes),
                    "CAL-3 derives the lock from the kickoff");
                SeasonCalendar.IsMatchday(DateOnly.FromDateTime(matchday.KickoffAt.UtcDateTime)).Should().BeTrue();

                var fixtures = await db.Fixtures
                    .Where(fixture => fixture.MatchdayId == matchday.Id)
                    .ToListAsync();

                fixtures.Should().HaveCount(WorldRuleSet.ClubsPerDivision / 2, "nine fixtures a round");
                fixtures.Should().OnlyContain(fixture => fixture.Status == FixtureStatus.Scheduled);
                fixtures.Should().OnlyContain(fixture => fixture.KickoffAt == matchday.KickoffAt);
                fixtures.Should().OnlyContain(
                    fixture => fixture.HomeScore == null && fixture.AwayScore == null && fixture.MatchId == null,
                    "a scheduled fixture carries no result (MAT-7)");
            }
        }
    }

    [Fact]
    public async Task Every_club_plays_thirty_four_fixtures_split_seventeen_home_and_seventeen_away()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var clubIds = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .Select(club => club.Id)
            .ToListAsync();

        var fixtures = await db.Fixtures.ToListAsync();

        foreach (var clubId in clubIds)
        {
            var home = fixtures.Count(fixture => fixture.HomeClubId == clubId);
            var away = fixtures.Count(fixture => fixture.AwayClubId == clubId);

            home.Should().Be(17, "a club hosts each of its division's other seventeen clubs once");
            away.Should().Be(17);
        }
    }

    [Fact]
    public async Task The_stored_schedule_is_exactly_what_its_seed_regenerates()
    {
        // CAL-8: the fixture list is reproducible from the division-season's recorded seed and the clubs'
        // stable order, which is the identity-generation order the seeder used.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var country = Fixture.Countries[0];
        var identities = ClubIdentityGenerator.GenerateDivision(
            WorldFixture.Seed,
            country.NamePoolKey,
            country.Code,
            tierNumber: 1,
            WorldRuleSet.ClubsPerDivision);

        var clubsByName = await db.Clubs
            .Where(club => club.CountryId == country.Id)
            .ToDictionaryAsync(club => club.Name, club => club.Id);

        var clubIds = identities.Select(identity => clubsByName[identity.Name]).ToList();

        var division = await db.Divisions.SingleAsync(
            candidate => candidate.CountryId == country.Id && candidate.TierNumber == 1);

        var divisionSeason = await db.DivisionSeasons.SingleAsync(
            candidate => candidate.DivisionId == division.Id);

        var regenerated = RoundRobinSchedule.Generate(
            clubIds,
            DeterministicDigest.SeedOf(divisionSeason.ScheduleSeed));

        var matchdayIds = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeason.Id)
            .Select(matchday => matchday.Id)
            .ToListAsync();

        var stored = await db.Fixtures
            .Where(fixture => matchdayIds.Contains(fixture.MatchdayId))
            .Join(
                db.Matchdays,
                fixture => fixture.MatchdayId,
                matchday => matchday.Id,
                (fixture, matchday) => new
                {
                    matchday.RoundNumber,
                    fixture.HomeClubId,
                    fixture.AwayClubId,
                })
            .ToListAsync();

        var expected = regenerated
            .SelectMany(round => round.Pairings.Select(
                pairing => new
                {
                    round.RoundNumber,
                    HomeClubId = pairing.HomeClubId,
                    AwayClubId = pairing.AwayClubId,
                }))
            .ToList();

        stored.Should().BeEquivalentTo(
            expected,
            "the stored schedule must be exactly what the seed regenerates");
    }

    [Fact]
    public async Task A_staged_fixture_without_a_result_is_refused_by_the_database()
    {
        // MIG-7: the check constraints are covered against real PostgreSQL, not an in-memory provider.
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var fixture = await db.Fixtures.FirstAsync();

        // The row keeps a legal score-free shape; only the status is forced, so the result-presence check
        // is the constraint that must fire.
        var update = () => db.Database.ExecuteSqlRawAsync(
            "update competition.fixtures set status = 'staged' where id = {0}",
            fixture.Id);

        var act = await update.Should().ThrowAsync<PostgresException>();

        act.Which.ConstraintName.Should().Be("ck_fixtures_result_presence");
    }

    [Fact]
    public async Task A_fixture_cannot_be_made_to_play_itself_in_the_database_either()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var fixture = await db.Fixtures.FirstAsync();

        var update = () => db.Database.ExecuteSqlRawAsync(
            "update competition.fixtures set away_club_id = home_club_id where id = {0}",
            fixture.Id);

        var act = await update.Should().ThrowAsync<PostgresException>();

        act.Which.ConstraintName.Should().Be("ck_fixtures_distinct_clubs");
    }

    [Fact]
    public async Task Every_fixture_belongs_to_clubs_in_its_own_country()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var countriesByClub = await db.Clubs
            .Where(club => club.WorldId == Fixture.WorldId)
            .ToDictionaryAsync(club => club.Id, club => club.CountryId);

        var rows = await db.Fixtures
            .Join(
                db.Matchdays,
                fixture => fixture.MatchdayId,
                matchday => matchday.Id,
                (fixture, matchday) => new { fixture, matchday.DivisionSeasonId })
            .Join(
                db.DivisionSeasons,
                row => row.DivisionSeasonId,
                divisionSeason => divisionSeason.Id,
                (row, divisionSeason) => new { row.fixture, divisionSeason.DivisionId })
            .Join(
                db.Divisions,
                row => row.DivisionId,
                division => division.Id,
                (row, division) => new { row.fixture, division.CountryId })
            .ToListAsync();

        rows.Should().NotBeEmpty();

        foreach (var row in rows)
        {
            countriesByClub[row.fixture.HomeClubId].Should().Be(
                row.CountryId,
                "a fixture is inside one country's pyramid (CAL-7)");
            countriesByClub[row.fixture.AwayClubId].Should().Be(row.CountryId);
        }
    }
}
