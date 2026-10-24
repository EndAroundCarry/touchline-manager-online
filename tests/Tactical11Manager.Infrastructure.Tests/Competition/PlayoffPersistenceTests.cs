using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Infrastructure.Persistence;
using FixtureEntity = Tactical11Manager.Domain.Competition.Fixture;

namespace Tactical11Manager.Infrastructure.Tests.Competition;

/// <summary>
/// The promotion play-off schema and queries against real PostgreSQL: the matchday kind, the stored bracket, and every
/// read that must ignore a play-off round or see across leagues (`PR-4`, `TBL-13`).
/// </summary>
/// <remarks>
/// <para>
/// All the tests share one arrangement made once in the French leagues, which no other test in the collection touches: a
/// semi-final in the second tier and a final in the first, each a real fixture that was locked, resolved and published
/// through the real workflow's own steps. The publication itself is not used, because it is the next milestone's pipeline
/// that will skip the table and the money for a play-off round; what is under test here is that the reads already know the
/// difference.
/// </para>
/// <para>
/// A league-only read is asserted against what it would hold without the round. The French second tier has played no
/// league round, so a count of its published rounds is nought before the semi-final and one if the read counted it.
/// </para>
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class PlayoffPersistenceTests
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Arrangement? _arrangement;

    /// <summary>Initializes the tests.</summary>
    public PlayoffPersistenceTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_play_off_round_is_stored_in_its_hosts_division_season_and_a_league_round_stays_a_league_round()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        var semi = await db.Matchdays.AsNoTracking().SingleAsync(matchday => matchday.Id == arranged.SemiMatchdayId);
        var final = await db.Matchdays.AsNoTracking().SingleAsync(matchday => matchday.Id == arranged.FinalMatchdayId);

        semi.Kind.Should().Be(MatchdayKind.Playoff);
        semi.RoundNumber.Should().Be(WorldRuleSet.MatchdaysPerSeason + WorldRuleSet.PlayoffSemiSlot);
        semi.DivisionSeasonId.Should().Be(arranged.FeederDivisionSeasonId, "a semi-final is hosted in the lower league");
        final.Kind.Should().Be(MatchdayKind.Playoff);
        final.RoundNumber.Should().Be(WorldRuleSet.MatchdaysPerSeason + WorldRuleSet.PlayoffFinalSlot);
        final.DivisionSeasonId.Should().Be(arranged.ParentDivisionSeasonId, "a final is hosted in the higher league");

        var leagueRounds = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == arranged.FeederDivisionSeasonId && matchday.Kind == MatchdayKind.League)
            .CountAsync();

        leagueRounds.Should().Be(WorldRuleSet.MatchdaysPerSeason, "a play-off round is in addition to the thirty league rounds");
    }

    [Fact]
    public async Task The_bracket_is_listed_and_found_by_fixture_with_its_decision_and_its_waiting_final()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var ties = scope.ServiceProvider.GetRequiredService<IPlayoffRepository>();

        var listed = await ties.ListBySeasonAsync(arranged.SeasonId, CancellationToken.None);

        listed.Select(tie => tie.Stage).Should().Equal(PlayoffStage.SemiFinal, PlayoffStage.Final);

        var semi = listed[0];

        semi.IsDecided.Should().BeTrue();
        semi.WinnerClubId.Should().Be(arranged.SemiHomeClubId);
        semi.LoserClubId.Should().Be(arranged.SemiAwayClubId);
        semi.DecidedBy.Should().Be(PlayoffDecision.Penalties);
        semi.ShootoutHomeScore.Should().Be(4);
        semi.ShootoutAwayScore.Should().Be(3);
        semi.ShootoutKicks.Should().Contain("\"taker\"", "the kicks are stored as the shoot-out produced them");

        var final = listed[1];

        final.IsDecided.Should().BeFalse();
        final.HomeClubId.Should().Be(arranged.HolderClubId);
        final.AwayClubId.Should().Be(arranged.SemiHomeClubId, "the semi-final winner filled the waiting away side");
        final.DecidedBy.Should().BeNull();

        (await ties.FindByFixtureAsync(arranged.SemiFixtureId, CancellationToken.None))!.Id.Should().Be(semi.Id);
        (await ties.FindByFixtureAsync(arranged.FinalFixtureId, CancellationToken.None))!.Id.Should().Be(final.Id);
        (await ties.FindByFixtureAsync(Guid.CreateVersion7(), CancellationToken.None)).Should().BeNull();

        (await ties.CountUndecidedAsync(arranged.SeasonId, CancellationToken.None)).Should().Be(1);
        (await ties.IsSeasonSettledAsync(arranged.SeasonId, CancellationToken.None))
            .Should().BeFalse("the final is undecided and the league rounds are not all published");
    }

    [Fact]
    public async Task A_season_is_settled_only_when_every_round_is_published_and_no_tie_is_undecided()
    {
        await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var ties = scope.ServiceProvider.GetRequiredService<IPlayoffRepository>();
        var now = Fixture.Clock.UtcNow;

        // Everything below is rolled back: another season, its division-season and its round would otherwise change what the
        // other tests in the collection mean by "the last division-season".
        await using var transaction = await db.Database.BeginTransactionAsync();

        var current = await CurrentSeasonAsync(db);
        var seasonId = Guid.CreateVersion7();

        db.Seasons.Add(Season.Create(
            seasonId,
            current.WorldId,
            sequenceNumber: 99,
            gameYear: current.GameYear + 98,
            current.RuleSetVersion,
            DateOnly.FromDateTime(current.StartsAt.UtcDateTime).AddDays(7 * 700),
            now));
        await db.SaveChangesAsync();

        (await ties.IsSeasonSettledAsync(seasonId, CancellationToken.None))
            .Should().BeTrue("a season with no round and no tie has nothing left to play");

        var (parent, feeder, homeClub, awayClub) = await FrenchLeaguesAsync(db);
        var feederSeason = DivisionSeason.Create(Guid.CreateVersion7(), feeder.Id, seasonId, "seed", "seed", "hash", now);
        var round = Matchday.Schedule(Guid.CreateVersion7(), feederSeason.Id, 1, now.AddDays(3), now);
        var fixture = ScheduleFixture(round.Id, homeClub, awayClub, round.KickoffAt, now);

        db.DivisionSeasons.Add(feederSeason);
        db.Matchdays.Add(round);
        await db.SaveChangesAsync();
        db.Fixtures.Add(fixture);
        await db.SaveChangesAsync();

        (await ties.IsSeasonSettledAsync(seasonId, CancellationToken.None))
            .Should().BeFalse("a pending round is still to be played");

        round.MarkStaged(now);
        round.Publish(now);
        await db.SaveChangesAsync();

        (await ties.IsSeasonSettledAsync(seasonId, CancellationToken.None)).Should().BeTrue();

        var tie = PlayoffTie.Draw(
            Guid.CreateVersion7(),
            seasonId,
            feeder.CountryId,
            new PlayoffPairing(
                PlayoffStage.SemiFinal,
                WorldRuleSet.PlayoffSemiSlot,
                parent.Id,
                feeder.Id,
                feeder.Id,
                homeClub,
                feeder.Id,
                3,
                awayClub,
                feeder.Id,
                4),
            now);

        tie.AttachFixture(fixture.Id, now);
        ties.Add(tie);
        await db.SaveChangesAsync();

        (await ties.CountUndecidedAsync(seasonId, CancellationToken.None)).Should().Be(1);
        (await ties.IsSeasonSettledAsync(seasonId, CancellationToken.None))
            .Should().BeFalse("a tie without a winner keeps the season open even when every round is published");

        tie.Decide(homeClub, PlayoffDecision.Score, null, null, null, now);
        await db.SaveChangesAsync();

        (await ties.CountUndecidedAsync(seasonId, CancellationToken.None)).Should().Be(0);
        (await ties.IsSeasonSettledAsync(seasonId, CancellationToken.None)).Should().BeTrue();

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task A_clubs_placement_is_its_division_season_league_and_tier_from_its_season_entry()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var matchdays = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();

        var placements = await matchdays.LoadClubPlacementsAsync(
            arranged.SeasonId,
            [arranged.HolderClubId, arranged.SemiHomeClubId, Guid.CreateVersion7()],
            CancellationToken.None);

        placements.Should().BeEquivalentTo(
            [
                new ClubSeasonPlacement(arranged.HolderClubId, arranged.ParentDivisionSeasonId, arranged.ParentDivisionId, 1, 1),
                new ClubSeasonPlacement(arranged.SemiHomeClubId, arranged.FeederDivisionSeasonId, arranged.FeederDivisionId, 2, 1),
            ],
            "an unknown club has no placement");

        (await matchdays.LoadClubPlacementsAsync(arranged.SeasonId, [], CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_leagues_table_and_statistics_reads_ignore_a_published_play_off_round()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var matchdays = scope.ServiceProvider.GetRequiredService<IMatchdayRepository>();

        (await matchdays.LoadMatchStatEventsAsync(arranged.SemiMatchdayId, CancellationToken.None))
            .Should().NotBeEmpty("the play-off match was played and has shots, so the leagues' reads below are not empty by accident");

        (await matchdays.LoadMatchLoadsAsync(arranged.SemiMatchdayId, CancellationToken.None))
            .Should().ContainSingle("the round's own reads still see the match, because the publication loads its players");

        var table = await matchdays.LoadTableSourceAsync(arranged.FeederDivisionSeasonId, CancellationToken.None);

        table!.Outcomes.Should().BeEmpty("the play-off result is outside the league table");
        table.ClubIds.Should().HaveCount(WorldRuleSet.ClubsPerDivision);

        (await matchdays.LoadDivisionMatchLoadsAsync(arranged.FeederDivisionSeasonId, CancellationToken.None)).Should().BeEmpty();
        (await matchdays.LoadDivisionStatEventsAsync(arranged.FeederDivisionSeasonId, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_division_with_a_published_play_off_round_reconciles_without_drift()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<RebuildDivisionProjections>()
            .ExecuteAsync(arranged.FeederDivisionSeasonId, apply: false, CancellationToken.None);

        result.Outcome.Should().Be(ProjectionRebuildOutcome.Reconciled);
        result.HasDrift.Should().BeFalse("the table and the season statistics are what the league's own results compute");
    }

    [Fact]
    public async Task The_money_reads_count_league_rounds_only()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<IFinanceQueries>();
        var aiFinance = scope.ServiceProvider.GetRequiredService<IAiFinanceQueries>();
        var sponsor = scope.ServiceProvider.GetRequiredService<ISponsorQueries>();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var season = await CurrentSeasonAsync(db);
        var playoffRound = WorldRuleSet.MatchdaysPerSeason + WorldRuleSet.PlayoffSemiSlot;

        (await finance.GetMoneyPositionAsync(arranged.SemiHomeClubId, CancellationToken.None))!
            .MatchdaysPlayed.Should().Be(0, "the club's league has played no league round, and the semi-final is not one");

        (await finance.GetSettledMatchdayOrdinalAsync(CancellationToken.None))
            .Should().BeLessThan(MatchdayOrdinal.Of(season.SequenceNumber, playoffRound), "wages are posted per league matchday, so the ordinal never reaches round 31");

        (await aiFinance.GetRoundsPublishedAsync(CancellationToken.None))
            .Should().BeLessThan(playoffRound);

        (await sponsor.GetClubContextAsync(arranged.SemiHomeClubId, CancellationToken.None))!
            .PublishedMatchdays.Should().Be(0, "a sponsor pays per league matchday");

        (await sponsor.CountMatchdaysPublishedSinceAsync(arranged.FeederDivisionSeasonId, DateTimeOffset.MinValue, CancellationToken.None))
            .Should().Be(0, "back pay for a late sponsor counts league rounds only");
    }

    [Fact]
    public async Task A_clubs_fixtures_cover_the_whole_season_including_a_final_hosted_in_another_league()
    {
        var arranged = await ArrangeAsync();

        await using var scope = Fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICompetitionQueries>();

        var visitor = await queries.GetClubFixturesAsync(arranged.SemiHomeClubId, CancellationToken.None);

        visitor!.DivisionId.Should().Be(arranged.FeederDivisionId, "the club's own league is still the one it finished in");
        visitor.Fixtures.Select(row => row.Id).Should().Contain([arranged.SemiFixtureId, arranged.FinalFixtureId]);
        visitor.Fixtures.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason + 2, "thirty league rounds, the semi-final and the final");
        visitor.Fixtures.Select(row => row.KickoffAt).Should().BeInAscendingOrder();
        visitor.Fixtures[^1].Id.Should().Be(arranged.FinalFixtureId, "the final is the club's last match");
        visitor.Fixtures[^1].IsHome.Should().BeFalse("the visitor plays the final at the other club's ground");

        var holder = await queries.GetClubFixturesAsync(arranged.HolderClubId, CancellationToken.None);

        holder!.Fixtures.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason + 1, "thirty league rounds and the final");
        holder.Fixtures[^1].Id.Should().Be(arranged.FinalFixtureId);
        holder.Fixtures[^1].IsHome.Should().BeTrue();

        var loser = await queries.GetClubFixturesAsync(arranged.SemiAwayClubId, CancellationToken.None);

        loser!.Fixtures.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason + 1, "thirty league rounds and the semi-final it lost");
    }

    private static FixtureEntity ScheduleFixture(Guid matchdayId, Guid homeClubId, Guid awayClubId, DateTimeOffset kickoffAt, DateTimeOffset now) =>
        FixtureEntity.Schedule(Guid.CreateVersion7(), matchdayId, homeClubId, awayClubId, kickoffAt, now);

    private static async Task<(Guid WorldId, int GameYear, string RuleSetVersion, DateTimeOffset StartsAt, Guid SeasonId, int SequenceNumber)> CurrentSeasonAsync(
        Tactical11ManagerDbContext db)
    {
        var world = await db.GameWorlds.OrderBy(candidate => candidate.CreatedAt).FirstAsync();
        var season = await db.Seasons.AsNoTracking()
            .SingleAsync(candidate => candidate.WorldId == world.Id && candidate.SequenceNumber == world.CurrentSeasonNumber);

        return (world.Id, season.GameYear, season.RuleSetVersion, season.StartsAt, season.Id, season.SequenceNumber);
    }

    /// <summary>Reads the French first league, the second league that feeds it, and two clubs of the second.</summary>
    private static async Task<(Division Parent, Division Feeder, Guid HomeClub, Guid AwayClub)> FrenchLeaguesAsync(
        Tactical11ManagerDbContext db)
    {
        var divisions = await (
            from division in db.Divisions.AsNoTracking()
            join country in db.Countries on division.CountryId equals country.Id
            where country.Code == LaunchCountries.FranceCode
            orderby division.TierNumber, division.LeagueNumber
            select division)
            .ToListAsync();

        var parent = divisions.First(division => division.TierNumber == 1);
        var feeder = divisions.First(division => division.TierNumber == 2);

        feeder.ParentDivisionId.Should().Be(parent.Id, "the second league feeds the first");

        var clubs = await (
            from entry in db.ClubSeasonEntries
            join divisionSeason in db.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.DivisionId == feeder.Id
            orderby entry.ClubId
            select entry.ClubId)
            .Take(2)
            .ToListAsync();

        return (parent, feeder, clubs[0], clubs[1]);
    }

    /// <summary>
    /// Plays a semi-final in the French second league and a final in the first, once, and stores their bracket. Later
    /// callers get the same arrangement.
    /// </summary>
    private async Task<Arrangement> ArrangeAsync()
    {
        await Gate.WaitAsync();

        try
        {
            return _arrangement ??= await BuildArrangementAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Arrangement> BuildArrangementAsync()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var now = Fixture.Clock.UtcNow;

        var current = await CurrentSeasonAsync(db);
        var (parent, feeder, semiHome, semiAway) = await FrenchLeaguesAsync(db);

        var divisionSeasons = await db.DivisionSeasons
            .Where(divisionSeason => divisionSeason.SeasonId == current.SeasonId
                && (divisionSeason.DivisionId == parent.Id || divisionSeason.DivisionId == feeder.Id))
            .ToListAsync();

        var parentSeason = divisionSeasons.Single(divisionSeason => divisionSeason.DivisionId == parent.Id);
        var feederSeason = divisionSeasons.Single(divisionSeason => divisionSeason.DivisionId == feeder.Id);

        var holder = await db.ClubSeasonEntries
            .Where(entry => entry.DivisionSeasonId == parentSeason.Id)
            .OrderBy(entry => entry.ClubId)
            .Select(entry => entry.ClubId)
            .FirstAsync();

        // After the last league round of either league, as the calendar has them, so the final is a club's last match.
        var lastLeagueKickoff = await db.Matchdays
            .Where(matchday => (matchday.DivisionSeasonId == parentSeason.Id || matchday.DivisionSeasonId == feederSeason.Id)
                && matchday.Kind == MatchdayKind.League)
            .MaxAsync(matchday => matchday.KickoffAt);

        var semiRound = Matchday.SchedulePlayoff(Guid.CreateVersion7(), feederSeason.Id, WorldRuleSet.PlayoffSemiSlot, lastLeagueKickoff.AddDays(2), now);
        var semiFixture = ScheduleFixture(semiRound.Id, semiHome, semiAway, semiRound.KickoffAt, now);
        var finalRound = Matchday.SchedulePlayoff(Guid.CreateVersion7(), parentSeason.Id, WorldRuleSet.PlayoffFinalSlot, lastLeagueKickoff.AddDays(4), now);
        var finalFixture = ScheduleFixture(finalRound.Id, holder, semiHome, finalRound.KickoffAt, now);

        db.Matchdays.AddRange(semiRound, finalRound);
        await db.SaveChangesAsync();
        db.Fixtures.AddRange(semiFixture, finalFixture);
        await db.SaveChangesAsync();

        var drawn = PlayoffBracket.Draw(
        [
            new LeagueStandings(parent.Id, 1, 1, null, Enumerable.Range(0, WorldRuleSet.ClubsPerDivision).Select(place => place == WorldRuleSet.PlayoffHolderPlace - 1 ? holder : Guid.CreateVersion7()).ToList()),
            new LeagueStandings(
                feeder.Id,
                2,
                1,
                parent.Id,
                Enumerable.Range(0, WorldRuleSet.ClubsPerDivision)
                    .Select(place => place switch
                    {
                        2 => semiHome,
                        3 => semiAway,
                        _ => Guid.CreateVersion7(),
                    })
                    .ToList()),
        ]);

        var semiTie = PlayoffTie.Draw(Guid.CreateVersion7(), current.SeasonId, feeder.CountryId, drawn.Single(pairing => pairing.Stage == PlayoffStage.SemiFinal), now);
        var finalTie = PlayoffTie.Draw(Guid.CreateVersion7(), current.SeasonId, feeder.CountryId, drawn.Single(pairing => pairing.Stage == PlayoffStage.Final), now);

        semiTie.AttachFixture(semiFixture.Id, now);
        db.PlayoffTies.AddRange(semiTie, finalTie);
        await db.SaveChangesAsync();

        // The semi-final is locked and played by the real steps, then published by the aggregates' own transitions.
        await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(semiRound.Id, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(semiRound.Id, jobId: null, CancellationToken.None);

        await using (var publishScope = Fixture.CreateScope())
        {
            var publishDb = publishScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            (await publishDb.Fixtures.SingleAsync(candidate => candidate.Id == semiFixture.Id)).Publish(now);
            (await publishDb.Matchdays.SingleAsync(candidate => candidate.Id == semiRound.Id)).Publish(now);
            await publishDb.SaveChangesAsync();
        }

        await using (var decideScope = Fixture.CreateScope())
        {
            var decideDb = decideScope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var semi = await decideDb.PlayoffTies.SingleAsync(candidate => candidate.Id == semiTie.Id);
            var final = await decideDb.PlayoffTies.SingleAsync(candidate => candidate.Id == finalTie.Id);

            semi.Decide(semiHome, PlayoffDecision.Penalties, 4, 3, "[{\"taker\":\"a\",\"scored\":true}]", now);
            final.FillAway(semiHome, feeder.Id, WorldRuleSet.PlayoffSemiHomePlace, now);
            final.AttachFixture(finalFixture.Id, now);
            await decideDb.SaveChangesAsync();
        }

        return new Arrangement(
            current.SeasonId,
            parent.Id,
            feeder.Id,
            parentSeason.Id,
            feederSeason.Id,
            semiRound.Id,
            semiFixture.Id,
            finalRound.Id,
            finalFixture.Id,
            semiHome,
            semiAway,
            holder);
    }

    private sealed record Arrangement(
        Guid SeasonId,
        Guid ParentDivisionId,
        Guid FeederDivisionId,
        Guid ParentDivisionSeasonId,
        Guid FeederDivisionSeasonId,
        Guid SemiMatchdayId,
        Guid SemiFixtureId,
        Guid FinalMatchdayId,
        Guid FinalFixtureId,
        Guid SemiHomeClubId,
        Guid SemiAwayClubId,
        Guid HolderClubId);
}
