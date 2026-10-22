using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Application.Tests.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Market;

/// <summary>
/// The wage impact a viewer reads on a listing (`WAGE-1`): computed by the server for the viewer's club, only on a purchase, and never a refusal.
/// </summary>
public sealed class ListingWageImpactTests
{
    private static readonly Guid Viewer = Guid.Parse("0192f100-0000-7000-8000-000000000001");

    private static readonly Guid Seller = Guid.Parse("0192f100-0000-7000-8000-000000000002");

    [Fact]
    public async Task A_listing_the_viewer_could_buy_carries_the_impact_of_the_smallest_acceptable_bid()
    {
        var (impact, finance) = Arrange(cashMinor: 5_000_000_000, wageBillMinor: 100_000_000);
        var listing = Listing(Seller, minimumFee: 200_000_000, wage: 50_000_000);

        var read = await impact.AttachAsync(listing, Viewer, CancellationToken.None);

        var response = read.WageImpact.Should().NotBeNull().And.Subject.As<Tactical11Manager.Contracts.Finance.WageImpactResponse>();
        response.AddedSeasonWageMinor.Should().Be(50_000_000, "the listing quotes the wage he would sign for");
        response.WageBillAfterMinor.Should().Be(150_000_000, "the club's bill and the new wage");
        response.NeedsConfirmation.Should().Be(response.Level is "danger" or "administration_risk");
        finance.Basis.Should().ContainSingle();
    }

    [Fact]
    public async Task The_clubs_own_listing_and_a_listing_it_already_leads_carry_none()
    {
        var (impact, _) = Arrange(cashMinor: 5_000_000_000, wageBillMinor: 100_000_000);

        var own = await impact.AttachAsync(Listing(Viewer, 200_000_000, 50_000_000), Viewer, CancellationToken.None);
        var led = await impact.AttachAsync(Listing(Seller, 200_000_000, 50_000_000) with { ViewerBidMinor = 200_000_000 }, Viewer, CancellationToken.None);

        own.WageImpact.Should().BeNull("selling is not a purchase");
        led.WageImpact.Should().BeNull("a bid the club leads is already in its wage bill and its reserved cash");
    }

    [Fact]
    public async Task A_club_that_would_end_the_season_in_the_red_is_warned_to_confirm_and_never_refused()
    {
        var (impact, _) = Arrange(cashMinor: 1_000_000, wageBillMinor: 100_000_000);

        var read = await impact.AttachAsync(Listing(Seller, minimumFee: 500_000, wage: 90_000_000_000), Viewer, CancellationToken.None);

        read.WageImpact!.NeedsConfirmation.Should().BeTrue();
        read.WageImpact.Level.Should().BeOneOf("danger", "administration_risk");
    }

    [Fact]
    public async Task A_viewer_with_no_account_reads_the_listings_unchanged()
    {
        var impact = new ListingWageImpact(new WageImpactReader(new FinanceDoubles.StubFinanceQueries()));
        var listing = Listing(Seller, 200_000_000, 50_000_000);

        var read = await impact.AttachAsync(listing, Viewer, CancellationToken.None);

        read.Should().Be(listing);
    }

    private static (ListingWageImpact Impact, FinanceDoubles.StubFinanceQueries Finance) Arrange(long cashMinor, long wageBillMinor)
    {
        var finance = new FinanceDoubles.StubFinanceQueries();

        finance.Basis.Add(new ClubFinanceBasis(
            Viewer,
            TierNumber: 1,
            new StadiumSeats(3_000, 1_000, 900, 100),
            Fans: 10_000,
            FanRuleSet.CalmMoodBp,
            StructureRuleSet.OpeningLevel,
            StructureRuleSet.OpeningLevel,
            SponsorFixedMatchdayMinor: 0,
            wageBillMinor,
            ContractedPlayers: 20));

        finance.Money[Viewer] = new ClubMoneyPosition(cashMinor, ReservedMinor: 0, LeadingBidWagesMinor: 0, MatchdaysPlayed: 0);

        return (new ListingWageImpact(new WageImpactReader(finance)), finance);
    }

    private static ListingRow Listing(Guid sellerClubId, long minimumFee, long wage) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Player",
            "P",
            PlayerPosition.CentralMidfielder,
            BirthGameYear: 2000,
            Ability: 12,
            sellerClubId,
            "Seller",
            minimumFee,
            wage,
            GeneratedContractSeasons: 2,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            ListingStatus.Open,
            LeadingAmountMinor: null,
            BidderCount: 0,
            ViewerBidMinor: null,
            GameYear: 2026,
            Version: 1);
}
