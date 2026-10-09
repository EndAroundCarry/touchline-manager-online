using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Simulation;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>How a challenge on the ball carrier ended.</summary>
internal enum TickTackleOutcome
{
    /// <summary>A clean tackle: the defender has the ball at his feet.</summary>
    Won = 0,

    /// <summary>The defender poked the ball free of the carrier; it is loose for whoever gets there.</summary>
    PokedLoose = 1,

    /// <summary>The carrier rode the challenge and went past; the defender is left stumbling.</summary>
    Beaten = 2,

    /// <summary>The challenge was a foul: play stops and the carrier's side has a free kick or a penalty.</summary>
    Foul = 3,
}

/// <summary>
/// The tackle and the duel it settles (`tick-engine-v1`, Milestone 3).
/// </summary>
/// <remarks>
/// <para>
/// A presser within <see cref="ContactRadiusUnits"/> of the carrier may challenge — a lunge at a man at arm's length, not
/// only a shoulder-to-shoulder contact — and the further from <see cref="FullContactUnits"/> the lunge starts, the less
/// of it lands. The contest otherwise reuses the possession engine's ground-duel rules, so a tackle here is worth what a
/// duel there was worth:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The defender's score is <c>5 × Tackling + 3 × Strength + 2 × Aggression</c> and the carrier's is
/// <c>5 × Dribbling + 3 × Agility + 2 × Composure</c>, in hundredths of an attribute point. An aggressive tackling style
/// adds <c>AggressiveTacklingDuelScoreBonus</c> to the defender's score; staying on his feet takes
/// <c>StayOnFeetDuelScorePenalty</c> off it, and every pitch unit of reach past <see cref="FullContactUnits"/> takes
/// <see cref="RangePenaltyPerUnitBasisPoints"/> off the chance.
/// </description></item>
/// <item><description>
/// The defender wins the ball with <c>BaseGroundDuelBasisPoints + swing</c>, where the swing scales the score gap against
/// <c>DuelDifferentialReference</c>, banded to <c>DuelMinWinBasisPoints..DuelMaxWinBasisPoints</c>. A win is a clean
/// tackle two times in three and a poke that leaves the ball loose the rest.
/// </description></item>
/// <item><description>
/// A challenge that does not win the ball is a foul with <c>DuelFoulBasisPoints × FoulShareMultiplierBasisPoints</c>
/// (the rules' per-duel share, raised for the tick engine's own fights: a challenge here is already a lunge at the man),
/// scaled by the tackling style and by the same Aggression/Tackling multiplier the possession engine uses, and otherwise
/// the carrier is past him (<see cref="TickTackleOutcome.Beaten"/>).
/// </description></item>
/// </list>
/// <para>
/// The whole contest is decided by <em>one</em> draw in basis points, so a challenge consumes the same amount of the random
/// stream whatever happens. <see cref="Apply"/> carries the result onto the bodies and the ball, and
/// <see cref="PunishFoul"/> hands a foul to <see cref="DisciplineSimulator"/> for the card.
/// </para>
/// </remarks>
internal static class TickTackleResolver
{
    /// <summary>The distance within which a defender may challenge, in pitch units (1.6 m).</summary>
    public const int ContactRadiusUnits = 150;

    /// <summary>The distance at which a challenge is at its full strength, in pitch units (0.9 m).</summary>
    public const int FullContactUnits = 90;

    /// <summary>How much of the win chance each pitch unit of reach past <see cref="FullContactUnits"/> costs, in basis points.</summary>
    public const int RangePenaltyPerUnitBasisPoints = 15;

    /// <summary>The share of the rules' per-duel foul chance a failed challenge carries, in basis points (three and one tenth).</summary>
    public const int FoulShareMultiplierBasisPoints = 31_000;

    /// <summary>The share of winning challenges that leave the ball loose rather than at the defender's feet, in percent.</summary>
    public const int PokedLoosePercent = 33;

    /// <summary>The ticks a beaten defender stumbles, unable to press or challenge (1.2 s).</summary>
    public const int BeatenLockoutTicks = 12;

    /// <summary>The ticks a dispossessed carrier is off balance (0.6 s), so the two do not trade the ball every tick.</summary>
    public const int DispossessedLockoutTicks = 6;

    /// <summary>The speed a poked ball leaves the carrier at, in cm/s.</summary>
    public const int PokeSpeedCentimetresPerSecond = 450;

    private const int DefenderTacklingWeight = 5;
    private const int DefenderStrengthWeight = 3;
    private const int DefenderAggressionWeight = 2;
    private const int CarrierDribblingWeight = 5;
    private const int CarrierAgilityWeight = 3;
    private const int CarrierComposureWeight = 2;

    /// <summary>Gets whether a defender is close enough to the carrier to challenge.</summary>
    /// <param name="defender">The defender.</param>
    /// <param name="carrier">The carrier.</param>
    public static bool InContact(in TickPlayerState defender, in TickPlayerState carrier)
    {
        if (defender.Lockout > 0)
        {
            return false;
        }

        long dx = defender.X - carrier.X;
        long dy = defender.Y - carrier.Y;
        var radius = (long)TickSpatialUnits.ToFixed(ContactRadiusUnits);

        return (dx * dx) + (dy * dy) < radius * radius;
    }

    /// <summary>Gets the chance, in basis points, that a challenge wins the ball.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    public static int WinChanceBasisPoints(in TickPlayerSkills defender, in TickPlayerSkills carrier, MatchTacklingStyle style, EngineRulesV2 rules) =>
        WinChanceBasisPoints(defender, carrier, style, rules, FullContactUnits, EngineRulesV2.Certain);

    /// <summary>Gets the chance, in basis points, that a challenge started a given distance away wins the ball.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    public static int WinChanceBasisPoints(in TickPlayerSkills defender, in TickPlayerSkills carrier, MatchTacklingStyle style, EngineRulesV2 rules, int distanceUnits) =>
        WinChanceBasisPoints(defender, carrier, style, rules, distanceUnits, EngineRulesV2.Certain);

    /// <summary>Gets the chance, in basis points, that a challenge started a given distance away wins the ball.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    /// <param name="advantageBasisPoints">The tackler's side's home advantage, in basis points (10,000 is none): the crowd lifts a home side's tackling.</param>
    public static int WinChanceBasisPoints(in TickPlayerSkills defender, in TickPlayerSkills carrier, MatchTacklingStyle style, EngineRulesV2 rules, int distanceUnits, int advantageBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var tackle = ((DefenderTacklingWeight * defender.Tackling)
            + (DefenderStrengthWeight * defender.Strength)
            + (DefenderAggressionWeight * defender.Aggression)) * EffectiveSkill.Scale;

        tackle += style switch
        {
            MatchTacklingStyle.Aggressive => rules.AggressiveTacklingDuelScoreBonus * EffectiveSkill.Scale,
            MatchTacklingStyle.StayOnFeet => -rules.StayOnFeetDuelScorePenalty * EffectiveSkill.Scale,
            _ => 0,
        };

        var dribble = ((CarrierDribblingWeight * carrier.Dribbling)
            + (CarrierAgilityWeight * carrier.Agility)
            + (CarrierComposureWeight * carrier.Composure)) * EffectiveSkill.Scale;

        var swing = Probability.Swing(
            tackle - dribble,
            rules.GroundDuelSwingBasisPoints,
            rules.DuelDifferentialReference * EffectiveSkill.Scale);

        var reach = Math.Max(0, distanceUnits - FullContactUnits) * RangePenaltyPerUnitBasisPoints;
        var chance = (rules.BaseGroundDuelBasisPoints + swing - reach) * Math.Clamp(advantageBasisPoints, 1, 2 * EngineRulesV2.Certain) / EngineRulesV2.Certain;

        return Probability.Band(chance, rules.DuelMinWinBasisPoints, rules.DuelMaxWinBasisPoints);
    }

    /// <summary>Gets the chance, in basis points, that a challenge which does not win the ball is a foul.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    public static int FoulChanceBasisPoints(in TickPlayerSkills defender, MatchTacklingStyle style, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var styleMultiplier = style switch
        {
            MatchTacklingStyle.Aggressive => rules.AggressiveTacklingDuelFoulMultiplierBasisPoints,
            MatchTacklingStyle.StayOnFeet => rules.StayOnFeetDuelFoulMultiplierBasisPoints,
            _ => EngineRulesV2.Certain,
        };

        var skillMultiplier = DisciplineSimulator.FoulSkillMultiplier(
            defender.Aggression * EffectiveSkill.Scale,
            defender.Tackling * EffectiveSkill.Scale,
            rules);

        return Probability.Band(
            Probability.Apply(
                Probability.Apply(
                    Probability.Apply(rules.DuelFoulBasisPoints, FoulShareMultiplierBasisPoints),
                    styleMultiplier),
                skillMultiplier),
            0,
            EngineRulesV2.Certain);
    }

    /// <summary>Settles a challenge from one draw.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="roll">A draw in basis points, 0..9,999.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        int roll) =>
        Resolve(defender, carrier, style, rules, FullContactUnits, EngineRulesV2.Certain, roll);

    /// <summary>Settles a challenge started a given distance away from one draw.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    /// <param name="roll">A draw in basis points, 0..9,999.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        int distanceUnits,
        int roll) =>
        Resolve(defender, carrier, style, rules, distanceUnits, EngineRulesV2.Certain, roll);

    /// <summary>Settles a challenge started a given distance away from one draw, under a home advantage.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    /// <param name="advantageBasisPoints">The tackler's side's home advantage, in basis points (10,000 is none).</param>
    /// <param name="roll">A draw in basis points, 0..9,999.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        int distanceUnits,
        int advantageBasisPoints,
        int roll)
    {
        var win = WinChanceBasisPoints(defender, carrier, style, rules, distanceUnits, advantageBasisPoints);

        if (roll < win)
        {
            return roll < win * PokedLoosePercent / 100 ? TickTackleOutcome.PokedLoose : TickTackleOutcome.Won;
        }

        // Of the challenges that missed, the foul is the share of them that landed on the man rather than the ball: the
        // roll is rescaled over the failures so a single draw decides everything.
        var failed = (roll - win) * EngineRulesV2.Certain / (EngineRulesV2.Certain - win);

        return failed < FoulChanceBasisPoints(defender, style, rules) ? TickTackleOutcome.Foul : TickTackleOutcome.Beaten;
    }

    /// <summary>Settles a challenge, taking one draw from the stream.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The play stream.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        Pcg32 random) =>
        Resolve(defender, carrier, style, rules, FullContactUnits, EngineRulesV2.Certain, random);

    /// <summary>Settles a challenge started a given distance away, taking one draw from the stream.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    /// <param name="random">The play stream.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        int distanceUnits,
        Pcg32 random) =>
        Resolve(defender, carrier, style, rules, distanceUnits, EngineRulesV2.Certain, random);

    /// <summary>Settles a challenge started a given distance away, taking one draw from the stream.</summary>
    /// <param name="defender">The tackler's skills.</param>
    /// <param name="carrier">The carrier's skills.</param>
    /// <param name="style">The defending side's tackling style.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="distanceUnits">How far the challenge started from the carrier, in pitch units.</param>
    /// <param name="advantageBasisPoints">The tackler's side's home advantage, in basis points (10,000 is none).</param>
    /// <param name="random">The play stream.</param>
    public static TickTackleOutcome Resolve(
        in TickPlayerSkills defender,
        in TickPlayerSkills carrier,
        MatchTacklingStyle style,
        EngineRulesV2 rules,
        int distanceUnits,
        int advantageBasisPoints,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return Resolve(defender, carrier, style, rules, distanceUnits, advantageBasisPoints, random.NextBasisPoints());
    }

    /// <summary>Carries a challenge's result onto the two bodies and the ball.</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Won.</b> The ball is the defender's; the carrier is off balance for <see cref="DispossessedLockoutTicks"/> and loses half his pace.</description></item>
    /// <item><description><b>PokedLoose.</b> The ball runs off from the carrier towards the defender at <see cref="PokeSpeedCentimetresPerSecond"/>; the carrier is off balance as above.</description></item>
    /// <item><description><b>Beaten.</b> The defender loses half his pace and is out of the play for <see cref="BeatenLockoutTicks"/>; the carrier keeps the ball.</description></item>
    /// <item><description><b>Foul.</b> The ball stops where it is, dead; the restart belongs to the match state machine.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="outcome">What <see cref="Resolve(in TickPlayerSkills, in TickPlayerSkills, MatchTacklingStyle, EngineRulesV2, int)"/> decided.</param>
    /// <param name="defender">The tackler, updated in place.</param>
    /// <param name="carrier">The carrier, updated in place.</param>
    /// <param name="defenderIndex">The tackler's index, as the ball knows its controller.</param>
    /// <param name="ball">The ball, updated in place.</param>
    public static void Apply(
        TickTackleOutcome outcome,
        ref TickPlayerState defender,
        ref TickPlayerState carrier,
        int defenderIndex,
        TickBallPhysics ball)
    {
        ArgumentNullException.ThrowIfNull(ball);

        switch (outcome)
        {
            case TickTackleOutcome.Won:
                carrier.Lockout = DispossessedLockoutTicks;
                carrier.Speed /= 2;
                ball.Attach(defenderIndex);
                break;

            case TickTackleOutcome.PokedLoose:
                carrier.Lockout = DispossessedLockoutTicks;
                carrier.Speed /= 2;
                Poke(defender, carrier, ball);
                break;

            case TickTackleOutcome.Beaten:
                defender.Lockout = BeatenLockoutTicks;
                defender.Speed = (int)((long)defender.Speed * TickPlayerPhysics.StumbleSpeedBasisPoints / 10_000);
                break;

            default:
                ball.PlaceAt(ball.UnitX, ball.UnitY);
                carrier.Speed = 0;
                defender.Speed = 0;
                break;
        }
    }

    /// <summary>Puts a foul on the event log and rolls the card it earns, through the possession engine's discipline rules.</summary>
    /// <param name="state">The match state.</param>
    /// <param name="defendingSide">The side that committed the foul.</param>
    /// <param name="foulerId">The tackler who fouled.</param>
    /// <returns>The foul and any card shown.</returns>
    public static DisciplineSimulator.FoulOutcome PunishFoul(MatchState state, MatchSide defendingSide, Guid foulerId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var roll = DisciplineSimulator.RollDuelFoul(state, defendingSide, foulerId);

        return DisciplineSimulator.ApplyFoul(state, defendingSide, roll);
    }

    private static void Poke(in TickPlayerState defender, in TickPlayerState carrier, TickBallPhysics ball)
    {
        var dx = (long)defender.X - carrier.X;
        var dy = (long)defender.Y - carrier.Y;
        var length = SpatialMath.Sqrt((dx * dx) + (dy * dy));
        var speed = TickSpatialUnits.SpeedToFixedPerTick(PokeSpeedCentimetresPerSecond);

        if (length == 0)
        {
            // On top of each other: knocked on the way the defender faces.
            ball.Kick(
                (int)((long)TickTrigonometry.Cos(defender.Heading) * speed / TickTrigonometry.Scale),
                (int)((long)TickTrigonometry.Sin(defender.Heading) * speed / TickTrigonometry.Scale),
                0);

            return;
        }

        ball.Kick((int)(dx * speed / length), (int)(dy * speed / length), 0);
    }
}
