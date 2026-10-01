using FluentAssertions;
using TouchlineManager.Application.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Application.Tests.Match;

/// <summary>
/// The match load: how a played result becomes condition consumed, fatigue accumulated, and morale moved
/// for each player who appeared (`TRN-11`, `TRN-13`).
/// </summary>
/// <remarks>
/// The rule is arithmetic over the frozen facts, so every case here is exact rather than statistical: a
/// neutral full match by a maximum-stamina player costs <see cref="MatchLoadCalculator.ConditionCostPerMinuteBp"/>
/// times ninety, adjusted by the stamina and intensity factors the calculator documents.
/// </remarks>
public sealed class MatchLoadCalculatorTests
{
    private static readonly Guid FixtureId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HomeClubId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AwayClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid HomePlayer = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid HomeBench = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AwayPlayer = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void A_full_match_costs_condition_and_adds_fatigue()
    {
        var load = MatchLoadCalculator.Calculate(
        [
            Match(
                homeGoals: 1,
                awayGoals: 0,
                lines:
                [
                    Line(HomePlayer, HomeClubId, MatchSide.Home, minutes: 90),
                    Line(AwayPlayer, AwayClubId, MatchSide.Away, minutes: 90),
                ]),
        ]).Single(candidate => candidate.PlayerId == HomePlayer);

        // A maximum-stamina player (factor 8,000) in a neutral side (factor 10,000), ninety minutes:
        // condition 90 × 10 × 8,000/10,000 = 720; fatigue 90 × 7 × 8,000/10,000 = 504.
        load.ConditionDeltaBp.Should().Be(-720, "a match consumes condition (TRN-11)");
        load.FatigueDeltaBp.Should().Be(504, "and adds fatigue");
        load.MoraleDeltaBp.Should().Be(MatchLoadCalculator.MoraleWinBp, "the win moves morale (TRN-13)");
        load.ClubId.Should().Be(HomeClubId);
    }

    [Fact]
    public void A_defeat_lowers_morale_as_much_as_a_win_raises_it()
    {
        var (home, away) = Played(
            homeGoals: 0,
            awayGoals: 2,
            HomePlayer,
            AwayPlayer);

        home.MoraleDeltaBp.Should().Be(-MatchLoadCalculator.MoraleLossBp);
        away.MoraleDeltaBp.Should().Be(MatchLoadCalculator.MoraleWinBp);
    }

    [Fact]
    public void A_draw_moves_morale_only_a_little()
    {
        var (home, _) = Played(0, 0, HomePlayer, AwayPlayer);

        home.MoraleDeltaBp.Should().Be(MatchLoadCalculator.MoraleDrawBp);
    }

    [Fact]
    public void An_unused_substitute_takes_no_load()
    {
        var loads = MatchLoadCalculator.Calculate(
        [
            Match(
                homeGoals: 1,
                awayGoals: 0,
                lines:
                [
                    Line(HomePlayer, HomeClubId, MatchSide.Home, minutes: 90),
                    Line(HomeBench, HomeClubId, MatchSide.Home, minutes: 0),
                ]),
        ]);

        loads.Select(load => load.PlayerId).Should().Equal(
            [HomePlayer],
            "a player who never left the bench carries none of the result (TRN-13)");
    }

    [Fact]
    public void Morale_carries_the_result_in_proportion_to_minutes()
    {
        var cameo = MatchLoadCalculator.Calculate(
        [
            Match(
                homeGoals: 1,
                awayGoals: 0,
                lines: [Line(HomeBench, HomeClubId, MatchSide.Home, minutes: 30)]),
        ]).Single();

        // 350 × min(10,000, 30 × 10,000 / 90) / 10,000 = 350 × 3,333 / 10,000 = 116.
        cameo.MoraleDeltaBp.Should().Be(116, "a cameo carries less of the result than a full match");
    }

    [Fact]
    public void A_fitter_player_pays_less_for_the_same_minutes()
    {
        var unfit = MatchLoadCalculator.ConditionCostBp(90, stamina: 1, MatchLoadCalculator.NeutralIntensityBp);
        var fit = MatchLoadCalculator.ConditionCostBp(90, stamina: 20, MatchLoadCalculator.NeutralIntensityBp);

        unfit.Should().Be(1_080, "no stamina means the maximum factor of 1.2");
        fit.Should().Be(720, "maximum stamina means the minimum factor of 0.8");
        fit.Should().BeLessThan(unfit);
    }

    [Fact]
    public void A_harder_side_pays_more_and_time_wasting_pays_less()
    {
        var attacking = MatchLoadCalculator.IntensityBp(new MatchInstructionsV1
        {
            Mentality = MatchMentality.Attacking,
            Tempo = MatchTempo.High,
            Pressing = MatchPressing.HighPress,
            DefensiveLine = MatchDefensiveLine.High,
        });

        var defensive = MatchLoadCalculator.IntensityBp(new MatchInstructionsV1
        {
            Mentality = MatchMentality.Defensive,
            Tempo = MatchTempo.Low,
            Pressing = MatchPressing.LowBlock,
            DefensiveLine = MatchDefensiveLine.Deep,
            TimeWasting = MatchTimeWasting.On,
        });

        var neutral = MatchLoadCalculator.IntensityBp(new MatchInstructionsV1());

        neutral.Should().Be(MatchLoadCalculator.NeutralIntensityBp, "the default set neither adds nor saves");
        attacking.Should().BeGreaterThan(neutral, "every aggressive instruction raises the load (INS-9)");
        defensive.Should().BeLessThan(neutral, "and every conservative one lowers it");
    }

    [Fact]
    public void Loads_are_ordered_by_club_and_player()
    {
        // Deliberately fed out of order, so the ordering is the calculator's rather than the caller's.
        var loads = MatchLoadCalculator.Calculate(
        [
            Match(
                homeGoals: 0,
                awayGoals: 0,
                lines:
                [
                    Line(AwayPlayer, AwayClubId, MatchSide.Away, minutes: 90),
                    Line(HomeBench, HomeClubId, MatchSide.Home, minutes: 10),
                    Line(HomePlayer, HomeClubId, MatchSide.Home, minutes: 90),
                ]),
        ]);

        loads.Select(load => (load.ClubId, load.PlayerId)).Should().Equal(
            (HomeClubId, HomePlayer),
            (HomeClubId, HomeBench),
            (AwayClubId, AwayPlayer));
    }

    [Fact]
    public void An_intensity_outside_the_band_is_refused()
    {
        var act = () => MatchLoadCalculator.ConditionCostBp(90, stamina: 10, intensityBp: 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_attribute_outside_the_scale_is_refused()
    {
        var act = () => MatchLoadCalculator.FatigueGainBp(90, stamina: 21, MatchLoadCalculator.NeutralIntensityBp);

        act.Should().Throw<ArgumentOutOfRangeException>("stamina is on the 1–20 scale (TRN-4)");
    }

    private static (MatchPlayerLoad Home, MatchPlayerLoad Away) Played(
        int homeGoals,
        int awayGoals,
        Guid homePlayer,
        Guid awayPlayer)
    {
        var loads = MatchLoadCalculator.Calculate(
        [
            Match(
                homeGoals,
                awayGoals,
                lines:
                [
                    Line(homePlayer, HomeClubId, MatchSide.Home, minutes: 90),
                    Line(awayPlayer, AwayClubId, MatchSide.Away, minutes: 90),
                ]),
        ]);

        return (
            loads.Single(load => load.PlayerId == homePlayer),
            loads.Single(load => load.PlayerId == awayPlayer));
    }

    private static MatchLoadInput Match(
        int homeGoals,
        int awayGoals,
        IReadOnlyList<MatchPlayerLineV1> lines) =>
        new(
            FixtureId,
            homeGoals,
            awayGoals,
            Input(),
            lines);

    private static MatchInputV1 Input() => new()
    {
        FixtureId = FixtureId,
        WorldId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
        SeasonId = Guid.Parse("88888888-8888-8888-8888-888888888888"),
        EngineVersion = EngineVersions.EngineLabel,
        RuleSetVersion = EngineVersions.RuleSetLabel,
        Home = Side(HomeClubId, HomePlayer, HomeBench),
        Away = Side(AwayClubId, AwayPlayer),
        HomeAdvantageBasisPoints = 10_300,
        FormulaConfigurationHash = "formula-hash",
        Seed = 42UL,
    };

    private static MatchSideV1 Side(Guid clubId, params Guid[] playerIds) => new()
    {
        ClubId = clubId,
        ClubName = "A club",
        Squad = [.. playerIds.Select(playerId => Participant(clubId, playerId))],
        Slots = [],
        Instructions = new MatchInstructionsV1(),
    };

    private static MatchParticipantV1 Participant(Guid clubId, Guid playerId) => new()
    {
        ParticipantId = playerId,
        PlayerId = playerId,
        ClubId = clubId,
        DisplayName = "A player",
        ShirtNumber = 1,
        Position = MatchPosition.Striker,
        Attributes = PlayerAttributesV1.From(
            [.. Enumerable.Repeat(20, MatchAttributeNames.Count)]),
        State = PlayerMatchStateV1.Uniform(5_000),
    };

    private static MatchPlayerLineV1 Line(
        Guid playerId,
        Guid clubId,
        MatchSide side,
        int minutes) => new()
        {
            ParticipantId = playerId,
            ClubId = clubId,
            Side = side,
            Started = minutes > 0,
            MinutesPlayed = minutes,
            Goals = 0,
            Assists = 0,
            YellowCards = 0,
            SentOff = false,
            AbsenceFixtures = 0,
            RatingBasisPoints = 0,
            FinalConditionBasisPoints = 6_000,
        };
}
