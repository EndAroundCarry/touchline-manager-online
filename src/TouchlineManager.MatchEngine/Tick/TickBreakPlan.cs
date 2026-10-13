using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// How big a counter-attack's break is: how long it lasts, how many runners it sends and whether they start at once
/// (`weaker-side-v1`, Milestone 1b).
/// </summary>
/// <param name="Ticks">How long the break lasts from the regain, in ticks.</param>
/// <param name="Runners">The fewest players it sends running behind the line, or 0 for the mentality's own number.</param>
/// <param name="Immediate">Whether the runners start from the first tick, without waiting for a free carrier.</param>
/// <param name="ProgressPercent">The percent of progress the carrier values a ball forward at, on top of his mentality's.</param>
internal readonly record struct TickBreakPlan(int Ticks, int Runners, bool Immediate, int ProgressPercent);

/// <summary>
/// What a counter-attack is worth against this opponent (`weaker-side-v1`, Milestone 1b): a fixed cost, and a gain that grows with the
/// opponent's posture and with how much quicker the breakers are than the men who must get back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cost is the same against everyone.</b> A side that counter-attacks does not force the play while it waits for a break: its
/// carrier values going forward <see cref="TickBallCarrierBrain.WaitProgressPercent"/> points less (<see cref="TickCarrierSituation.Waiting"/>).
/// Against a side that attacks, the waiting is made up for by the breaks; against one that sits deep it is not, and the counter is a net minus.
/// </para>
/// <para>
/// <b>The gain is a percentage.</b> <see cref="GainAtBalancedPercent"/> at posture 0 plus <see cref="GainPerPosturePercent"/> for every point
/// of the opponent's posture (<see cref="TickTeamStyle.Posture"/>), times the speed factor (<see cref="SpeedFactorPercent"/>). It sets the
/// window's length (<see cref="TicksPerGainPercent"/>), the runners (one for every <see cref="GainPerRunnerPercent"/>, up to
/// <see cref="MaximumRunners"/>), whether they start at once (from <see cref="ImmediateGainPercent"/>) and how much more the carrier values a
/// ball forward (<see cref="TickBallCarrierBrain.BreakProgressPercent"/> at 100%, up to <see cref="MaximumProgressGainPercent"/>). The progress follows the speed factor twice over: a break that a quicker side can catch loses its hurry faster than it loses its length, which is what turns a counter against an attacking side from a plus into a small minus at a 2-point edge.
/// </para>
/// <para>
/// <b>Speed.</b> The breakers are the side's Attack and Midfield families, the recoverers the opponent's Defence and Midfield families; a
/// group's speed is the mean of <c>(Pace + Acceleration) / 2</c> over the men on the pitch, scaled by their condition as the physics
/// scales their top speed. The edge is the recoverers' minus the breakers', in hundredths of an attribute point, and the factor moves
/// <see cref="GainPerSpeedPointPercent"/> for every point of it: a relative measure, 13 against 15 counts as 18 against 20.
/// </para>
/// </remarks>
internal static class TickBreakGain
{
    /// <summary>The gain against a balanced opponent (posture 0), in percent.</summary>
    public const int GainAtBalancedPercent = 100;

    /// <summary>The gain added for every point of the opponent's posture, in percent.</summary>
    public const int GainPerPosturePercent = 50;

    /// <summary>The ticks a break lasts for every 100% of gain (7 s).</summary>
    public const int TicksPerGainPercent = 70;

    /// <summary>The gain, in percent, that buys one runner.</summary>
    public const int GainPerRunnerPercent = 50;

    /// <summary>The most runners a break sends.</summary>
    public const int MaximumRunners = 3;

    /// <summary>The gain, in percent, from which the runners start at once.</summary>
    public const int ImmediateGainPercent = 75;

    /// <summary>The most gain, in percent, the carrier's hunger for progress grows with.</summary>
    public const int MaximumProgressGainPercent = 100;

    /// <summary>The ticks a Defensive side's outlet lasts from the regain (5 s).</summary>
    public const int OutletTicks = 50;

    /// <summary>The runners a Defensive side's outlet sends.</summary>
    public const int OutletRunners = 1;

    /// <summary>The percent of progress a Defensive side's carrier values a ball forward at while its outlet is open: a mild lift on its mentality's 90.</summary>
    public const int OutletProgressPercent = 15;

    /// <summary>The percent the gain moves for every point the recoverers are quicker (down) or slower (up) than the breakers.</summary>
    public const int GainPerSpeedPointPercent = 15;

    /// <summary>The least the speed factor goes to, in percent.</summary>
    public const int SpeedFloorPercent = 30;

    /// <summary>The most the speed factor goes to, in percent.</summary>
    public const int SpeedCeilingPercent = 115;

    /// <summary>Gets the break's speed factor, in percent, for an edge.</summary>
    /// <param name="edgeHundredths">The recoverers' speed less the breakers', in hundredths of an attribute point.</param>
    /// <returns>100 for no edge; lower when the opponent is quicker, higher when the breakers are, within the floor and the ceiling.</returns>
    public static int SpeedFactorPercent(int edgeHundredths) =>
        Math.Clamp(100 - (edgeHundredths * GainPerSpeedPointPercent / 100), SpeedFloorPercent, SpeedCeilingPercent);

    private static readonly int SpeedUnit = TickSpatialUnits.SpeedToFixedPerTick(100_000);

    private static readonly int AccelerationUnit = TickSpatialUnits.AccelerationToFixedPerTickSquared(100_000);

    /// <summary>Gets the speed of a group of a side's men on the pitch, in hundredths of an attribute point.</summary>
    /// <param name="team">The side.</param>
    /// <param name="first">The first family in the group.</param>
    /// <param name="second">The second family in the group.</param>
    /// <returns>The mean of <c>(Pace + Acceleration) / 2</c> scaled by condition, or 0 for nobody.</returns>
    public static int GroupSpeed(TickTeam team, MatchPositionFamily first, MatchPositionFamily second)
    {
        ArgumentNullException.ThrowIfNull(team);

        long sum = 0;
        var count = 0;

        for (var seat = 0; seat < team.Count; seat++)
        {
            var family = team.Spec[seat].Family;

            if (family != first && family != second)
            {
                continue;
            }

            // The raw Pace and Acceleration, read back from the profile the physics runs on: the skills are pulled towards the average by the skill curve.
            var profile = team.Profile[seat];
            var topSpeed = profile.TopSpeed * 100_000L / SpeedUnit;
            var acceleration = profile.Acceleration * 100_000L / AccelerationUnit;
            var pace = ((topSpeed * 100) - (TickPlayerPhysics.BaseSpeedCentimetresPerSecond * 100L)) / TickPlayerPhysics.PaceWeightCentimetresPerSecond;
            var accel = ((acceleration * 100) - (TickPlayerPhysics.BaseAccelerationCentimetresPerSecondSquared * 100L)) / TickPlayerPhysics.AccelerationWeight;
            var fresh = 7_000 + (3_000L * team.Body[seat].Energy / TickPlayerPhysics.EnergyFull);

            sum += (pace + accel) / 2 * fresh / 10_000;
            count++;
        }

        return count == 0 ? 0 : (int)(sum / count);
    }

    /// <summary>
    /// Gets the break with a Defensive side's outlet added (`weaker-side-v1`, Milestone 2): a side that sits deep with the attack switched off
    /// still has one man to play a ball won in its own half to. It is the larger of the counter's break and the outlet, so a counter against a
    /// deep side, worth nothing of its own, keeps the outlet.
    /// </summary>
    /// <param name="plan">The break the counter-attack opens, or the default for a side without the flag.</param>
    public static TickBreakPlan WithOutlet(TickBreakPlan plan) =>
        new(
            Math.Max(plan.Ticks, OutletTicks),
            Math.Max(plan.Runners, OutletRunners),
            true,
            Math.Max(plan.ProgressPercent, OutletProgressPercent));

    /// <summary>Gets the break a regain opens.</summary>
    /// <param name="posture">The opponent's posture, -4..+4.</param>
    /// <param name="speedFactorPercent">The speed factor of the two groups.</param>
    public static TickBreakPlan Plan(int posture, int speedFactorPercent)
    {
        var posted = Math.Max(0, GainAtBalancedPercent + (GainPerPosturePercent * posture));
        var gain = posted * speedFactorPercent / 100;

        return new TickBreakPlan(
            TicksPerGainPercent * gain / 100,
            Math.Min(MaximumRunners, (gain + (GainPerRunnerPercent / 2)) / GainPerRunnerPercent),
            gain >= ImmediateGainPercent,
            TickBallCarrierBrain.BreakProgressPercent * Math.Min(posted, MaximumProgressGainPercent) * speedFactorPercent * speedFactorPercent / 1_000_000);
    }
}
