using FluentAssertions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Domain.Match;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// The match effects: how a match's card and injury events become one effect per player (`DIS-1`, `DIS-2`,
/// `DIS-4`).
/// </summary>
/// <remarks>
/// The counts mirror the engine's own reconciliation rule (`MAT-5`): a second yellow is both the booking it
/// was and the sending-off it became, so the discipline a manager reads agrees with the match statistics.
/// </remarks>
public sealed class MatchEffectsCalculatorTests
{
    private static readonly Guid FixtureId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HomeClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AwayClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid Player = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TeamMate = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Opponent = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void A_second_yellow_is_both_a_booking_and_a_sending_off()
    {
        var effects = MatchEffectsCalculator.Calculate(
            [Event(HomeClubId, Player, MatchEventType.SecondYellowCard)]);

        var effect = effects.Should().ContainSingle().Subject;

        effect.YellowCards.Should().Be(1, "the booking happened");
        effect.RedCards.Should().Be(1, "and it ended the player's match");
        effect.AbsenceFixtures.Should().Be(0);
    }

    [Fact]
    public void A_booking_is_not_a_sending_off_and_a_red_is_not_a_booking()
    {
        var effects = MatchEffectsCalculator.Calculate(
        [
            Event(HomeClubId, Player, MatchEventType.YellowCard),
            Event(AwayClubId, Opponent, MatchEventType.RedCard),
        ]);

        var booked = effects.Single(effect => effect.PlayerId == Player);

        booked.YellowCards.Should().Be(1);
        booked.RedCards.Should().Be(0);

        var sentOff = effects.Single(effect => effect.PlayerId == Opponent);

        sentOff.YellowCards.Should().Be(0);
        sentOff.RedCards.Should().Be(1);
    }

    [Fact]
    public void An_injury_carries_the_absence_the_engine_drew()
    {
        var effects = MatchEffectsCalculator.Calculate(
            [Event(HomeClubId, Player, MatchEventType.Injury, absenceFixtures: 4)]);

        var effect = effects.Should().ContainSingle().Subject;

        effect.AbsenceFixtures.Should().Be(4, "DIS-1: the absence is the fixtures the engine drew");
        effect.YellowCards.Should().Be(0);
        effect.RedCards.Should().Be(0);
    }

    [Fact]
    public void A_players_events_aggregate_into_one_effect()
    {
        var effects = MatchEffectsCalculator.Calculate(
        [
            Event(HomeClubId, Player, MatchEventType.YellowCard),
            Event(HomeClubId, Player, MatchEventType.YellowCard),
            Event(HomeClubId, Player, MatchEventType.Injury, absenceFixtures: 2),
            Event(HomeClubId, TeamMate, MatchEventType.YellowCard),
        ]);

        effects.Should().HaveCount(2, "one effect per player, not per event");

        var player = effects.Single(effect => effect.PlayerId == Player);

        player.YellowCards.Should().Be(2, "the second booking in the same match is the threshold's business");
        player.AbsenceFixtures.Should().Be(2);
    }

    [Fact]
    public void The_effects_are_ordered_by_fixture_club_and_player()
    {
        // Deliberately fed out of order, so the ordering is the calculator's rather than the caller's.
        var effects = MatchEffectsCalculator.Calculate(
        [
            Event(AwayClubId, Opponent, MatchEventType.YellowCard),
            Event(HomeClubId, TeamMate, MatchEventType.YellowCard),
            Event(HomeClubId, Player, MatchEventType.YellowCard),
        ]);

        effects.Select(effect => (effect.ClubId, effect.PlayerId))
            .Should()
            .Equal(
                (HomeClubId, Player),
                (HomeClubId, TeamMate),
                (AwayClubId, Opponent));
    }

    [Fact]
    public void A_match_with_no_effect_events_has_no_effects()
    {
        MatchEffectsCalculator.Calculate([]).Should().BeEmpty();
    }

    private static MatchEffectEvent Event(
        Guid clubId,
        Guid playerId,
        MatchEventType type,
        int absenceFixtures = 0) =>
        new(FixtureId, clubId, type, playerId, absenceFixtures);
}
