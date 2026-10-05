using FluentAssertions;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The spatial play model of `engine-v3`: duels, scrambles, direct free kicks, live ratings, and the
/// pitch coordinates the play now produces (master plan Stage 2).
/// </summary>
public sealed class SpatialPlayTests
{
    // ---- Spatial mathematics -----------------------------------------------------------------

    [Fact]
    public void Every_away_coordinate_mirrors_to_the_opposite_half()
    {
        var slot = TestMatchFactory.Shape(10); // a striker, deep in the home half's attacking third

        var home = TacticalFormationResolver.Orient(slot.X, slot.Y, isHome: true);
        var away = TacticalFormationResolver.Orient(slot.X, slot.Y, isHome: false);

        home.X.Should().Be(SpatialPitch.PitchLength - away.X);
        home.Y.Should().Be(SpatialPitch.PitchWidth - away.Y);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolved_positions_never_leave_the_pitch(bool isHome)
    {
        var input = TestMatchFactory.Even();

        foreach (var side in new[] { input.Home, input.Away })
        {
            foreach (var slot in side.Slots)
            {
                // Every extreme ball position and both phases, so no combination can walk a token off the pitch.
                foreach (var ball in new[]
                         {
                             new SpatialPoint(0, 0),
                             new SpatialPoint(SpatialPitch.PitchLength, SpatialPitch.PitchWidth),
                             SpatialPoint.Center,
                             new SpatialPoint(SpatialPitch.PitchLength, 0),
                             new SpatialPoint(0, SpatialPitch.PitchWidth),
                         })
                {
                    foreach (var hasPossession in new[] { true, false })
                    {
                        var position = TacticalFormationResolver.ResolvePosition(
                            slot,
                            isHome,
                            hasPossession,
                            ball,
                            side.Instructions,
                            EngineRulesV2.Default);

                        position.X.Should().BeInRange(
                            TacticalFormationResolver.TouchlineMargin,
                            SpatialPitch.PitchLength - TacticalFormationResolver.TouchlineMargin);
                        position.Y.Should().BeInRange(
                            TacticalFormationResolver.TouchlineMargin,
                            SpatialPitch.PitchWidth - TacticalFormationResolver.TouchlineMargin);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_side_in_possession_stands_closer_to_the_ball_than_one_out_of_it()
    {
        var input = TestMatchFactory.Even();
        var slot = input.Home.Slots.First(slot => slot.Family == MatchPositionFamily.Midfield);
        var participant = input.Home.Squad.First(p => p.ParticipantId == slot.ParticipantId);
        var ball = new SpatialPoint(SpatialPitch.PitchLength - 500, SpatialPitch.PitchWidth / 2);

        var attacking = TacticalFormationResolver.ResolvePosition(
            slot, isHome: true, hasPossession: true, ball, input.Home.Instructions, EngineRulesV2.Default);
        var defending = TacticalFormationResolver.ResolvePosition(
            slot, isHome: true, hasPossession: false, ball, input.Home.Instructions, EngineRulesV2.Default);

        Math.Abs(attacking.X - ball.X).Should().BeLessThan(
            Math.Abs(defending.X - ball.X),
            "the side with the ball pushes up to it, the side without holds off");
    }

    [Fact]
    public void The_away_side_is_placed_as_the_home_side_mirrored()
    {
        var input = TestMatchFactory.Even();

        foreach (var family in new[] { MatchPositionFamily.Defence, MatchPositionFamily.Midfield, MatchPositionFamily.Attack })
        {
            var slot = input.Home.Slots.First(slot => slot.Family == family);

            foreach (var hasPossession in new[] { true, false })
            {
                foreach (var ball in new[]
                         {
                             new SpatialPoint(1_000, 1_500),
                             new SpatialPoint(3_000, 5_000),
                             SpatialPoint.Center,
                             new SpatialPoint(8_500, 2_000),
                         })
                {
                    var home = TacticalFormationResolver.ResolvePosition(
                        slot, isHome: true, hasPossession, ball, input.Home.Instructions, EngineRulesV2.Default);
                    var away = TacticalFormationResolver.ResolvePosition(
                        slot,
                        isHome: false,
                        hasPossession,
                        new SpatialPoint(SpatialPitch.PitchLength - ball.X, SpatialPitch.PitchWidth - ball.Y),
                        input.Home.Instructions,
                        EngineRulesV2.Default);

                    away.X.Should().BeCloseTo(SpatialPitch.PitchLength - home.X, 2, "the same shape, seen from the other end");
                    away.Y.Should().BeCloseTo(SpatialPitch.PitchWidth - home.Y, 2);
                }
            }
        }
    }

    [Fact]
    public void Both_sides_move_up_the_pitch_with_the_ball_they_attack_with()
    {
        var input = TestMatchFactory.Even();
        var slot = input.Home.Slots.First(slot => slot.Family == MatchPositionFamily.Midfield);
        var centre = SpatialPitch.PitchWidth / 2;

        var homeDeep = TacticalFormationResolver.ResolvePosition(
            slot, isHome: true, hasPossession: true, new SpatialPoint(2_000, centre), input.Home.Instructions, EngineRulesV2.Default);
        var homeHigh = TacticalFormationResolver.ResolvePosition(
            slot, isHome: true, hasPossession: true, new SpatialPoint(8_000, centre), input.Home.Instructions, EngineRulesV2.Default);
        var awayDeep = TacticalFormationResolver.ResolvePosition(
            slot, isHome: false, hasPossession: true, new SpatialPoint(8_000, centre), input.Away.Instructions, EngineRulesV2.Default);
        var awayHigh = TacticalFormationResolver.ResolvePosition(
            slot, isHome: false, hasPossession: true, new SpatialPoint(2_000, centre), input.Away.Instructions, EngineRulesV2.Default);

        homeHigh.X.Should().BeGreaterThan(homeDeep.X, "the home side attacks towards high X");
        awayHigh.X.Should().BeLessThan(awayDeep.X, "the away side attacks towards low X, so its block moves to low X as the ball does");
    }

    // ---- Duels -----------------------------------------------------------------------------------

    [Fact]
    public void A_far_better_dribbler_wins_more_ground_duels_than_he_loses()
    {
        var attacker = Participant(dribbling: 20, agility: 20, pace: 20, otherwise: 5);
        var defender = Participant(tackling: 3, positioning: 3, strength: 3, otherwise: 5);
        var random = new Pcg32(99);

        var won = 0;

        for (var attempt = 0; attempt < 2_000; attempt++)
        {
            if (DuelResolver.ResolveGroundDuel(
                    Contend(attacker), Contend(defender), attackerIsHome: false,
                    MatchTacklingStyle.Normal, EngineRulesV2.Default, random).AttackerWon)
            {
                won++;
            }
        }

        won.Should().BeGreaterThan(1_400, "attributes decide the run of contests");
        won.Should().BeLessThan(2_000, "the underdog band keeps every contest open");
    }

    [Fact]
    public void Home_advantage_tips_a_level_duel()
    {
        var attacker = Participant(dribbling: 10, agility: 10, pace: 10, otherwise: 10);
        var defender = Participant(tackling: 10, positioning: 10, strength: 10, otherwise: 10);

        var homeWins = ShareWon(attacker, defender, attackerIsHome: true);
        var awayWins = ShareWon(attacker, defender, attackerIsHome: false);

        homeWins.Should().BeGreaterThan(awayWins, "the crowd is worth something (master plan Stage 2)");
    }

    private static double ShareWon(MatchParticipantV1 attacker, MatchParticipantV1 defender, bool attackerIsHome)
    {
        var random = new Pcg32(attackerIsHome ? 1_111UL : 2_222UL);
        var won = 0;

        for (var attempt = 0; attempt < 4_000; attempt++)
        {
            if (DuelResolver.ResolveGroundDuel(
                    Contend(attacker), Contend(defender), attackerIsHome,
                    MatchTacklingStyle.Normal, EngineRulesV2.Default, random).AttackerWon)
            {
                won++;
            }
        }

        return (double)won / 4_000;
    }

    [Fact]
    public void An_aggressive_tackler_fouls_more_than_one_staying_on_his_feet()
    {
        var attacker = Participant(dribbling: 20, agility: 20, pace: 20, otherwise: 5);
        var defender = Participant(tackling: 3, positioning: 3, strength: 3, otherwise: 5);
        var rules = EngineRulesV2.Default;

        var aggressive = FoulShare(attacker, defender, MatchTacklingStyle.Aggressive, rules, 7_777UL);
        var onFeet = FoulShare(attacker, defender, MatchTacklingStyle.StayOnFeet, rules, 7_777UL);

        aggressive.Should().BeGreaterThan(onFeet, "the tackling instruction is a disciplinary trade");
    }

    private static double FoulShare(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        ulong seed)
    {
        var random = new Pcg32(seed);
        var fouls = 0;
        const int attempts = 6_000;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            // Only a duel the attacker wins goes to ground cleanly; the foul roll follows a lost duel.
            if (!DuelResolver.ResolveGroundDuel(Contend(attacker), Contend(defender), false, style, rules, random).WasFoul)
            {
                continue;
            }

            fouls++;
        }

        return (double)fouls / attempts;
    }

    [Fact]
    public void A_scramble_is_decided_by_the_legs_that_arrive_first()
    {
        var quick = Participant(pace: 20, acceleration: 20, workRate: 20, otherwise: 5);
        var slow = Participant(pace: 3, acceleration: 3, workRate: 3, otherwise: 5);
        var random = new Pcg32(31);

        var kept = 0;

        for (var attempt = 0; attempt < 2_000; attempt++)
        {
            if (DuelResolver.ResolveScramble(Contend(quick), Contend(slow), attackerIsHome: false, EngineRulesV2.Default, random))
            {
                kept++;
            }
        }

        kept.Should().BeGreaterThan(1_400, "the quicker side collects the loose ball far more often");
    }

    // ---- Set pieces ------------------------------------------------------------------------------

    [Fact]
    public void A_penalty_is_the_takers_to_lose_and_depends_on_taker_and_keeper()
    {
        var rules = EngineRulesV2.Default;

        var good = SetPieceDirector.PenaltyGoalChance(Slot(Participant(finishing: 16, composure: 16, otherwise: 10)), Slot(Participant(reflexes: 12, otherwise: 10)), rules);
        var poorTaker = SetPieceDirector.PenaltyGoalChance(Slot(Participant(finishing: 6, composure: 6, otherwise: 10)), Slot(Participant(reflexes: 12, otherwise: 10)), rules);
        var greatKeeper = SetPieceDirector.PenaltyGoalChance(Slot(Participant(finishing: 16, composure: 16, otherwise: 10)), Slot(Participant(reflexes: 20, otherwise: 10)), rules);

        good.Should().BeGreaterThan(6_000, "a penalty is the taker's to lose");
        poorTaker.Should().BeLessThan(good, "a poor finisher converts fewer");
        greatKeeper.Should().BeLessThan(good, "a great goalkeeper saves more");
        good.Should().BeInRange(rules.PenaltyMinGoalBasisPoints, rules.PenaltyMaxGoalBasisPoints);
    }

    [Fact]
    public void A_free_kick_out_of_range_is_crossed_rather_than_struck()
    {
        var taker = Participant(setPieces: 20, finishing: 20, otherwise: 10);
        var goalkeeper = Participant(reflexes: 10, otherwise: 10);
        var random = new Pcg32(1);

        var rules = EngineRulesV2.Default with { FreeKickAttemptBasisPoints = 10_000 };

        SetPieceDirector
            .ResolveDirectFreeKick(Slot(taker), Slot(goalkeeper), attackingX: 1_000, rules, random)
            .Attempted
            .Should().BeFalse("a strike from the halfway line is not football");
    }

    [Fact]
    public void A_direct_free_kick_is_scored_more_rarely_than_an_open_play_chance()
    {
        var taker = Participant(setPieces: 20, finishing: 20, otherwise: 10);
        var goalkeeper = Participant(reflexes: 10, otherwise: 10);
        var random = new Pcg32(515_0UL);
        var rules = EngineRulesV2.Default;

        var goals = 0;
        const int attempts = 4_000;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var outcome = SetPieceDirector.ResolveDirectFreeKick(
                Slot(taker), Slot(goalkeeper), attackingX: 9_000, rules, random);

            if (outcome.Attempted && outcome.IsGoal)
            {
                goals++;
            }
        }

        // The attempt share and the conversion share compose to well below the open-play chance rate.
        ((double)goals / attempts).Should().BeLessThan(0.06);
        goals.Should().BeGreaterThan(0, "a great taker scores his share of free kicks");
    }

    [Fact]
    public void A_corner_header_is_contested_between_the_two_jumpers()
    {
        var attacker = Participant(jumpingReach: 20, heading: 20, strength: 20, otherwise: 5);
        var defender = Participant(jumpingReach: 3, heading: 3, strength: 3, otherwise: 5);
        var random = new Pcg32(8_888UL);

        var won = 0;

        for (var attempt = 0; attempt < 2_000; attempt++)
        {
            if (DuelResolver.ResolveAerialDuel(Contend(attacker), Contend(defender), attackerIsHome: false, 0, EngineRulesV2.Default, random).AttackerWon)
            {
                won++;
            }
        }

        won.Should().BeGreaterThan(1_400, "the far better jumper wins the far more headers");
    }

    // ---- Live ratings ----------------------------------------------------------------------------

    [Fact]
    public void Live_ratings_move_within_their_scale_and_start_from_the_baseline()
    {
        var rules = EngineRulesV2.Default;
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        foreach (var line in result.PlayerLines)
        {
            if (line.MinutesPlayed <= 0)
            {
                line.LiveRatingBasisPoints.Should().Be(0, "nobody who did not appear has a live rating");
            }
            else
            {
                line.LiveRatingBasisPoints.Should().BeInRange(
                    EngineRulesV2.MinLiveRatingBasisPoints,
                    EngineRulesV2.MaxLiveRatingBasisPoints);
            }
        }

        rules.LiveRatingBaseBasisPoints.Should().Be(6_000);
    }

    [Fact]
    public void A_scorer_ends_on_a_higher_live_rating_than_a_who_led_the_same_match_and_did_nothing()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        var scorers = result.PlayerLines.Where(line => line.Goals > 0).ToList();

        if (scorers.Count == 0)
        {
            return; // This seed's fixture may finish goalless; the invariant is about scorers, not about goals.
        }

        var quiet = result.PlayerLines
            .Where(line => line.Goals == 0 && line.Assists == 0 && line.MinutesPlayed > 0)
            .Select(line => line.LiveRatingBasisPoints)
            .DefaultIfEmpty(EngineRulesV2.MinLiveRatingBasisPoints)
            .Min();

        scorers.Should().OnlyContain(
            line => line.LiveRatingBasisPoints > quiet,
            "a goal moves the live rating more than anything else does");
    }

    // ---- Contract invariants ---------------------------------------------------------------------

    [Fact]
    public void Every_goal_and_set_piece_event_carries_its_pitch_location()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        foreach (var matchEvent in result.Events)
        {
            if (matchEvent.IsGoal
                || matchEvent.Type is EngineEventType.ShotSaved
                    or EngineEventType.ShotBlocked
                    or EngineEventType.ShotOffTarget
                    or EngineEventType.Woodwork
                    or EngineEventType.Corner
                    or EngineEventType.FreeKickWon
                    or EngineEventType.FreeKickShot)
            {
                matchEvent.X.Should().NotBeNull($"{matchEvent.Type} happened somewhere on the pitch");
                matchEvent.Y.Should().NotBeNull();

                matchEvent.X.Should().BeInRange(0, SpatialPitch.PitchLength);
                matchEvent.Y.Should().BeInRange(0, SpatialPitch.PitchWidth);
            }
        }
    }

    [Fact]
    public void A_full_match_stays_inside_the_shorthanded_fatigue_bounds()
    {
        var input = TestMatchFactory.Even();
        var result = MatchSimulator.Simulate(input);

        foreach (var line in result.PlayerLines.Where(line => line.MinutesPlayed > 0))
        {
            line.FinalConditionBasisPoints.Should().BeInRange(0, 10_000);
        }

        // Somebody who played the whole match is more tired than the bench they left behind.
        var starters = result.PlayerLines.Where(line => line.Started).ToList();
        var substitutes = result.PlayerLines.Where(line => !line.Started && line.MinutesPlayed == 0).ToList();

        if (substitutes.Count > 0)
        {
            starters.Select(line => line.FinalConditionBasisPoints).Min()
                .Should().BeLessThanOrEqualTo(
                    substitutes.Select(line => line.FinalConditionBasisPoints).Max(),
                    "the pitch spends condition the bench does not");
        }
    }

    private static ActiveSlot Slot(MatchParticipantV1 participant, MatchPositionFamily family = MatchPositionFamily.Midfield) =>
        new()
        {
            Slot = new MatchSlotV1
            {
                SlotNumber = 6,
                Family = family,
                Role = MatchRole.CentralMidfielder,
                X = 5_000,
                Y = 5_000,
                ParticipantId = participant.ParticipantId,
            },
            Participant = participant,
            FamiliarityBasisPoints = EngineRulesV2.Certain,
            Condition = PlayerCondition.From(participant.State),
        };

    private static DuelContender Contend(MatchParticipantV1 participant) => new(Slot(participant), PlayersDown: 0);

    private static MatchParticipantV1 Participant(
        int otherwise,
        int? dribbling = null,
        int? agility = null,
        int? pace = null,
        int? tackling = null,
        int? positioning = null,
        int? strength = null,
        int? finishing = null,
        int? composure = null,
        int? setPieces = null,
        int? reflexes = null,
        int? jumpingReach = null,
        int? heading = null,
        int? acceleration = null,
        int? workRate = null)
    {
        var values = new int[MatchAttributeNames.Count];
        Array.Fill(values, otherwise);

        values[(int)MatchAttributeName.Dribbling] = dribbling ?? otherwise;
        values[(int)MatchAttributeName.Agility] = agility ?? otherwise;
        values[(int)MatchAttributeName.Pace] = pace ?? otherwise;
        values[(int)MatchAttributeName.Tackling] = tackling ?? otherwise;
        values[(int)MatchAttributeName.Positioning] = positioning ?? otherwise;
        values[(int)MatchAttributeName.Strength] = strength ?? otherwise;
        values[(int)MatchAttributeName.Finishing] = finishing ?? otherwise;
        values[(int)MatchAttributeName.Composure] = composure ?? otherwise;
        values[(int)MatchAttributeName.SetPieces] = setPieces ?? otherwise;
        values[(int)MatchAttributeName.Reflexes] = reflexes ?? otherwise;
        values[(int)MatchAttributeName.JumpingReach] = jumpingReach ?? otherwise;
        values[(int)MatchAttributeName.Heading] = heading ?? otherwise;
        values[(int)MatchAttributeName.Acceleration] = acceleration ?? otherwise;
        values[(int)MatchAttributeName.WorkRate] = workRate ?? otherwise;

        return new MatchParticipantV1
        {
            ParticipantId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            ClubId = Guid.NewGuid(),
            DisplayName = "Test Player",
            ShirtNumber = 1,
            Position = MatchPosition.CentralMidfielder,
            Attributes = PlayerAttributesV1.From(values),
            State = PlayerMatchStateV1.Uniform(8_000),
        };
    }
}
