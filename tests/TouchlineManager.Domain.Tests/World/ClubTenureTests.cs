using FluentAssertions;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The tenure lifecycle (`OCC-2`, `OCC-4`, `OCC-5`, `OCC-8`, `OCC-9`). The whole of the ownership
/// model is these transitions.
/// </summary>
public sealed class ClubTenureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_tenure_is_open_active_and_counts_towards_occupancy()
    {
        var tenure = Start();

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Active);
        tenure.IsOpen.Should().BeTrue();
        tenure.OccupiesCapacity.Should().BeTrue();
        tenure.EndedAt.Should().BeNull();
        tenure.EndReason.Should().BeNull();
        tenure.LastActiveAt.Should().Be(Now);
    }

    [Fact]
    public void Opening_a_tenure_without_an_idempotency_key_is_a_programming_error()
    {
        // The key is what makes a retried takeover a no-op rather than a second club (CONC-3).
        var act = () => ClubTenure.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "  ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_inactive_tenure_still_occupies_the_club()
    {
        // OCC-8: counting only active tenures would report a tier as having room while a manager who
        // may still return holds a club in it.
        var tenure = Start();

        tenure.MarkInactive(Now.AddDays(14));

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Inactive);
        tenure.IsOpen.Should().BeTrue("the manager may resume by logging in");
        tenure.OccupiesCapacity.Should().BeTrue();
    }

    [Fact]
    public void Returning_from_inactivity_restores_control_and_restarts_the_inactivity_ladder()
    {
        var tenure = Start();
        tenure.MarkInactive(Now.AddDays(14));

        tenure.Resume(Now.AddDays(15));

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Active);
        tenure.LastActiveAt.Should().Be(Now.AddDays(15));
    }

    [Fact]
    public void Recording_activity_restarts_the_ladder_without_changing_control()
    {
        var tenure = Start();

        tenure.RecordActivity(Now.AddDays(3));

        tenure.LastActiveAt.Should().Be(Now.AddDays(3));
        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Active);
    }

    [Fact]
    public void Closing_a_tenure_ends_it_and_releases_the_club()
    {
        var tenure = Start();

        tenure.Close(ClubTenureEndReasons.Resigned, Now.AddDays(1));

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
        tenure.IsOpen.Should().BeFalse();
        tenure.OccupiesCapacity.Should().BeFalse();
        tenure.EndedAt.Should().Be(Now.AddDays(1));
        tenure.EndReason.Should().Be(ClubTenureEndReasons.Resigned);
    }

    [Fact]
    public void Closing_twice_keeps_the_first_end_instant()
    {
        var tenure = Start();
        tenure.Close(ClubTenureEndReasons.Resigned, Now.AddDays(1));

        tenure.Close(ClubTenureEndReasons.AdministratorClosed, Now.AddDays(5));

        tenure.EndedAt.Should().Be(Now.AddDays(1));
        tenure.EndReason.Should().Be(ClubTenureEndReasons.Resigned);
    }

    [Fact]
    public void An_unknown_end_reason_is_a_programming_error()
    {
        var tenure = Start();

        var act = () => tenure.Close("got_bored", Now.AddDays(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_closed_tenure_cannot_become_inactive_or_be_resumed()
    {
        var tenure = Start();
        tenure.Close(ClubTenureEndReasons.InactivityClosed, Now.AddDays(21));

        var inactive = () => tenure.MarkInactive(Now.AddDays(22));
        var resume = () => tenure.Resume(Now.AddDays(22));

        inactive.Should().Throw<InvalidOperationException>();
        resume.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Recording_activity_on_a_closed_tenure_is_ignored()
    {
        // A late request must not resurrect a tenure that inactivity already closed.
        var tenure = Start();
        tenure.Close(ClubTenureEndReasons.InactivityClosed, Now.AddDays(21));

        tenure.RecordActivity(Now.AddDays(22));

        tenure.LastActiveAt.Should().Be(Now);
    }

    [Fact]
    public void Every_end_reason_is_recognised()
    {
        ClubTenureEndReasons.IsKnown(ClubTenureEndReasons.Resigned).Should().BeTrue();
        ClubTenureEndReasons.IsKnown(ClubTenureEndReasons.InactivityClosed).Should().BeTrue();
        ClubTenureEndReasons.IsKnown(ClubTenureEndReasons.AdministratorClosed).Should().BeTrue();
        ClubTenureEndReasons.IsKnown("retired").Should().BeFalse();
    }

    [Fact]
    public void Every_control_status_round_trips_through_its_code()
    {
        ClubTenureControlStatus.Active.ToCode().Should().Be("active");
        ClubTenureControlStatus.Inactive.ToCode().Should().Be("inactive");
        ClubTenureControlStatus.Closed.ToCode().Should().Be("closed");
        ClubTenureControlStatusRules.FromCode("inactive").Should().Be(ClubTenureControlStatus.Inactive);

        var act = () => ClubTenureControlStatusRules.FromCode("abandoned");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ClubTenure Start() =>
        ClubTenure.Start(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "claim-key-1",
            Now);
}
