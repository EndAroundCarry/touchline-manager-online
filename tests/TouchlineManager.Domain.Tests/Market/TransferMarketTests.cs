using FluentAssertions;
using TouchlineManager.Domain.Market;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Domain.Tests.Market;

/// <summary>
/// The market aggregates: a listing's window and lifecycle (`TRF-1`, `TRF-2`, `TRF-14`, `TRF-15`), a bid's
/// one-way path (`TRF-4`…`TRF-8`), and a shortlist note's bound (`SCT-3`).
/// </summary>
public sealed class TransferMarketTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static TransferListing Listing() => TransferListing.Open(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        minimumFeeMinor: 1_000_000,
        generatedBuyerWageMinor: 50_000,
        generatedContractSeasons: 2,
        opensAt: Now,
        endsAt: AuctionWindows.EndsAtFor(Now),
        idempotencyKey: null,
        now: Now);

    private static TransferBid Bid(TransferListing listing, long amount = 1_000_000) => TransferBid.Place(
        Guid.CreateVersion7(),
        listing.Id,
        Guid.CreateVersion7(),
        amount,
        "bid:test:reserve",
        idempotencyKey: null,
        now: Now);

    [Fact]
    public void A_listing_needs_a_positive_fee_and_a_window_after_it_opens()
    {
        var noFee = () => TransferListing.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            minimumFeeMinor: 0, generatedBuyerWageMinor: 1, generatedContractSeasons: 1,
            opensAt: Now, endsAt: Now.AddDays(3), idempotencyKey: null, now: Now);
        noFee.Should().Throw<ArgumentOutOfRangeException>("TRF-1: a minimum fee is positive");

        var backwards = () => TransferListing.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            minimumFeeMinor: 1, generatedBuyerWageMinor: 1, generatedContractSeasons: 1,
            opensAt: Now, endsAt: Now, idempotencyKey: null, now: Now);
        backwards.Should().Throw<ArgumentOutOfRangeException>("TRF-2: a listing resolves after it opens");

        var tooLong = () => TransferListing.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            minimumFeeMinor: 1, generatedBuyerWageMinor: 1,
            generatedContractSeasons: WorldRuleSet.ContractMaxSeasons + 1,
            opensAt: Now, endsAt: Now.AddDays(3), idempotencyKey: null, now: Now);
        tooLong.Should().Throw<ArgumentOutOfRangeException>("CON-1: a contract is one to three seasons");
    }

    [Fact]
    public void A_listing_leaves_open_only_once()
    {
        var listing = Listing();

        listing.IsOpen.Should().BeTrue();
        listing.MarkSold(Now);
        listing.Status.Should().Be(ListingStatus.Sold);

        var again = () => listing.Cancel(Now);
        again.Should().Throw<InvalidOperationException>("a listing that has resolved cannot be cancelled");
    }

    [Fact]
    public void A_bid_is_placed_leading_and_moves_only_once()
    {
        var listing = Listing();
        var bid = Bid(listing);

        bid.IsLeading.Should().BeTrue("TRF-4");
        bid.AmountMinor.Should().Be(1_000_000);

        bid.Outbid(Now);
        bid.Status.Should().Be(BidStatus.Outbid);

        var again = () => bid.Win(Now);
        again.Should().Throw<InvalidOperationException>("a bid that has left leading cannot move again");
    }

    [Fact]
    public void A_leading_bid_can_be_raised_but_a_resolved_one_cannot()
    {
        var listing = Listing();
        var bid = Bid(listing);

        bid.Raise(1_250_000, idempotencyKey: null, Now);
        bid.AmountMinor.Should().Be(1_250_000, "TRF-6");

        bid.Release(Now);

        var afterRelease = () => bid.Raise(1_500_000, idempotencyKey: null, Now);
        afterRelease.Should().Throw<InvalidOperationException>("only a leading bid can be raised");
    }

    [Fact]
    public void A_bid_must_move_a_positive_amount()
    {
        var listing = Listing();

        var free = () => Bid(listing, amount: 0);
        free.Should().Throw<ArgumentOutOfRangeException>("TRF-4");
    }

    [Fact]
    public void A_shortlist_note_is_bounded()
    {
        var longNote = new string('x', WorldRuleSet.ShortlistNotesMaxLength + 1);

        var tooLong = () => ShortlistEntry.Add(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), longNote, Now);
        tooLong.Should().Throw<ArgumentOutOfRangeException>("SCT-3");

        var entry = ShortlistEntry.Add(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "  watch him  ", Now);
        entry.Notes.Should().Be("watch him", "a note is trimmed");

        entry.UpdateNotes(null, Now);
        entry.Notes.Should().BeNull();
    }
}
