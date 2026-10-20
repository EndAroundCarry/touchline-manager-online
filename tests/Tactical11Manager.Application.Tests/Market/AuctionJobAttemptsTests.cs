using FluentAssertions;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Ops;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Tests.Market;

/// <summary>How many attempts a listing's resolution job is allowed (`FA-5`).</summary>
public sealed class AuctionJobAttemptsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_club_listing_keeps_the_default_attempts()
    {
        var listing = TransferListing.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            1_000_000, 50_000, 2, Now, Now.AddDays(2), idempotencyKey: null, Now);

        AuctionJobTypes.MaxAttemptsFor(listing).Should().Be(JobRetryPolicy.DefaultMaxAttempts);
    }

    [Fact]
    public void A_free_agent_listing_retries_for_longer_than_a_whole_rollover()
    {
        var listing = TransferListing.OpenFreeAgent(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            1_000_000, 50_000, 2, Now, Now.AddDays(7), Now);

        var attempts = AuctionJobTypes.MaxAttemptsFor(listing);

        // The shortest retry delay is the cap at its lowest jitter, 15 minutes.
        var shortestWait = TimeSpan.FromTicks(JobRetryPolicy.MaxDelay.Ticks / 2) * attempts;

        shortestWait.Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(WorldRuleSet.RolloverDays));
    }
}
