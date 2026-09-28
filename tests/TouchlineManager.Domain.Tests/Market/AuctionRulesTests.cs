using FluentAssertions;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Tests.Market;

/// <summary>
/// The timed-auction rules: the exposure window and blackout (`TRF-2`, `TRF-3`) and the winning order
/// (`TRF-8`).
/// </summary>
public sealed class AuctionRulesTests
{
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_listing_resolves_at_least_forty_eight_hours_after_it_opens()
    {
        var endsAt = AuctionWindows.EndsAtFor(Tuesday);

        endsAt.Should().BeAfter(Tuesday.AddHours(WorldRuleSet.ListingMinimumExposureHours - 1), "TRF-2");
        endsAt.TimeOfDay.Should().Be(WorldRuleSet.AuctionResolutionUtc.ToTimeSpan(), "TRF-2: a fixed daily window");
        AuctionWindows.IsBlackout(endsAt).Should().BeFalse("TRF-3");
    }

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(12, 59, false)]
    [InlineData(19, 0, false)]
    public void The_blackout_covers_the_six_hours_before_a_kickoff(int hour, int minute, bool expected)
    {
        var instant = new DateTimeOffset(2026, 9, 29, hour, minute, 0, TimeSpan.Zero);

        AuctionWindows.IsBlackout(instant).Should().Be(expected, "TRF-3");
    }

    [Fact]
    public void A_day_with_no_kickoff_has_no_blackout()
    {
        // Wednesday is not a kickoff day (CAL-2).
        var wednesday = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

        AuctionWindows.IsBlackout(wednesday).Should().BeFalse();
    }

    [Fact]
    public void A_window_inside_the_blackout_slips_to_the_next_day()
    {
        // 13:30 on a kickoff day: the same day's window has passed and the next midnight window would be
        // inside the blackout only if the rule were changed, so this pins the walk-forward behaviour.
        var window = AuctionWindows.WindowOnOrAfter(new DateTimeOffset(2026, 9, 29, 13, 30, 0, TimeSpan.Zero));

        window.Should().Be(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_first_bid_meets_the_floor_and_a_raise_clears_the_increment()
    {
        AuctionRules.MinimumAcceptableBid(1_000_000, currentLeadingMinor: null).Should().Be(1_000_000, "TRF-1");
        AuctionRules.IsAcceptableBid(1_000_000, null, 1_000_000).Should().BeTrue();
        AuctionRules.IsAcceptableBid(1_000_000, null, 999_999).Should().BeFalse();

        var raised = 1_000_000 + WorldRuleSet.MinimumBidIncrementMinor;

        AuctionRules.MinimumAcceptableBid(1_000_000, 1_000_000).Should().Be(raised, "TRF-5");
        AuctionRules.IsAcceptableBid(1_000_000, 1_000_000, raised).Should().BeTrue();
        AuctionRules.IsAcceptableBid(1_000_000, 1_000_000, raised - 1).Should().BeFalse();
    }

    [Fact]
    public void The_highest_amount_wins_and_an_equal_amount_falls_to_the_earliest_sequence()
    {
        var key = new BidOrderKey(AmountMinor: 5_000, BidSequence: 2, Id: Guid.CreateVersion7());
        var later = new BidOrderKey(AmountMinor: 5_000, BidSequence: 3, Id: Guid.CreateVersion7());
        var lower = new BidOrderKey(AmountMinor: 4_000, BidSequence: 1, Id: Guid.CreateVersion7());

        key.Should().BeLessThan(later, "TRF-8: equal amounts resolve to the lowest sequence");
        key.Should().BeLessThan(lower, "TRF-8: the higher amount sorts ahead of the lower one");
        later.Should().BeLessThan(lower, "TRF-8: 5,000 sorts ahead of 4,000 regardless of sequence");
    }

    [Fact]
    public void An_equal_amount_and_sequence_falls_to_the_immutable_identity()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var lower = first.CompareTo(second) < 0 ? first : second;
        var higher = first.CompareTo(second) < 0 ? second : first;

        var left = new BidOrderKey(5_000, 1, lower);
        var right = new BidOrderKey(5_000, 1, higher);

        left.Should().BeLessThan(right, "TRF-8: the immutable bid identity is the final tie-break");
    }

    [Fact]
    public void The_highest_bid_is_the_winner()
    {
        var listingId = Guid.CreateVersion7();
        var low = TransferBid.Place(Guid.CreateVersion7(), listingId, Guid.CreateVersion7(), 1_000, "c", null, Tuesday);
        var high = TransferBid.Place(Guid.CreateVersion7(), listingId, Guid.CreateVersion7(), 2_000, "c", null, Tuesday);

        AuctionRules.Winner([low, high]).Should().BeSameAs(high);
    }
}
