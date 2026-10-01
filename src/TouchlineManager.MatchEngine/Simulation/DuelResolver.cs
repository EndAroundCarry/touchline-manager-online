using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// One contested 1v1 contest between an attacker and a defender.
/// </summary>
/// <param name="AttackerWon">Whether the attacking side kept the ball.</param>
/// <param name="WasFoul">Whether the defender conceded a foul in the attempt.</param>
/// <param name="AttackerId">The player who tried to keep the ball.</param>
/// <param name="DefenderId">The player who tried to take it.</param>
/// <remarks>
/// A foul is not resolved here: it is routed back through the discipline flow, which owns bookings, sendings-off,
/// and what the foul gives the attacking side. A contest that says "foul" therefore costs the same draws wherever
/// it happens, which is what keeps the random stream's shape part of the engine version.
/// </remarks>
public readonly record struct DuelOutcome(
    bool AttackerWon,
    bool WasFoul,
    Guid AttackerId,
    Guid DefenderId);

/// <summary>
/// Resolves the on-pitch contests of a possession: 1v1 ground duels, aerial duels, and loose-ball
/// scrambles (master plan Stage 2).
/// </summary>
/// <remarks>
/// <para>
/// Every contest is the same shape: a baseline chance, moved by a bounded attribute differential on the
/// attribute scale, nudged by home advantage, rolled inside a small underdog-variance band. The shape is
/// written once here, so the duel constants in <see cref="EngineRulesV2"/> mean the same thing wherever a
/// contest is resolved.
/// </para>
/// <para>
/// Attributes are read on the attribute scale (1–20) rather than the unit-rating scale, which is what
/// lets the swing constants be calibrated directly: a swing of 3,500 basis points is "a maximal
/// differential at the reference distance moves the contest this far".
/// </para>
/// </remarks>
public static class DuelResolver
{
    /// <summary>Resolves a 1v1 ground duel: a dribble against a tackle.</summary>
    /// <param name="attacker">The player trying to keep the ball.</param>
    /// <param name="defender">The player trying to take it.</param>
    /// <param name="attackerIsHome">Whether the attacking player's side is at home.</param>
    /// <param name="tacklingStyle">The defending side's tackling instruction.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    public static DuelOutcome ResolveGroundDuel(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        MatchTacklingStyle tacklingStyle,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var dribble = AttackScore(
            attacker,
            isHome: attackerIsHome,
            rules,
            (MatchAttributeName.Dribbling, 4),
            (MatchAttributeName.Agility, 3),
            (MatchAttributeName.Pace, 3));

        var tackle = DefendScore(
            defender,
            attackerIsHome,
            rules,
            (MatchAttributeName.Tackling, 4),
            (MatchAttributeName.Positioning, 3),
            (MatchAttributeName.Strength, 3));

        // Tackling style is a trade the defending side has already chosen: committing to the challenge
        // wins more ball and gives away more fouls; staying on feet does the reverse.
        if (tacklingStyle == MatchTacklingStyle.Aggressive)
        {
            tackle += 6;
        }
        else if (tacklingStyle == MatchTacklingStyle.StayOnFeet)
        {
            tackle -= 4;
        }

        var winChance = ContestChance(dribble - tackle, rules.BaseGroundDuelBasisPoints, rules.GroundDuelSwingBasisPoints, rules, random);

        var attackerWon = random.RollBasisPoints(winChance);

        var wasFoul = false;

        if (!attackerWon)
        {
            var foulChance = rules.DuelFoulBasisPoints;

            foulChance = tacklingStyle switch
            {
                MatchTacklingStyle.Aggressive => Probability.Apply(foulChance, rules.AggressiveTacklingDuelFoulMultiplierBasisPoints),
                MatchTacklingStyle.StayOnFeet => Probability.Apply(foulChance, rules.StayOnFeetDuelFoulMultiplierBasisPoints),
                _ => foulChance,
            };

            wasFoul = random.RollBasisPoints(foulChance);
        }

        return new DuelOutcome(attackerWon, wasFoul, attacker.ParticipantId, defender.ParticipantId);
    }

    /// <summary>Resolves an aerial contest: a jump against a jump.</summary>
    /// <param name="attacker">The attacking player.</param>
    /// <param name="defender">The defending player.</param>
    /// <param name="attackerIsHome">Whether the attacking player's side is at home.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    public static DuelOutcome ResolveAerialDuel(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var attack = AttackScore(
            attacker,
            attackerIsHome,
            rules,
            (MatchAttributeName.JumpingReach, 5),
            (MatchAttributeName.Heading, 3),
            (MatchAttributeName.Strength, 2));

        var defence = DefendScore(
            defender,
            attackerIsHome,
            rules,
            (MatchAttributeName.JumpingReach, 5),
            (MatchAttributeName.Heading, 3),
            (MatchAttributeName.Strength, 2));

        var winChance = ContestChance(attack - defence, rules.BaseAerialDuelBasisPoints, rules.AerialDuelSwingBasisPoints, rules, random);

        return new DuelOutcome(
            random.RollBasisPoints(winChance),
            WasFoul: false,
            attacker.ParticipantId,
            defender.ParticipantId);
    }

    /// <summary>
    /// Resolves a loose-ball scramble: which side collects a ball neither side controls.
    /// </summary>
    /// <remarks>
    /// Weighted by pace, acceleration, and work rate — the attributes that decide who arrives first — and
    /// settled with a single roll, because a scramble has no ball-carrier to foul.
    /// </remarks>
    /// <param name="attacker">A player from the side in possession.</param>
    /// <param name="defender">A player from the side out of possession.</param>
    /// <param name="attackerIsHome">Whether the possession side is at home.</param>
    /// <param name="rules">The rules in force.</param>
    /// <param name="random">The generator.</param>
    /// <returns>Whether the possession side kept the ball.</returns>
    public static bool ResolveScramble(
        MatchParticipantV1 attacker,
        MatchParticipantV1 defender,
        bool attackerIsHome,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        var attack = AttackScore(
            attacker,
            attackerIsHome,
            rules,
            (MatchAttributeName.Pace, 3),
            (MatchAttributeName.Acceleration, 3),
            (MatchAttributeName.WorkRate, 2));

        var defence = DefendScore(
            defender,
            attackerIsHome,
            rules,
            (MatchAttributeName.Pace, 3),
            (MatchAttributeName.Acceleration, 3),
            (MatchAttributeName.WorkRate, 2));

        var keepChance = ContestChance(
            attack - defence,
            rules.BaseScrambleBasisPoints,
            rules.ScrambleSwingBasisPoints,
            rules,
            random);

        return random.RollBasisPoints(keepChance);
    }

    /// <summary>Builds the attacking side of a contest and applies home advantage to it.</summary>
    private static int AttackScore(
        MatchParticipantV1 player,
        bool isHome,
        EngineRulesV2 rules,
        params (MatchAttributeName Name, int Weight)[] attributes)
    {
        var score = 0;

        foreach (var (name, weight) in attributes)
        {
            score += player.Attributes.ValueOf(name) * weight;
        }

        if (isHome)
        {
            // The crowd bonus is the plan's +4% duel success, applied as a bounded lift on the score rather
            // than as a second probability, so it composes with the swing instead of fighting it.
            score = Probability.Apply(score, EngineRulesV2.Certain + rules.DuelHomeBonusBasisPoints);
        }

        return score;
    }

    /// <summary>Builds the defending side of a contest, applying home advantage when the defender is at home.</summary>
    private static int DefendScore(
        MatchParticipantV1 player,
        bool attackerIsHome,
        EngineRulesV2 rules,
        params (MatchAttributeName Name, int Weight)[] attributes)
    {
        return AttackScore(player, isHome: !attackerIsHome, rules, attributes);
    }

    /// <summary>
    /// Converts a differential into a contest probability: baseline, swung by the attribute differential,
    /// jittered by the underdog band, and clamped so no contest is certain either way.
    /// </summary>
    /// <remarks>
    /// The jitter is the plan's controlled stochastic variance: each contest's chance is moved by up to
    /// half the variance in either direction before it is rolled, so a favourite still loses the odd
    /// contest while attributes decide the run of them. The jitter is a draw from the play stream like any
    /// other, in a fixed order, so a result stays reproducible.
    /// </remarks>
    private static int ContestChance(
        int differential,
        int baselineBasisPoints,
        int swingBasisPoints,
        EngineRulesV2 rules,
        Pcg32 random)
    {
        var swing = Probability.Swing(differential, swingBasisPoints, rules.DuelDifferentialReference);
        var jitter = random.NextInt(rules.UnderdogVarianceBasisPoints + 1) - (rules.UnderdogVarianceBasisPoints / 2);

        return Probability.Band(baselineBasisPoints + swing + jitter, 1_500, 8_500);
    }
}
