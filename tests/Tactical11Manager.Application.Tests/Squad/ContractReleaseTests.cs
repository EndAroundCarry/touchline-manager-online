using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Tests.Squad;

/// <summary>
/// The rules that decide whether a club may release a player now, and what it costs (`CON-12`).
/// </summary>
public sealed class ContractReleaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_payout_is_half_the_wages_left_in_the_season_and_in_the_seasons_after_it()
    {
        // 10 weeks to the end of the season, one more season of 12 weeks: 22 weeks at 1,000 is half of 22,000.
        var context = Context(wage: 1_000, endSeason: 3, currentSeason: 2, seasonEnds: Now.AddDays(70), weeksPerSeason: 12);

        var quote = ContractRelease.Price(context, Now);

        quote.RemainingWeeks.Should().Be(22);
        quote.PayoutMinor.Should().Be(11_000);
    }

    [Fact]
    public void A_contract_ending_this_season_pays_for_the_rest_of_this_season_only()
    {
        var context = Context(wage: 1_000, endSeason: 2, currentSeason: 2, seasonEnds: Now.AddDays(70));

        ContractRelease.Price(context, Now).PayoutMinor.Should().Be(5_000);
    }

    [Fact]
    public void A_clean_release_is_allowed()
    {
        var context = Context();

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), availableMinor: 1_000_000)
            .Should().BeNull();
    }

    [Fact]
    public void Nothing_changes_while_the_season_is_closing()
    {
        var context = Context(isRollover: true);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), 1_000_000)
            .Should().Be(ContractReleaseOutcome.SeasonRollover);
    }

    [Fact]
    public void A_listed_player_must_be_withdrawn_first()
    {
        var context = Context(isListed: true);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), 1_000_000)
            .Should().Be(ContractReleaseOutcome.PlayerListed);
    }

    [Fact]
    public void A_squad_of_eighteen_cannot_lose_a_player()
    {
        var context = Context(registered: 18);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), 1_000_000)
            .Should().Be(ContractReleaseOutcome.SquadTooSmall);
    }

    [Fact]
    public void A_squad_of_nineteen_can()
    {
        var context = Context(registered: 19);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), 1_000_000)
            .Should().BeNull();
    }

    [Fact]
    public void The_second_goalkeeper_cannot_be_released_but_an_outfield_player_can()
    {
        var keeper = Context(position: PlayerPosition.Goalkeeper, goalkeepers: 2);
        var striker = Context(position: PlayerPosition.Striker, goalkeepers: 2);

        ContractRelease.Refusal(keeper, ContractRelease.Price(keeper, Now), 1_000_000)
            .Should().Be(ContractReleaseOutcome.GoalkeepersTooFew);
        ContractRelease.Refusal(striker, ContractRelease.Price(striker, Now), 1_000_000)
            .Should().BeNull();
    }

    [Fact]
    public void A_third_goalkeeper_can_go()
    {
        var context = Context(position: PlayerPosition.Goalkeeper, goalkeepers: 3);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), 1_000_000)
            .Should().BeNull();
    }

    [Fact]
    public void The_payout_must_fit_in_the_cash_left_after_bids()
    {
        var context = Context(wage: 1_000, endSeason: 2, currentSeason: 2, seasonEnds: Now.AddDays(70));
        var quote = ContractRelease.Price(context, Now);

        quote.PayoutMinor.Should().Be(5_000);
        ContractRelease.Refusal(context, quote, availableMinor: 4_999)
            .Should().Be(ContractReleaseOutcome.InsufficientFunds);
        ContractRelease.Refusal(context, quote, availableMinor: 5_000).Should().BeNull();
    }

    [Fact]
    public void The_rule_that_blocks_a_release_first_is_the_one_named()
    {
        var context = Context(isRollover: true, isListed: true, registered: 18);

        ContractRelease.Refusal(context, ContractRelease.Price(context, Now), availableMinor: 0)
            .Should().Be(ContractReleaseOutcome.SeasonRollover, "rollover outranks everything a manager could fix");
    }

    [Theory]
    [InlineData(ContractReleaseOutcome.SeasonRollover, SquadErrorCodes.ReleaseSeasonRollover)]
    [InlineData(ContractReleaseOutcome.PlayerListed, SquadErrorCodes.ReleasePlayerListed)]
    [InlineData(ContractReleaseOutcome.SquadTooSmall, SquadErrorCodes.ReleaseSquadTooSmall)]
    [InlineData(ContractReleaseOutcome.GoalkeepersTooFew, SquadErrorCodes.ReleaseGoalkeepersTooFew)]
    [InlineData(ContractReleaseOutcome.InsufficientFunds, SquadErrorCodes.ReleaseInsufficientFunds)]
    public void Each_blocking_rule_has_its_own_stable_code(ContractReleaseOutcome outcome, string code)
    {
        ContractRelease.CodeOf(outcome).Should().Be(code);
    }

    [Fact]
    public void No_refusal_has_no_code()
    {
        ContractRelease.CodeOf(null).Should().BeNull();
    }

    [Theory]
    [InlineData("2026-10-20T09:00:00Z", "2026-10-26T12:00:00Z")] // Tuesday before the next Monday
    [InlineData("2026-10-26T11:59:00Z", "2026-10-26T12:00:00Z")] // minutes before the batch opens
    [InlineData("2026-10-26T12:00:00Z", "2026-11-02T12:00:00Z")] // as the batch opens: the next one
    [InlineData("2026-10-26T12:30:00Z", "2026-11-02T12:00:00Z")]
    public void A_released_player_joins_at_the_next_monday_batch(string now, string joins)
    {
        ContractRelease.JoinsPoolAt(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(DateTimeOffset.Parse(joins, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ReleaseContext Context(
        long wage = 1_000,
        int endSeason = 2,
        int currentSeason = 1,
        DateTimeOffset? seasonEnds = null,
        int weeksPerSeason = 12,
        bool isRollover = false,
        PlayerPosition position = PlayerPosition.CentreBack,
        int registered = 24,
        int goalkeepers = 3,
        bool isListed = false) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ContractVersion: 1,
            wage,
            endSeason,
            currentSeason,
            seasonEnds ?? Now.AddDays(70),
            weeksPerSeason,
            isRollover,
            position,
            registered,
            goalkeepers,
            isListed);
}
