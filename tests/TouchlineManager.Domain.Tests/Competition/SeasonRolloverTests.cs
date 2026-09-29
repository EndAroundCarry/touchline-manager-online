using FluentAssertions;
using TouchlineManager.Domain.Competition;

namespace TouchlineManager.Domain.Tests.Competition;

/// <summary>
/// The rollover state machine's checkpoints and its idempotence (`PR-4`, master plan §7.5, ADR-0031).
/// </summary>
/// <remarks>
/// The repeats matter as much as the transitions: the rollover runs from a durable job that the queue may
/// deliver more than once, so every step has to be safe to run again from the checkpoint it reached.
/// </remarks>
public sealed class SeasonRolloverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_rollover_starts_at_the_first_checkpoint()
    {
        var rollover = Start();

        rollover.Phase.Should().Be(SeasonRolloverPhase.Started);
        rollover.NextSeasonId.Should().BeNull();
        rollover.CompletedAt.Should().BeNull();
        rollover.FailureReason.Should().BeNull();
    }

    [Fact]
    public void The_phases_run_in_order_to_completion()
    {
        var rollover = Start();
        var nextSeasonId = Guid.CreateVersion7();

        rollover.Freeze(Now.AddMinutes(1));
        rollover.Phase.Should().Be(SeasonRolloverPhase.Frozen);

        rollover.Finalize(Now.AddMinutes(2));
        rollover.Phase.Should().Be(SeasonRolloverPhase.Finalized);

        rollover.SettleSquads(Now.AddMinutes(2.5));
        rollover.Phase.Should().Be(SeasonRolloverPhase.Squads);

        rollover.Move(nextSeasonId, Now.AddMinutes(3));
        rollover.Phase.Should().Be(SeasonRolloverPhase.Moved);
        rollover.NextSeasonId.Should().Be(nextSeasonId);

        rollover.Complete(Now.AddMinutes(4));
        rollover.Phase.Should().Be(SeasonRolloverPhase.Completed);
        rollover.CompletedAt.Should().Be(Now.AddMinutes(4));
    }

    [Fact]
    public void Repeating_a_checkpoint_is_a_no_op()
    {
        // The queue is at-least-once, so a redelivered job re-runs a step it has already committed.
        var rollover = Start();
        var versionAfterFreeze = 0L;

        rollover.Freeze(Now.AddMinutes(1));
        versionAfterFreeze = rollover.Version;

        rollover.Freeze(Now.AddMinutes(2));
        rollover.Finalize(Now.AddMinutes(3));
        rollover.Finalize(Now.AddMinutes(4));
        var nextSeasonId = Guid.CreateVersion7();
        rollover.SettleSquads(Now.AddMinutes(4.5));
        rollover.SettleSquads(Now.AddMinutes(4.75));
        rollover.Move(nextSeasonId, Now.AddMinutes(5));
        rollover.Move(Guid.CreateVersion7(), Now.AddMinutes(6));
        rollover.Complete(Now.AddMinutes(7));
        rollover.Complete(Now.AddMinutes(8));

        rollover.Phase.Should().Be(SeasonRolloverPhase.Completed);
        rollover.NextSeasonId.Should().Be(nextSeasonId, "the first move's season is the one that stands");
        rollover.Version.Should().BeGreaterThan(versionAfterFreeze);
    }

    [Fact]
    public void Skipping_a_checkpoint_is_a_programming_error()
    {
        var rollover = Start();

        var finalizeFromStart = () => rollover.Finalize(Now);
        var settleSquadsFromStart = () => rollover.SettleSquads(Now);
        var moveFromStart = () => rollover.Move(Guid.CreateVersion7(), Now);
        var completeFromStart = () => rollover.Complete(Now);

        finalizeFromStart.Should().Throw<InvalidOperationException>();
        settleSquadsFromStart.Should().Throw<InvalidOperationException>();
        moveFromStart.Should().Throw<InvalidOperationException>();
        completeFromStart.Should().Throw<InvalidOperationException>();

        // The move phase runs after contracts are settled, so moving straight from finalized is refused too.
        rollover.Freeze(Now);
        rollover.Finalize(Now);

        var moveBeforeSquads = () => rollover.Move(Guid.CreateVersion7(), Now);
        moveBeforeSquads.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_failed_rollover_must_be_retried_before_it_can_advance()
    {
        var rollover = Start();
        rollover.Freeze(Now.AddMinutes(1));
        rollover.Fail("the closing season still had an unplayed matchday", Now.AddMinutes(2));

        rollover.Phase.Should().Be(SeasonRolloverPhase.Failed);
        rollover.FailureReason.Should().Be("the closing season still had an unplayed matchday");
        SeasonRolloverPhaseRules.IsTerminal(rollover.Phase).Should().BeTrue();

        var advanceWhileFailed = () => rollover.Finalize(Now.AddMinutes(3));
        advanceWhileFailed.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Retrying_a_failed_rollover_starts_it_again_from_the_top()
    {
        // Every phase is idempotent, so restarting from Started re-does nothing it already did and reuses
        // this row rather than creating a second rollover for the same season.
        var rollover = Start();
        rollover.Freeze(Now.AddMinutes(1));
        rollover.Finalize(Now.AddMinutes(2));
        rollover.Fail("the next season's schedule did not reconcile", Now.AddMinutes(3));

        rollover.Retry(Now.AddMinutes(4));

        rollover.Phase.Should().Be(SeasonRolloverPhase.Started);
        rollover.FailureReason.Should().BeNull();
        rollover.NextSeasonId.Should().BeNull();
        rollover.CompletedAt.Should().BeNull();

        rollover.Freeze(Now.AddMinutes(5));
        rollover.Finalize(Now.AddMinutes(6));
        rollover.SettleSquads(Now.AddMinutes(6.5));
        rollover.Move(Guid.CreateVersion7(), Now.AddMinutes(7));
        rollover.Complete(Now.AddMinutes(8));

        rollover.Phase.Should().Be(SeasonRolloverPhase.Completed);
    }

    [Fact]
    public void Retrying_a_rollover_that_did_not_fail_is_a_programming_error()
    {
        var started = Start();
        var completed = Start();
        completed.Freeze(Now);
        completed.Finalize(Now);
        completed.SettleSquads(Now);
        completed.Move(Guid.CreateVersion7(), Now);
        completed.Complete(Now);

        var fromStarted = () => started.Retry(Now);
        var fromCompleted = () => completed.Retry(Now);

        fromStarted.Should().Throw<InvalidOperationException>();
        fromCompleted.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_completed_rollover_cannot_fail()
    {
        var rollover = Completed();

        var act = () => rollover.Fail("a late error", Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Failing_without_a_reason_is_a_programming_error()
    {
        var rollover = Start();

        var act = () => rollover.Fail("   ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_phase_round_trips_through_its_code()
    {
        SeasonRolloverPhase.Started.ToCode().Should().Be("started");
        SeasonRolloverPhase.Frozen.ToCode().Should().Be("frozen");
        SeasonRolloverPhase.Finalized.ToCode().Should().Be("finalized");
        SeasonRolloverPhase.Squads.ToCode().Should().Be("squads");
        SeasonRolloverPhase.Moved.ToCode().Should().Be("moved");
        SeasonRolloverPhase.Completed.ToCode().Should().Be("completed");
        SeasonRolloverPhase.Failed.ToCode().Should().Be("failed");
        SeasonRolloverPhaseRules.FromCode("moved").Should().Be(SeasonRolloverPhase.Moved);

        var act = () => SeasonRolloverPhaseRules.FromCode("paused");
        act.Should().Throw<ArgumentOutOfRangeException>();

        SeasonRolloverPhases.MaxCodeLength.Should().Be("finalized".Length);
    }

    [Fact]
    public void The_phase_order_is_the_checkpoint_order()
    {
        ((int)SeasonRolloverPhase.Started).Should().BeLessThan((int)SeasonRolloverPhase.Frozen);
        ((int)SeasonRolloverPhase.Frozen).Should().BeLessThan((int)SeasonRolloverPhase.Finalized);
        ((int)SeasonRolloverPhase.Finalized).Should().BeLessThan((int)SeasonRolloverPhase.Squads);
        ((int)SeasonRolloverPhase.Squads).Should().BeLessThan((int)SeasonRolloverPhase.Moved);
        ((int)SeasonRolloverPhase.Moved).Should().BeLessThan((int)SeasonRolloverPhase.Completed);
    }

    private static SeasonRollover Start() =>
        SeasonRollover.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static SeasonRollover Completed()
    {
        var rollover = Start();
        rollover.Freeze(Now);
        rollover.Finalize(Now);
        rollover.SettleSquads(Now);
        rollover.Move(Guid.CreateVersion7(), Now);
        rollover.Complete(Now);

        return rollover;
    }
}
