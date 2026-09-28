using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// A division's discipline as its own read: the cards the season has shown and the suspensions they cost
/// (`DIS-2`, `DIS-4`, `DIS-5`).
/// </summary>
/// <remarks>
/// <para>
/// The card and injury events are injected rather than waited for, exactly as the effects suite does: the
/// engine draws cards from a fixture-derived seed and fixture identities are UUIDv7, so a test that waited
/// for a sending-off would be testing the engine's luck. Writing the event and letting the real publication
/// apply it is the same arrangement, and the workflow under test is the real one.
/// </para>
/// <para>
/// The read is public game data, so it is read through the query port without a manager; what it asserts is
/// that the page and the side a manager may actually name cannot disagree about who is suspended.
/// </para>
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class DivisionDisciplineTests
{
    /// <summary>Initializes the tests.</summary>
    public DivisionDisciplineTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_division_discipline_reports_the_cards_and_the_suspension_they_cost()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var round = await ClaimRoundAsync(scope);

        await LockAndResolveAsync(scope, round);

        var divisionSeasonId = await DivisionSeasonOfAsync(db, round);
        var fixture = await FirstStagedFixtureAsync(db, round);
        var matchId = fixture.MatchId!.Value;

        var (sentOff, booked) = await PickUnaffectedPlayersAsync(db, divisionSeasonId, fixture.Id, matchId);

        await InjectAsync(db, matchId, sentOff.ClubId, sentOff.ParticipantId, MatchEventType.RedCard, absenceFixtures: null);
        await InjectAsync(db, matchId, booked.ClubId, booked.ParticipantId, MatchEventType.YellowCard, absenceFixtures: null);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(round, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        var divisionId = await db.DivisionSeasons
            .Where(candidate => candidate.Id == divisionSeasonId)
            .Select(candidate => candidate.DivisionId)
            .SingleAsync();

        await using var readScope = Fixture.CreateScope();
        var discipline = await readScope.ServiceProvider.GetRequiredService<ICompetitionQueries>()
            .GetDivisionDisciplineAsync(divisionId, CancellationToken.None);

        discipline.Should().NotBeNull();
        discipline!.Rows.Should().NotBeEmpty("a division whose round has been played has booked somebody");

        var redRow = discipline.Rows.Single(row => row.PlayerId == sentOff.ParticipantId);

        redRow.RedCards.Should().Be(1, "DIS-4: the sending-off is on the record");
        redRow.YellowCards.Should().Be(0, "a straight red is not a booking");
        redRow.SuspensionFixturesRemaining.Should().Be(
            WorldRuleSet.RedCardSuspensionFixtures,
            "DIS-5: the page shows the fixtures the player still misses");

        var yellowRow = discipline.Rows.Single(row => row.PlayerId == booked.ParticipantId);

        yellowRow.YellowCards.Should().Be(1, "DIS-2: the booking is on the record");
        yellowRow.RedCards.Should().Be(0);
        yellowRow.SuspensionFixturesRemaining.Should().Be(0, "one booking is not yet a ban");

        // The order is the server's — most sendings-off, then most bookings, then name — so the screen
        // sorts nothing and two reads never disagree (TBL-12's principle).
        for (var index = 1; index < discipline.Rows.Count; index++)
        {
            var previous = discipline.Rows[index - 1];
            var current = discipline.Rows[index];

            var ordered = previous.RedCards > current.RedCards
                || (previous.RedCards == current.RedCards && previous.YellowCards > current.YellowCards)
                || (previous.RedCards == current.RedCards
                    && previous.YellowCards == current.YellowCards
                    && string.CompareOrdinal(previous.PlayerName, current.PlayerName) <= 0);

            ordered.Should().BeTrue($"row {index} is ranked after the one before it");
        }
    }

    [Fact]
    public async Task An_unknown_division_has_no_discipline()
    {
        await using var scope = Fixture.CreateScope();

        var discipline = await scope.ServiceProvider.GetRequiredService<ICompetitionQueries>()
            .GetDivisionDisciplineAsync(Guid.CreateVersion7(), CancellationToken.None);

        discipline.Should().BeNull();
    }

    /// <summary>Locks and resolves a round, leaving its fixtures staged and its snapshots frozen.</summary>
    private static async Task LockAndResolveAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
    }

    /// <summary>Injects one card event into a simulated match's stream.</summary>
    private static async Task InjectAsync(
        TouchlineManagerDbContext db,
        Guid matchId,
        Guid clubId,
        Guid playerId,
        MatchEventType type,
        int? absenceFixtures)
    {
        var sequence = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId)
            .MaxAsync(matchEvent => matchEvent.Sequence) + 1;

        db.MatchEvents.Add(MatchEvent.Record(
            Guid.CreateVersion7(),
            matchId,
            sequence,
            minute: 90,
            stoppageMinute: 0,
            clubId,
            type,
            participantId: playerId,
            absenceFixtures: absenceFixtures));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Claims one untouched round of a division.</summary>
    private static async Task<Guid> ClaimRoundAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var divisionSeasonId = await db.DivisionSeasons
            .OrderBy(divisionSeason => divisionSeason.CreatedAt)
            .Select(divisionSeason => divisionSeason.Id)
            .FirstAsync();

        return await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    /// <summary>
    /// Picks two outfield players who enter the round with a clean slate, so the counts assertable are the
    /// ones this test caused rather than somebody else's accumulation.
    /// </summary>
    private static async Task<(MatchParticipantV1 First, MatchParticipantV1 Second)> PickUnaffectedPlayersAsync(
        TouchlineManagerDbContext db,
        Guid divisionSeasonId,
        Guid fixtureId,
        Guid matchId)
    {
        var snapshot = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == fixtureId);
        var input = MatchSnapshotDocument.Read(snapshot.SnapshotJson).Input;

        var named = input.Home.Squad.Concat(input.Away.Squad).ToList();

        var affected = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId
                && matchEvent.ParticipantId != null
                && (matchEvent.Type == MatchEventType.YellowCard
                    || matchEvent.Type == MatchEventType.SecondYellowCard
                    || matchEvent.Type == MatchEventType.RedCard
                    || matchEvent.Type == MatchEventType.Injury))
            .Select(matchEvent => matchEvent.ParticipantId!.Value)
            .Distinct()
            .ToListAsync();

        var recorded = await db.DisciplineRecords
            .Where(record => record.DivisionSeasonId == divisionSeasonId)
            .Select(record => record.PlayerId)
            .ToListAsync();

        var candidates = named
            .Where(participant => !participant.IsGoalkeeper
                && !affected.Contains(participant.ParticipantId)
                && !recorded.Contains(participant.ParticipantId))
            .OrderBy(participant => participant.ParticipantId)
            .ToList();

        candidates.Should().HaveCountGreaterThanOrEqualTo(2, "a match leaves most of a squad untouched");

        return (candidates[0], candidates[1]);
    }

    private static async Task<Fixture> FirstStagedFixtureAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId && fixture.Status == FixtureStatus.Staged)
            .OrderBy(fixture => fixture.Id)
            .FirstAsync();

    private static async Task<Guid> DivisionSeasonOfAsync(TouchlineManagerDbContext db, Guid matchdayId) =>
        await db.Matchdays
            .Where(matchday => matchday.Id == matchdayId)
            .Select(matchday => matchday.DivisionSeasonId)
            .SingleAsync();
}
