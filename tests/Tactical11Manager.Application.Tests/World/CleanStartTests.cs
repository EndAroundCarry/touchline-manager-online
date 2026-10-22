using System.Text.Json;
using FluentAssertions;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.World;
using Tactical11Manager.Application.World.Validation;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;
using Tactical11Manager.Domain.World.Generation;

namespace Tactical11Manager.Application.Tests.World;

/// <summary>
/// The clean start a new manager may choose: the request that asks for it, the squad it returns the club to and the one ledger entry that sets
/// the cash (`TKO-1`…`TKO-4`).
/// </summary>
public sealed class CleanStartTests
{
    private const string WorldSeed = "clean-start-tests";

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly ClaimClubRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("keep")]
    [InlineData("clean_start")]
    public void A_claim_asks_for_nothing_or_for_one_of_the_two_starts(string? start) =>
        _validator.Validate(new ClaimClubRequest { ClubId = Guid.CreateVersion7(), Start = start }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Keep")]
    [InlineData("fresh")]
    public void A_claim_that_asks_for_some_other_start_is_refused(string start)
    {
        var result = _validator.Validate(new ClaimClubRequest { ClubId = Guid.CreateVersion7(), Start = start });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(ClaimClubRequest.Start));
    }

    [Fact]
    public void The_squad_is_the_same_whenever_it_is_drawn_for_the_same_club_and_season()
    {
        var (country, club, season) = Fixtures();

        var first = CleanStartSquad.Draw(WorldSeed, country, club, 1, season, Now);
        var second = CleanStartSquad.Draw(WorldSeed, country, club, 1, season, Now.AddHours(5));

        Abilities(first).Should().Equal(Abilities(second), "the preview draws what the claim writes");
        first.Select(member => member.Player.FullName).Should().Equal(second.Select(member => member.Player.FullName));

        // The strength reads the same eleven, but between players it rates equally it falls back on their ids, which are new on every draw.
        CleanStartSquad.StrengthOf(first).Should().BeCloseTo(CleanStartSquad.StrengthOf(second), 10);
    }

    [Fact]
    public void Another_season_or_another_club_draws_another_squad()
    {
        var (country, club, season) = Fixtures();
        var nextSeason = Season.Create(Guid.CreateVersion7(), season.WorldId, 2, 2027, "world-rules-v17", SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2027, 10, 1)), Now);
        var otherClub = Club.Generate(Guid.CreateVersion7(), season.WorldId, country.Id, IdentityOf(country, 1), 1, season.GameYear, Now);

        var squad = Abilities(CleanStartSquad.Draw(WorldSeed, country, club, 1, season, Now));

        Abilities(CleanStartSquad.Draw(WorldSeed, country, club, 1, nextSeason, Now)).Should().NotEqual(squad);
        Abilities(CleanStartSquad.Draw(WorldSeed, country, otherClub, 1, season, Now)).Should().NotEqual(squad);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_squad_costs_a_share_of_a_fresh_clubs_forecast_it_can_carry(int tier)
    {
        var (country, club, season) = Fixtures();

        var squad = CleanStartSquad.Draw(WorldSeed, country, club, tier, season, Now);
        var wages = squad.Sum(member => member.Contract.SeasonWageMinor);
        var forecast = EconomyRuleSet.FreshClubForecastMinor(tier);

        wages.Should().BeInRange(
            forecast * EconomyRuleSet.CleanStartWageBandLowBp / 10_000,
            forecast * EconomyRuleSet.CleanStartWageBandHighBp / 10_000,
            "a clean-start squad's wages are 55-65% of the forecast (plan target 12)");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    public void In_the_deep_tiers_the_wage_floor_keeps_the_squad_above_the_band_but_below_the_forecast(int tier)
    {
        var (country, club, season) = Fixtures();

        var squad = CleanStartSquad.Draw(WorldSeed, country, club, tier, season, Now);
        var wages = squad.Sum(member => member.Contract.SeasonWageMinor);
        var forecast = EconomyRuleSet.FreshClubForecastMinor(tier);

        wages.Should().BeGreaterThan(
            forecast * EconomyRuleSet.CleanStartWageBandHighBp / 10_000,
            "every contract is at least the wage floor, and in the deep tiers 24 floors are already more than 65% of the forecast");
        wages.Should().BeLessThan(forecast, "a clean-start club still earns more than its squad costs");
    }

    [Fact]
    public void The_squad_can_field_a_legal_side_and_every_player_is_signed_to_the_club()
    {
        var (country, club, season) = Fixtures();

        var squad = CleanStartSquad.Draw(WorldSeed, country, club, 1, season, Now);

        squad.Should().HaveCountGreaterThanOrEqualTo(WorldRuleSet.SquadMinimumRegistered);
        squad.Count(member => member.Player.PrimaryPosition == PlayerPosition.Goalkeeper).Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers);
        squad.Should().OnlyContain(member => member.Contract.ClubId == club.Id && member.Registration.ClubId == club.Id);
        squad.Should().OnlyContain(member => member.Contract.StartSeasonNumber == season.SequenceNumber);
        squad.Select(member => member.Player.Id).Should().OnlyHaveUniqueItems();
        CleanStartSquad.StrengthOf(squad).Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_clean_start_entry_moves_the_cash_to_the_opening_cash_and_keys_on_the_tenure()
    {
        var clubId = Guid.CreateVersion7();
        var tenureId = Guid.CreateVersion7();

        var posting = LedgerPostings.CleanStart(Guid.CreateVersion7(), clubId, tenureId, deltaMinor: 9_000, openingCashMinor: 4_000);

        posting.Category.Should().Be(LedgerCategory.CleanStart);
        posting.CashDeltaMinor.Should().Be(9_000);
        posting.ReservedDeltaMinor.Should().Be(0);
        posting.SourceId.Should().Be(tenureId);
        posting.CorrelationId.Should().Be(LedgerPostings.CleanStartCorrelationId(tenureId));
        posting.CorrelationId.Length.Should().BeLessThanOrEqualTo(80, "the ledger key limit");
        posting.DescriptionTemplate.Should().Be(LedgerPostings.CleanStartTemplate);

        using var document = JsonDocument.Parse(posting.DescriptionParametersJson);

        document.RootElement.GetProperty("amountMinor").GetInt64().Should().Be(4_000);
        document.RootElement.GetProperty("deltaMinor").GetInt64().Should().Be(9_000);
    }

    [Fact]
    public void A_club_that_was_richer_than_the_opening_cash_gives_the_difference_back()
    {
        var posting = LedgerPostings.CleanStart(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), deltaMinor: -250_000, openingCashMinor: 100_000);

        posting.CashDeltaMinor.Should().Be(-250_000);
    }

    [Fact]
    public void A_clean_start_that_moves_nothing_is_not_an_entry()
    {
        var act = () => LedgerPostings.CleanStart(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), deltaMinor: 0, openingCashMinor: 4_000);

        act.Should().Throw<ArgumentOutOfRangeException>("a ledger entry must move something (FIN-11)");
    }

    private static List<int> Abilities(IReadOnlyList<Domain.Squad.Generation.GeneratedSquadMember> squad) =>
        [.. squad.Select(member => member.Attributes.ToSet().MeanTenths)];

    private static (Country Country, Club Club, Season Season) Fixtures()
    {
        var worldId = Guid.CreateVersion7();
        var country = Country.Create(Guid.CreateVersion7(), worldId, LaunchCountries.All[0], Now);
        var season = Season.Create(
            Guid.CreateVersion7(),
            worldId,
            1,
            2026,
            "world-rules-v17",
            SeasonCalendar.FirstMatchdayOnOrAfter(new DateOnly(2026, 10, 1)),
            Now);
        var club = Club.Generate(Guid.CreateVersion7(), worldId, country.Id, IdentityOf(country, 0), 1, season.GameYear, Now);

        return (country, club, season);
    }

    private static ClubIdentity IdentityOf(Country country, int index) =>
        ClubIdentityGenerator.GenerateDivision(WorldSeed, country.NamePoolKey, country.Code, 1, WorldRuleSet.ClubsPerDivision)[index];
}
