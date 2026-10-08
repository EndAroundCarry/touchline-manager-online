using FluentAssertions;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;
using TouchlineManager.MatchEngine.Tick;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// Verifies the tick engine's player kinematics: speed, acceleration, braking, turning and stamina (Milestone 1).
/// </summary>
public sealed class TickPlayerPhysicsTests
{
    [Fact]
    public void Pace_sets_top_speed_in_metres_per_second_and_acceleration_sets_how_fast_he_gets_there()
    {
        var slow = TickPlayerProfile.From(Attributes(pace: 1));
        var quick = TickPlayerProfile.From(Attributes(pace: 20));

        PerSecond(slow.TopSpeed).Should().BeInRange(5.6, 6.0);
        PerSecond(quick.TopSpeed).Should().BeInRange(9.6, 10.0);

        var sluggish = TickPlayerProfile.From(Attributes(acceleration: 1));
        var explosive = TickPlayerProfile.From(Attributes(acceleration: 20));

        explosive.Acceleration.Should().BeGreaterThan(sluggish.Acceleration);
        TicksToReachTopSpeed(Attributes(acceleration: 20)).Should().BeLessThan(TicksToReachTopSpeed(Attributes(acceleration: 1)));
    }

    [Fact]
    public void A_runner_takes_a_couple_of_seconds_to_reach_top_speed_and_never_exceeds_it()
    {
        var profile = TickPlayerProfile.From(Attributes());
        var player = TickPlayerState.Standing(1_000, 3_500, heading: 0, conditionBasisPoints: 10_000);
        var run = new TickMoveIntent(9_500, 3_500, 10_000, Arrive: false);
        var ticks = 0;

        while (player.Speed < TickPlayerPhysics.EffectiveTopSpeed(player, profile) && ticks < 200)
        {
            TickPlayerPhysics.Step(ref player, profile, run);
            player.Speed.Should().BeLessThanOrEqualTo(profile.TopSpeed);
            ticks++;
        }

        ticks.Should().BeInRange(15, 40, "about two seconds to top speed");
        player.Speed.Should().BeInRange((int)(profile.TopSpeed * 0.99), profile.TopSpeed, "running barely tires him in two seconds");
        player.X.Should().BeGreaterThan(TickSpatialUnits.ToFixed(1_000));
        player.Y.Should().Be(TickSpatialUnits.ToFixed(3_500));
    }

    [Fact]
    public void A_player_told_to_arrive_settles_on_the_point_without_overshooting()
    {
        var profile = TickPlayerProfile.From(Attributes(pace: 15, agility: 12));
        var player = TickPlayerState.Standing(2_000, 3_500, heading: 0, conditionBasisPoints: 10_000);
        var target = new TickMoveIntent(4_000, 3_500, 10_000, Arrive: true);
        var farthest = player.X;

        for (var tick = 0; tick < 300; tick++)
        {
            TickPlayerPhysics.Step(ref player, profile, target);
            farthest = Math.Max(farthest, player.X);
        }

        farthest.Should().BeLessThanOrEqualTo(TickSpatialUnits.ToFixed(4_000), "he brakes in time");
        player.Speed.Should().Be(0);
        player.X.Should().BeInRange(TickSpatialUnits.ToFixed(3_999) - 1, TickSpatialUnits.ToFixed(4_000));
        player.Y.Should().Be(TickSpatialUnits.ToFixed(3_500));
    }

    [Fact]
    public void A_player_running_through_a_point_passes_it_at_pace_before_turning_back()
    {
        var profile = TickPlayerProfile.From(Attributes(pace: 15));
        var player = TickPlayerState.Standing(2_000, 3_500, heading: 0, conditionBasisPoints: 10_000);
        var through = new TickMoveIntent(4_000, 3_500, 10_000, Arrive: false);
        var speedAtThePoint = 0;
        var farthest = 0;

        for (var tick = 0; tick < 120; tick++)
        {
            var before = player.X;
            TickPlayerPhysics.Step(ref player, profile, through);
            farthest = Math.Max(farthest, player.X);

            if (speedAtThePoint == 0 && before < TickSpatialUnits.ToFixed(4_000) && player.X >= TickSpatialUnits.ToFixed(4_000))
            {
                speedAtThePoint = player.Speed;
            }
        }

        speedAtThePoint.Should().BeGreaterThan(profile.TopSpeed * 8 / 10, "he does not brake for a point he was not told to stop on");
        farthest.Should().BeGreaterThan(TickSpatialUnits.ToFixed(4_000));
    }

    [Fact]
    public void An_agile_player_turns_a_right_angle_faster_than_a_clumsy_one()
    {
        TicksToTurnRunningFlatOut(Attributes(agility: 20)).Should().BeLessThan(TicksToTurnRunningFlatOut(Attributes(agility: 1)));
    }

    [Fact]
    public void A_sharp_turn_costs_pace_and_a_standing_player_faces_his_target_at_once()
    {
        var profile = TickPlayerProfile.From(Attributes(pace: 12, agility: 10));
        var standing = TickPlayerState.Standing(5_000, 3_500, heading: 0, conditionBasisPoints: 10_000);

        TickPlayerPhysics.Step(ref standing, profile, new TickMoveIntent(5_000, 6_000, 10_000, Arrive: false));

        standing.Heading.Should().Be(TickTrigonometry.QuarterTurn, "from rest he simply faces where he is going");
        standing.Speed.Should().BeGreaterThan(0);

        var running = Cruising(profile, speedBasisPoints: 10_000);
        var speedBefore = running.Speed;

        // A U-turn: the target is straight behind him.
        TickPlayerPhysics.Step(ref running, profile, new TickMoveIntent(0, running.Y / TickSpatialUnits.FixedScale, 10_000, Arrive: false));

        running.Speed.Should().BeLessThan(speedBefore, "a player facing away from where he is going slows down");
        running.Heading.Should().NotBe(TickTrigonometry.HalfTurn, "he cannot spin on the spot at full tilt");
    }

    [Fact]
    public void Heading_swings_the_short_way_round_and_stays_in_range()
    {
        var profile = TickPlayerProfile.From(Attributes());
        var player = Cruising(profile, speedBasisPoints: 8_000);

        // Running at heading 1,000 (just short of a full turn), told to go to heading ~30: a short right swing.
        player.Heading = 1_000;

        for (var tick = 0; tick < 30; tick++)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(9_000, 3_700, 10_000, Arrive: false));
            player.Heading.Should().BeInRange(0, TickTrigonometry.FullTurn - 1);
        }
    }

    [Fact]
    public void Sprinting_drains_energy_far_faster_than_jogging_and_walking_recovers_it()
    {
        var profile = TickPlayerProfile.From(Attributes());
        var sprinter = TickPlayerState.Standing(100, 3_500, 0, 10_000);
        var jogger = TickPlayerState.Standing(100, 3_500, 0, 10_000);
        var walker = TickPlayerState.Standing(100, 3_500, 0, 5_000);

        for (var tick = 0; tick < 600; tick++)
        {
            TickPlayerPhysics.Step(ref sprinter, profile, new TickMoveIntent(9_900, 3_500, 10_000, Arrive: false));
            TickPlayerPhysics.Step(ref jogger, profile, new TickMoveIntent(9_900, 3_500, 5_000, Arrive: false));
            TickPlayerPhysics.Step(ref walker, profile, new TickMoveIntent(walker.X / TickSpatialUnits.FixedScale, 3_500, 0, Arrive: true));
        }

        var sprintLoss = TickPlayerPhysics.EnergyFull - sprinter.Energy;
        var jogLoss = TickPlayerPhysics.EnergyFull - jogger.Energy;

        sprintLoss.Should().BeGreaterThan(jogLoss * 3);
        jogger.Energy.Should().BeLessThan(TickPlayerPhysics.EnergyFull);
        walker.Energy.Should().BeGreaterThan(5_000 * (TickPlayerPhysics.EnergyFull / 10_000), "standing around recovers a little");
    }

    [Fact]
    public void Stamina_slows_the_drain_and_work_rate_speeds_it()
    {
        LossOverFiveMinutes(Attributes(stamina: 20)).Should().BeLessThan(LossOverFiveMinutes(Attributes(stamina: 1)));
        LossOverFiveMinutes(Attributes(workRate: 20)).Should().BeGreaterThan(LossOverFiveMinutes(Attributes(workRate: 1)));
    }

    [Fact]
    public void A_tired_player_is_slower_but_never_stops()
    {
        var profile = TickPlayerProfile.From(Attributes());
        var fresh = TickPlayerState.Standing(100, 3_500, 0, 10_000);
        var spent = TickPlayerState.Standing(100, 3_500, 0, 0);

        TickPlayerPhysics.EffectiveTopSpeed(spent, profile).Should().BeLessThan(TickPlayerPhysics.EffectiveTopSpeed(fresh, profile));
        TickPlayerPhysics.EffectiveTopSpeed(spent, profile).Should().BeGreaterThan(profile.TopSpeed * 6 / 10);
        TickPlayerPhysics.EffectiveTopSpeed(fresh, profile).Should().Be(profile.TopSpeed);
    }

    [Fact]
    public void An_average_player_ends_ninety_minutes_of_mixed_running_a_quarter_down_not_empty()
    {
        var profile = TickPlayerProfile.From(Attributes());
        var player = TickPlayerState.Standing(5_000, 3_500, 0, 10_000);

        // A crude mix: eleven seconds standing or walking, four jogging, one flat-out burst, repeated.
        for (var second = 0; second < 90 * 60; second++)
        {
            var phase = second % 16;
            var forward = (second / 16) % 2 == 0;
            var targetX = forward ? 8_000 : 2_000;
            var limit = phase switch
            {
                < 11 => 1_500,
                < 15 => 5_500,
                _ => 10_000,
            };

            for (var tick = 0; tick < TickSpatialUnits.TicksPerSecond; tick++)
            {
                TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(targetX, 3_500, limit, Arrive: false));
            }
        }

        var loss = (TickPlayerPhysics.EnergyFull - player.Energy) * 100L / TickPlayerPhysics.EnergyFull;

        loss.Should().BeInRange(8, 55, "tiring, but a full match is playable");
    }

    [Fact]
    public void A_player_stays_on_the_pitch()
    {
        var profile = TickPlayerProfile.From(Attributes(pace: 20));
        var player = TickPlayerState.Standing(100, 100, 0, 10_000);

        for (var tick = 0; tick < 400; tick++)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(-3_000, -3_000, 10_000, Arrive: false));
        }

        player.X.Should().BeInRange(0, TickSpatialUnits.PitchLengthFixed);
        player.Y.Should().BeInRange(0, TickSpatialUnits.PitchWidthFixed);
    }

    [Fact]
    public void Player_physics_is_deterministic_and_allocates_nothing_in_the_tick_loop()
    {
        var profiles = new TickPlayerProfile[22];
        var players = new TickPlayerState[22];

        static long Run(TickPlayerProfile[] profiles, TickPlayerState[] players)
        {
            long hash = 17;

            for (var index = 0; index < players.Length; index++)
            {
                players[index] = TickPlayerState.Standing(500 + (index * 400), 300 + (index * 290), index * 40, 10_000);
            }

            for (var tick = 0; tick < 54_000; tick++)
            {
                for (var index = 0; index < players.Length; index++)
                {
                    var intent = new TickMoveIntent(
                        500 + (((tick / 70) + (index * 7)) * 613 % 9_000),
                        400 + (((tick / 90) + (index * 11)) * 487 % 6_200),
                        2_000 + ((index * 400) % 8_000),
                        Arrive: (index & 1) == 0);

                    TickPlayerPhysics.Step(ref players[index], profiles[index], intent);
                    hash = unchecked((hash * 31) + players[index].X);
                    hash = unchecked((hash * 31) + players[index].Y);
                    hash = unchecked((hash * 31) + players[index].Heading);
                    hash = unchecked((hash * 31) + players[index].Energy);
                }
            }

            return hash;
        }

        for (var index = 0; index < profiles.Length; index++)
        {
            profiles[index] = TickPlayerProfile.From(Attributes(pace: 1 + (index % 20), agility: 1 + ((index * 3) % 20)));
        }

        var first = Run(profiles, players);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var second = Run(profiles, players);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.Should().Be(first);
        allocated.Should().BeLessThan(256, "22 players for 54,000 ticks must not allocate");
    }

    private static double PerSecond(int fixedPerTick) =>
        fixedPerTick * (double)TickSpatialUnits.TicksPerSecond / TickSpatialUnits.FixedScale / 95.238;

    private static int TicksToReachTopSpeed(PlayerAttributesV1 attributes)
    {
        var profile = TickPlayerProfile.From(attributes);
        var player = TickPlayerState.Standing(1_000, 3_500, 0, 10_000);
        var ticks = 0;

        while (player.Speed < TickPlayerPhysics.EffectiveTopSpeed(player, profile) && ticks < 500)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(9_500, 3_500, 10_000, Arrive: false));
            ticks++;
        }

        return ticks;
    }

    private static TickPlayerState Cruising(TickPlayerProfile profile, int speedBasisPoints)
    {
        var player = TickPlayerState.Standing(2_000, 3_500, 0, 10_000);

        for (var tick = 0;
            tick < 500 && player.Speed < (long)TickPlayerPhysics.EffectiveTopSpeed(player, profile) * speedBasisPoints / 10_000;
            tick++)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(9_500, 3_500, speedBasisPoints, Arrive: false));
        }

        return player;
    }

    private static int TicksToTurnRunningFlatOut(PlayerAttributesV1 attributes)
    {
        var profile = TickPlayerProfile.From(attributes);
        var player = Cruising(profile, speedBasisPoints: 9_000);

        // Running along +X, he must come to run along +Y.
        var ticks = 0;

        while (player.Heading < TickTrigonometry.QuarterTurn - 4 && ticks < 200)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(player.X / TickSpatialUnits.FixedScale, 6_500, 9_000, Arrive: false));
            ticks++;
        }

        return ticks;
    }

    private static int LossOverFiveMinutes(PlayerAttributesV1 attributes)
    {
        var profile = TickPlayerProfile.From(attributes);
        var player = TickPlayerState.Standing(100, 3_500, 0, 10_000);

        for (var tick = 0; tick < 3_000; tick++)
        {
            TickPlayerPhysics.Step(ref player, profile, new TickMoveIntent(9_900, 3_500, 6_000, Arrive: false));
        }

        return TickPlayerPhysics.EnergyFull - player.Energy;
    }

    private static PlayerAttributesV1 Attributes(
        int pace = 10,
        int acceleration = 10,
        int agility = 10,
        int stamina = 10,
        int workRate = 10)
    {
        var values = Enumerable.Repeat(10, MatchAttributeNames.Count).ToArray();
        values[(int)MatchAttributeName.Pace] = pace;
        values[(int)MatchAttributeName.Acceleration] = acceleration;
        values[(int)MatchAttributeName.Agility] = agility;
        values[(int)MatchAttributeName.Stamina] = stamina;
        values[(int)MatchAttributeName.WorkRate] = workRate;

        return PlayerAttributesV1.From(values);
    }
}
