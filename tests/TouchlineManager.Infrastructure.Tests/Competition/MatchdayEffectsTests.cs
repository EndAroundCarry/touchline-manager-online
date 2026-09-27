using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Infrastructure.Tests.Competition;

/// <summary>
/// The effects a published result has on the squad: the cards that become discipline records and
/// suspensions, the injuries that become absences, and the fixtures that serve them (`DIS-1`, `DIS-2`,
/// `DIS-4`, `DIS-5`).
/// </summary>
/// <remarks>
/// <para>
/// The card and injury events are injected rather than waited for. The engine draws cards and injuries from
/// a seed derived from the fixture, and fixture identities are UUIDv7 — they differ per run — so a test that
/// waited for a sending-off would be testing the engine's luck. Writing the event and letting the real
/// publication apply it is the same arrangement the interrupted-job test makes when it marks a fixture
/// simulating by hand: the workflow under test is the real one.
/// </para>
/// <para>
/// Every test claims two consecutive untouched rounds of one division, so "the next fixture" is unambiguous
/// and a claim by another test cannot sit between them.
/// </para>
/// </remarks>
[Collection(MatchdayCollection.Name)]
public sealed class MatchdayEffectsTests
{
    /// <summary>Initializes the tests.</summary>
    public MatchdayEffectsTests(MatchdayFixture fixture) => Fixture = fixture;

    /// <summary>Gets the seeded world the tests play rounds in.</summary>
    private MatchdayFixture Fixture { get; }

    [Fact]
    public async Task A_sending_off_and_an_injury_keep_their_players_out_of_the_next_round()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (first, second) = await ClaimConsecutiveRoundsAsync(scope);

        await LockAndResolveAsync(scope, first);

        var divisionSeasonId = await DivisionSeasonOfAsync(db, first);
        var fixture = await FirstStagedFixtureAsync(db, first);
        var matchId = fixture.MatchId!.Value;

        var (sentOff, injured) = await PickUnaffectedPlayersAsync(db, divisionSeasonId, fixture.Id, matchId);

        await InjectAsync(
            db,
            matchId,
            sentOff.ClubId,
            sentOff.ParticipantId,
            MatchEventType.RedCard,
            absenceFixtures: null);

        await InjectAsync(
            db,
            matchId,
            injured.ClubId,
            injured.ParticipantId,
            MatchEventType.Injury,
            absenceFixtures: 3);

        var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(first, CancellationToken.None);

        published.Outcome.Should().Be(PublishMatchdayOutcome.Published);

        // The sending-off is a one-fixture suspension (DIS-4), and it was not served by the match that
        // caused it: the remaining count is the whole ban.
        var suspension = await db.PlayerUnavailabilities.SingleAsync(record =>
            record.PlayerId == sentOff.ParticipantId
            && record.SourceFixtureId == fixture.Id
            && record.Type == UnavailabilityType.Suspension);

        suspension.RemainingFixtures.Should().Be(WorldRuleSet.RedCardSuspensionFixtures);
        suspension.ResolvedAt.Should().BeNull();

        var record = await db.DisciplineRecords.SingleAsync(candidate =>
            candidate.DivisionSeasonId == divisionSeasonId && candidate.PlayerId == sentOff.ParticipantId);

        record.RedCards.Should().Be(1, "DIS-4: the sending-off is on the record");
        record.YellowCards.Should().Be(0, "a straight red is not a booking");

        // The injury is an absence of exactly the fixtures the engine drew (DIS-1), in the band that
        // describes it.
        var injury = await db.PlayerUnavailabilities.SingleAsync(candidate =>
            candidate.PlayerId == injured.ParticipantId
            && candidate.SourceFixtureId == fixture.Id
            && candidate.Type == UnavailabilityType.Injury);

        injury.RemainingFixtures.Should().Be(3, "the absence is measured in fixtures, not days (TRN-12)");
        injury.Severity.Should().Be(WorldRuleSet.InjurySeverityFor(3));
        injury.ResolvedAt.Should().BeNull();

        // The next round freezes both clubs, and neither player can be named in it (DIS-5).
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(second, CancellationToken.None);

        var named = await PlayersNamedInRoundAsync(db, second);

        named.Should().NotContain(sentOff.ParticipantId, "DIS-5: a suspended player is not selected");
        named.Should().NotContain(injured.ParticipantId, "TRN-12: an injured player is not selected");

        await ResolveAndPublishAsync(scope, second);

        await using var readScope = Fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var servedSuspension = await readDb.PlayerUnavailabilities.SingleAsync(candidate =>
            candidate.PlayerId == sentOff.ParticipantId
            && candidate.SourceFixtureId == fixture.Id
            && candidate.Type == UnavailabilityType.Suspension);

        servedSuspension.RemainingFixtures.Should().Be(0);
        servedSuspension.ResolvedAt.Should().NotBeNull(
            "DIS-5: a one-fixture suspension is served by the next fixture the club plays");

        var servedInjury = await readDb.PlayerUnavailabilities.SingleAsync(candidate =>
            candidate.PlayerId == injured.ParticipantId
            && candidate.SourceFixtureId == fixture.Id
            && candidate.Type == UnavailabilityType.Injury);

        servedInjury.RemainingFixtures.Should().Be(2, "exactly one fixture was served, and only one");
        servedInjury.ResolvedAt.Should().BeNull("a three-fixture injury is not over after one fixture");
    }

    [Fact]
    public async Task A_second_yellow_is_recorded_as_a_booking_and_a_sending_off()
    {
        await using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var (round, _) = await ClaimConsecutiveRoundsAsync(scope);

        await LockAndResolveAsync(scope, round);

        var divisionSeasonId = await DivisionSeasonOfAsync(db, round);
        var fixture = await FirstStagedFixtureAsync(db, round);
        var matchId = fixture.MatchId!.Value;

        var (booked, _) = await PickUnaffectedPlayersAsync(db, divisionSeasonId, fixture.Id, matchId);

        await InjectAsync(
            db,
            matchId,
            booked.ClubId,
            booked.ParticipantId,
            MatchEventType.SecondYellowCard,
            absenceFixtures: null);

        await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(round, CancellationToken.None);

        var record = await db.DisciplineRecords.SingleAsync(candidate =>
            candidate.DivisionSeasonId == divisionSeasonId && candidate.PlayerId == booked.ParticipantId);

        record.YellowCards.Should().Be(1, "the second booking was still a booking");
        record.RedCards.Should().Be(1, "and it ended the player's match (DIS-4)");

        var suspension = await db.PlayerUnavailabilities.SingleAsync(candidate =>
            candidate.PlayerId == booked.ParticipantId
            && candidate.SourceFixtureId == fixture.Id
            && candidate.Type == UnavailabilityType.Suspension);

        suspension.RemainingFixtures.Should().Be(WorldRuleSet.RedCardSuspensionFixtures);
    }

    /// <summary>Locks and resolves a round, leaving its fixtures staged and its snapshots frozen.</summary>
    private static async Task LockAndResolveAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<LockMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
    }

    /// <summary>Resolves and publishes a locked round.</summary>
    private static async Task ResolveAndPublishAsync(AsyncServiceScope scope, Guid matchdayId)
    {
        await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
            .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
            .ExecuteAsync(matchdayId, CancellationToken.None);
    }

    /// <summary>Injects one card or injury event into a simulated match's stream.</summary>
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

    /// <summary>Finds two untouched rounds that immediately follow one another in the same division.</summary>
    private static async Task<(Guid First, Guid Second)> ClaimConsecutiveRoundsAsync(AsyncServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var divisionSeasonId = await db.DivisionSeasons
            .OrderBy(divisionSeason => divisionSeason.CreatedAt)
            .Select(divisionSeason => divisionSeason.Id)
            .FirstAsync();

        var untouched = await db.Matchdays
            .Where(matchday => matchday.DivisionSeasonId == divisionSeasonId
                && matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => new { matchday.Id, matchday.RoundNumber })
            .ToListAsync();

        for (var index = 0; index + 1 < untouched.Count; index++)
        {
            if (untouched[index + 1].RoundNumber == untouched[index].RoundNumber + 1)
            {
                return (untouched[index].Id, untouched[index + 1].Id);
            }
        }

        throw new InvalidOperationException("The seeded world has no two consecutive untouched rounds left.");
    }

    /// <summary>
    /// Picks two outfield players who enter the round with a clean slate: no card or injury in the
    /// simulated match, and no accumulation from an earlier round.
    /// </summary>
    /// <remarks>
    /// The clean slate is what makes the counts assertable. A player who was booked in a round another test
    /// published already carries a record, and the effect under test would be one booking on top of
    /// somebody else's, which is a real behaviour but not a legible expectation.
    /// </remarks>
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

    /// <summary>Reads every player named in any of a round's frozen sides.</summary>
    private static async Task<List<Guid>> PlayersNamedInRoundAsync(TouchlineManagerDbContext db, Guid matchdayId)
    {
        var fixtureIds = await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId)
            .Select(fixture => fixture.Id)
            .ToListAsync();

        var snapshots = await db.InputSnapshots
            .Where(snapshot => fixtureIds.Contains(snapshot.FixtureId))
            .ToListAsync();

        return
        [
            .. snapshots
                .Select(snapshot => MatchSnapshotDocument.Read(snapshot.SnapshotJson).Input)
                .SelectMany(input => input.Home.Squad.Concat(input.Away.Squad))
                .Select(participant => participant.ParticipantId)
                .Distinct(),
        ];
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
