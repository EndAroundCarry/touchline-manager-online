using FluentAssertions;
using TouchlineManager.Domain.Match;

namespace TouchlineManager.Domain.Tests.Match;

/// <summary>
/// The match module's aggregates: the frozen snapshot, the result, the attempt, and the event
/// (`MAT-1`, `MAT-9`, master plan §6.6).
/// </summary>
/// <remarks>
/// These types are written once and never changed, and the tests say so: the snapshot has no mutator to
/// call, a result cannot be recorded with a score that contradicts its own timing, and an event's sequence
/// position is a positive integer because it is the match's total order.
/// </remarks>
public sealed class MatchAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_snapshot_is_frozen_with_the_seed_it_was_derived_from()
    {
        var fixtureId = Guid.CreateVersion7();

        var snapshot = InputSnapshot.Freeze(
            Guid.CreateVersion7(),
            fixtureId,
            "engine-v1",
            "engine-rules-v1",
            12_345UL,
            "commitment",
            """{"schema":"match-snapshot-v1"}""",
            "hash",
            Now);

        snapshot.FixtureId.Should().Be(fixtureId);
        snapshot.Seed.Should().Be(12_345UL);
        snapshot.SnapshotHash.Should().Be("hash");
        snapshot.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void A_snapshot_refuses_to_exist_without_its_fixture_or_its_hashes()
    {
        var act = () => InputSnapshot.Freeze(
            Guid.CreateVersion7(),
            Guid.Empty,
            "engine-v1",
            "engine-rules-v1",
            1UL,
            "commitment",
            "{}",
            "hash",
            Now);

        act.Should().Throw<ArgumentException>("a snapshot belongs to a fixture (MAT-1)");

        var missingHash = () => InputSnapshot.Freeze(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "engine-v1",
            "engine-rules-v1",
            1UL,
            "commitment",
            "{}",
            " ",
            Now);

        missingHash.Should().Throw<ArgumentException>("the hash is what a stored result is re-derived against (MAT-9)");
    }

    [Fact]
    public void A_snapshot_cannot_be_changed_after_it_is_frozen()
    {
        var settable = typeof(InputSnapshot)
            .GetProperties()
            .Where(property => property.SetMethod is not null && property.SetMethod.IsPublic)
            .Select(property => property.Name)
            .ToList();

        settable.Should().BeEmpty(
            "immutability after the fixture locks is a property of the type, not a promise (MAT-1)");
    }

    [Fact]
    public void A_match_records_both_hashes_and_its_attempt()
    {
        var attemptId = Guid.CreateVersion7();

        var match = SimulatedMatch.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "engine-v1",
            "engine-rules-v1",
            "commitment",
            homeGoals: 2,
            awayGoals: 1,
            """{"schema":"match-statistics-v1"}""",
            "input-hash",
            "output-hash",
            attemptId,
            Now,
            Now.AddSeconds(1));

        match.HomeGoals.Should().Be(2);
        match.AwayGoals.Should().Be(1);
        match.InputHash.Should().Be("input-hash");
        match.OutputHash.Should().Be("output-hash");
        match.SimulationAttemptId.Should().Be(attemptId);
    }

    [Fact]
    public void A_match_refuses_a_negative_score_or_an_impossible_timing()
    {
        var negative = () => SimulatedMatch.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "engine-v1",
            "engine-rules-v1",
            "commitment",
            -1,
            0,
            "{}",
            "in",
            "out",
            Guid.CreateVersion7(),
            Now,
            Now);

        negative.Should().Throw<ArgumentOutOfRangeException>();

        var backwards = () => SimulatedMatch.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "engine-v1",
            "engine-rules-v1",
            "commitment",
            0,
            0,
            "{}",
            "in",
            "out",
            Guid.CreateVersion7(),
            Now,
            Now.AddSeconds(-1));

        backwards.Should().Throw<ArgumentOutOfRangeException>("a match cannot finish before it started");
    }

    [Fact]
    public void A_successful_attempt_carries_its_hashes_and_a_failed_one_carries_its_reason()
    {
        var fixtureId = Guid.CreateVersion7();

        var succeeded = SimulationAttempt.Succeeded(
            Guid.CreateVersion7(),
            fixtureId,
            jobId: null,
            attemptNumber: 1,
            "engine-v1",
            "input-hash",
            "output-hash",
            Now,
            Now.AddMilliseconds(40));

        succeeded.Status.Should().Be(SimulationAttemptStatus.Succeeded);
        succeeded.OutputHash.Should().Be("output-hash");
        succeeded.ErrorCategory.Should().BeNull();
        succeeded.DurationMilliseconds.Should().Be(40);

        var failed = SimulationAttempt.Failed(
            Guid.CreateVersion7(),
            fixtureId,
            jobId: null,
            attemptNumber: 2,
            "engine-v1",
            "invalid-input",
            "the lineup names ten players",
            Now,
            Now.AddMilliseconds(5));

        failed.Status.Should().Be(SimulationAttemptStatus.Failed);
        failed.OutputHash.Should().BeNull("a failed attempt produced no result to publish");
        failed.ErrorCategory.Should().Be("invalid-input");
        failed.ErrorMessage.Should().Contain("ten players");
    }

    [Fact]
    public void An_attempt_number_is_positive()
    {
        var act = () => SimulationAttempt.Succeeded(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            jobId: null,
            attemptNumber: 0,
            "engine-v1",
            "in",
            "out",
            Now,
            Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_event_records_where_and_when_it_happened()
    {
        var matchId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();
        var scorer = Guid.CreateVersion7();

        var goal = MatchEvent.Record(
            Guid.CreateVersion7(),
            matchId,
            sequence: 7,
            minute: 23,
            stoppageMinute: 0,
            clubId,
            MatchEventType.Goal,
            scorer,
            zone: MatchShotZone.InsideLeft,
            qualityBasisPoints: 1_800);

        goal.Sequence.Should().Be(7);
        goal.Minute.Should().Be(23);
        goal.Type.IsGoal().Should().BeTrue();
        goal.ParticipantId.Should().Be(scorer);
        goal.Zone.Should().Be(MatchShotZone.InsideLeft);
    }

    [Fact]
    public void An_event_refuses_an_impossible_sequence_or_quality()
    {
        var noSequence = () => MatchEvent.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            0,
            1,
            0,
            Guid.CreateVersion7(),
            MatchEventType.Foul);

        noSequence.Should().Throw<ArgumentOutOfRangeException>("the sequence is the match's total order");

        var badQuality = () => MatchEvent.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            1,
            0,
            Guid.CreateVersion7(),
            MatchEventType.Goal,
            qualityBasisPoints: 10_001);

        badQuality.Should().Throw<ArgumentOutOfRangeException>("a shot quality is a probability");
    }

    [Fact]
    public void A_second_yellow_is_a_red_card_and_not_a_second_yellow()
    {
        MatchEventType.YellowCard.IsRedCard().Should().BeFalse();
        MatchEventType.RedCard.IsRedCard().Should().BeTrue();
        MatchEventType.SecondYellowCard
            .IsRedCard()
            .Should()
            .BeTrue("the card that ends a player's match is the sending-off (TBL-8)");

        MatchEventType.Goal.IsGoal().Should().BeTrue();
        MatchEventType.PenaltyGoal.IsGoal().Should().BeTrue();
        MatchEventType.PenaltyMissed.IsGoal().Should().BeFalse();
    }

    [Fact]
    public void Every_stored_code_round_trips()
    {
        foreach (var type in MatchEventTypes.All)
        {
            MatchEventTypes.FromCode(type.ToCode()).Should().Be(type);
        }

        foreach (var zone in Enum.GetValues<MatchShotZone>())
        {
            MatchEventTypes.ZoneFromCode(zone.ToCode()).Should().Be(zone);
        }

        foreach (var cause in Enum.GetValues<MatchSubstitutionCause>())
        {
            MatchEventTypes.CauseFromCode(cause.ToCode()).Should().Be(cause);
        }
    }
}
