using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Tests.Market;

/// <summary>How a listing and a transfer read on the wire when there is no seller (`FA-5`).</summary>
public sealed class MarketMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_free_agent_listing_is_kind_free_agent_with_no_seller_and_shows_the_ability()
    {
        var response = ListingRow(sellerClubId: null, sellerClubName: null).ToResponse();

        response.Kind.Should().Be(ListingKinds.FreeAgentCode);
        response.SellerClubId.Should().BeNull();
        response.SellerClubName.Should().BeNull();
        response.Ability.Should().Be(8);
    }

    [Fact]
    public void A_club_listing_is_kind_club_and_names_the_seller()
    {
        var clubId = Guid.CreateVersion7();

        var response = ListingRow(clubId, "Rovers").ToResponse();

        response.Kind.Should().Be(ListingKinds.ClubCode);
        response.SellerClubId.Should().Be(clubId);
        response.SellerClubName.Should().Be("Rovers");
    }

    [Fact]
    public void A_free_agent_transfer_has_no_seller_in_the_history()
    {
        var row = new TransferHistoryRow(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "A. Player",
            SellerClubId: null,
            SellerClubName: null,
            Guid.CreateVersion7(),
            "United",
            120_000,
            Now);

        var response = row.ToResponse();

        response.SellerClubId.Should().BeNull();
        response.SellerClubName.Should().BeNull();
        response.BuyerClubName.Should().Be("United");
    }

    [Theory]
    [InlineData("club", true, ListingKind.Club)]
    [InlineData("free_agent", true, ListingKind.FreeAgent)]
    [InlineData("Club", false, ListingKind.Club)]
    [InlineData("pool", false, ListingKind.Club)]
    public void A_listing_kind_parses_only_its_stable_code(string code, bool known, ListingKind expected)
    {
        ListingKinds.TryFromCode(code, out var kind).Should().Be(known);
        kind.Should().Be(expected);
    }

    private static ListingRow ListingRow(Guid? sellerClubId, string? sellerClubName) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "A. Player",
            "A. Player",
            PlayerPosition.CentreBack,
            1_990,
            Ability: 8,
            sellerClubId,
            sellerClubName,
            MinimumFeeMinor: 100_000,
            GeneratedBuyerSeasonWageMinor: 5_000,
            GeneratedContractSeasons: 2,
            Now,
            Now.AddDays(7),
            ListingStatus.Open,
            LeadingAmountMinor: null,
            BidderCount: 0,
            ViewerBidMinor: null,
            GameYear: 2_012,
            Version: 1);
}
