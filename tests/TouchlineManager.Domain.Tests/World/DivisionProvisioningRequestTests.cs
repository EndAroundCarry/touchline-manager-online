using FluentAssertions;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The provisioning request lifecycle (`PYR-2`, `PYR-3`, `PYR-10`).
/// </summary>
public sealed class DivisionProvisioningRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_request_is_pending_and_not_claimable_yet()
    {
        var request = Request();

        request.Status.Should().Be(ProvisioningRequestStatus.Requested);
        request.IsPending.Should().BeTrue();
        request.StartedAt.Should().BeNull();
        request.CompletedAt.Should().BeNull();
        request.TargetTier.Should().Be(2);
    }

    [Fact]
    public void Requesting_tier_one_is_a_programming_error_because_it_is_seeded()
    {
        // PYR-2: only a tier above the first is ever provisioned.
        var act = () => DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            Guid.CreateVersion7(),
            "seed",
            Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_request_cannot_be_started_from_a_terminal_state()
    {
        var request = Request();
        request.Complete(Now.AddMinutes(1));

        var act = () => request.Start(Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Completing_a_request_that_already_failed_is_ignored_rather_than_papering_over_it()
    {
        // A failed run must never be reported as a completed one: the tier would become claimable with
        // no valid squads or table behind it (PYR-8).
        var request = Request();
        request.Fail("the generator ran out of name combinations", Now.AddMinutes(1));

        request.Complete(Now.AddMinutes(2));

        request.Status.Should().Be(ProvisioningRequestStatus.Failed);
        request.FailureDiagnostics.Should().Be("the generator ran out of name combinations");
    }

    [Fact]
    public void Retrying_a_failed_request_reuses_the_row_and_its_seed()
    {
        // unique (country_id, target_tier) means a second request for the same tier cannot exist, so a
        // retry must reuse this one — and must attempt the same world, not a different one (PYR-14).
        var request = Request();
        var seed = request.GenerationSeed;

        request.Start(Now.AddMinutes(1));
        request.Fail("validation found a squad with one goalkeeper", Now.AddMinutes(2));

        request.Retry(Now.AddMinutes(3));

        request.Status.Should().Be(ProvisioningRequestStatus.Requested);
        request.GenerationSeed.Should().Be(seed);
        request.FailureDiagnostics.Should().BeNull();
        request.StartedAt.Should().BeNull();
        request.CompletedAt.Should().BeNull();
        request.IsPending.Should().BeTrue();

        // And the retry can run to completion.
        request.Start(Now.AddMinutes(4));
        request.Complete(Now.AddMinutes(5));

        request.Status.Should().Be(ProvisioningRequestStatus.Completed);
    }

    [Fact]
    public void Retrying_a_request_that_did_not_fail_is_a_programming_error()
    {
        var requested = Request();
        var completed = Request();
        completed.Complete(Now);

        var fromRequested = () => requested.Retry(Now);
        var fromCompleted = () => completed.Retry(Now);

        fromRequested.Should().Throw<InvalidOperationException>();
        fromCompleted.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_failed_request_is_terminal_and_keeps_its_diagnostics()
    {
        // Retrying a failed provisioning is an operator decision (Stage 11), not an automatic one, so
        // the record stays visible rather than silently disappearing.
        var request = Request();

        request.Fail("validation found a squad with one goalkeeper", Now.AddMinutes(3));

        request.Status.Should().Be(ProvisioningRequestStatus.Failed);
        request.FailureDiagnostics.Should().Be("validation found a squad with one goalkeeper");
        request.IsPending.Should().BeFalse();
        ProvisioningRequestStatusRules.IsTerminal(request.Status).Should().BeTrue();
    }

    [Fact]
    public void Failing_without_diagnostics_is_a_programming_error()
    {
        var request = Request();

        var act = () => request.Fail("   ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_status_round_trips_through_its_code()
    {
        ProvisioningRequestStatus.Requested.ToCode().Should().Be("requested");
        ProvisioningRequestStatus.Running.ToCode().Should().Be("running");
        ProvisioningRequestStatus.Completed.ToCode().Should().Be("completed");
        ProvisioningRequestStatus.Failed.ToCode().Should().Be("failed");
        ProvisioningRequestStatusRules.FromCode("running").Should().Be(ProvisioningRequestStatus.Running);

        var act = () => ProvisioningRequestStatusRules.FromCode("queued");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static DivisionProvisioningRequest Request() =>
        DivisionProvisioningRequest.Request(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            targetTier: 2,
            Guid.CreateVersion7(),
            "world-seed-1",
            Now);
}

/// <summary>The generation-run record that makes generated content traceable (`PYR-14`).</summary>
public sealed class GenerationRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_run_starts_with_no_output_counts()
    {
        var run = Start();

        run.Status.Should().Be(GenerationRunStatus.Running);
        run.CountriesCreated.Should().Be(0);
        run.ClubsCreated.Should().Be(0);
        run.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Succeeding_records_what_the_run_produced()
    {
        var run = Start();

        run.Succeed(new GenerationRunCounts(Countries: 6, Clubs: 108, Players: 0, Accounts: 108), Now.AddMinutes(4));

        run.Status.Should().Be(GenerationRunStatus.Succeeded);
        run.CountriesCreated.Should().Be(6);
        run.ClubsCreated.Should().Be(108);
        run.AccountsCreated.Should().Be(108);
        run.CompletedAt.Should().Be(Now.AddMinutes(4));
    }

    [Fact]
    public void A_failed_run_keeps_its_diagnostics()
    {
        var run = Start();

        run.Fail("country ENG already had a tier 1", Now.AddMinutes(1));

        run.Status.Should().Be(GenerationRunStatus.Failed);
        run.Diagnostics.Should().Be("country ENG already had a tier 1");
    }

    [Fact]
    public void Starting_a_run_without_a_seed_or_generator_version_is_a_programming_error()
    {
        var noSeed = () => GenerationRun.Start(
            Guid.CreateVersion7(),
            GenerationRunKind.WorldBootstrap,
            "  ",
            "generator-v1",
            "input-hash",
            Now);

        var noVersion = () => GenerationRun.Start(
            Guid.CreateVersion7(),
            GenerationRunKind.WorldBootstrap,
            "seed",
            "  ",
            "input-hash",
            Now);

        noSeed.Should().Throw<ArgumentException>();
        noVersion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Every_kind_and_status_round_trips_through_its_code()
    {
        GenerationRunKind.WorldBootstrap.ToCode().Should().Be("world_bootstrap");
        GenerationRunKind.DivisionProvisioning.ToCode().Should().Be("division_provisioning");
        GenerationRuns.KindFromCode("division_provisioning").Should().Be(GenerationRunKind.DivisionProvisioning);

        GenerationRunStatus.Succeeded.ToCode().Should().Be("succeeded");
        GenerationRuns.StatusFromCode("failed").Should().Be(GenerationRunStatus.Failed);

        var unknownKind = () => GenerationRuns.KindFromCode("seed");
        var unknownStatus = () => GenerationRuns.StatusFromCode("paused");

        unknownKind.Should().Throw<ArgumentOutOfRangeException>();
        unknownStatus.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static GenerationRun Start() =>
        GenerationRun.Start(
            Guid.CreateVersion7(),
            GenerationRunKind.WorldBootstrap,
            "world-seed-1",
            "generator-v1",
            "input-hash",
            Now);
}
