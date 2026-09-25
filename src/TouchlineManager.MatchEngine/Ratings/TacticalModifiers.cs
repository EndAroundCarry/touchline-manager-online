using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// Turns the eight team instructions into a bounded multiplier on each unit rating (`INS-1`…`INS-9`).
/// </summary>
/// <remarks>
/// <para>
/// Every instruction has a cost as well as a benefit, and this is where that is enforced — not just
/// asserted. Attacking commits players forward, which buys creation and finishing and sells defensive shape.
/// A high press wins the ball higher, which buys pressure and chances and costs fitness. A low tempo keeps a
/// side fresh and stops it creating. A side cannot pick up a free multiplier by choosing the aggressive
/// column, because the column is not free.
/// </para>
/// <para>
/// Two bounds keep attributes dominant (master plan §8.4, `INS-9`). Each unit's modifier is clamped to
/// <c>MinTacticalModifierBasisPoints..MaxTacticalModifierBasisPoints</c> — around ±15% at the extremes for
/// the whole instruction set — and the modifiers are deliberately partial: no combination of choices
/// changes a unit by more than a fifth, which is worth roughly three attribute points. A well-drilled
/// side beats a better-coached one, but not a much better one.
/// </para>
/// </remarks>
public static class TacticalModifiers
{
    /// <summary>The version label of this modifier table, versioned with the engine.</summary>
    public const string Version = "engine-tactical-v1";

    /// <summary>Computes one unit's modifier for a side's instructions.</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="instructions">The team instructions.</param>
    /// <param name="rules">The rules in force, which supply the bounds.</param>
    /// <returns>The multiplier in basis points, clamped to the rules' bounds.</returns>
    public static int For(MatchUnit unit, MatchInstructionsV1 instructions, EngineRulesV1 rules)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(rules);

        var modifier = EngineRulesV1.Certain + Deltas(unit, instructions);

        return int.Clamp(
            modifier,
            rules.MinTacticalModifierBasisPoints,
            rules.MaxTacticalModifierBasisPoints);
    }

    private static int Deltas(MatchUnit unit, MatchInstructionsV1 instructions) => unit switch
    {
        MatchUnit.BuildUp =>
            Passing(instructions.Passing, shortPassing: 150, direct: -200)
            + Tempo(instructions.Tempo, low: 200, high: -250)
            + Line(instructions.DefensiveLine, deep: -120, high: 120)
            + Width(instructions.Width, narrow: -80, wide: 80)
            + Mentality(instructions.Mentality, defensive: -150, attacking: 100, positive: 50),

        MatchUnit.Creation =>
            Mentality(instructions.Mentality, defensive: -350, cautious: -180, positive: 250, attacking: 500)
            + Tempo(instructions.Tempo, low: -200, high: 300)
            + Passing(instructions.Passing, shortPassing: 120, direct: 80)
            + Width(instructions.Width, narrow: -120, wide: 150)
            + Pressing(instructions.Pressing, lowBlock: -100, highPress: 100)
            + TimeWasting(instructions.TimeWasting, situational: -120, on: -350),

        MatchUnit.Finishing =>
            Mentality(instructions.Mentality, defensive: -250, cautious: -120, positive: 180, attacking: 350)
            + Tempo(instructions.Tempo, low: -100, high: 100)
            + TimeWasting(instructions.TimeWasting, situational: -80, on: -200),

        MatchUnit.DefensivePressure =>
            Pressing(instructions.Pressing, lowBlock: -300, highPress: 400)
            + Tackling(instructions.Tackling, stayOnFeet: -150, aggressive: 250)
            + Mentality(instructions.Mentality, defensive: 200, cautious: 120, positive: -150, attacking: -250)
            + Tempo(instructions.Tempo, low: -100, high: 100)
            + Line(instructions.DefensiveLine, deep: -100, high: 150),

        MatchUnit.DefensiveShape =>
            Line(instructions.DefensiveLine, deep: 250, high: -250)
            + Mentality(instructions.Mentality, defensive: 300, cautious: 150, positive: -150, attacking: -400)
            + Width(instructions.Width, narrow: 150, wide: -100)
            + Tackling(instructions.Tackling, stayOnFeet: 120, aggressive: -200)
            + Pressing(instructions.Pressing, lowBlock: 180, highPress: -200)
            + TimeWasting(instructions.TimeWasting, situational: 80, on: 150),

        // Stopping shots is the goalkeeper's own business; no team instruction changes it.
        MatchUnit.Goalkeeping => 0,

        MatchUnit.SetPieces =>
            Width(instructions.Width, narrow: -60, wide: 100)
            + Mentality(instructions.Mentality, attacking: 100, defensive: -60),

        // Freshness is bought directly: an instruction that covers more ground costs condition.
        MatchUnit.Fitness =>
            Tempo(instructions.Tempo, low: 250, high: -300)
            + Pressing(instructions.Pressing, lowBlock: 250, highPress: -300)
            + Mentality(instructions.Mentality, defensive: 100, attacking: -150),

        // Cohesion is about how well the eleven fit their jobs, which no instruction changes.
        MatchUnit.Cohesion => 0,

        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown unit."),
    };

    private static int Mentality(
        MatchMentality value,
        int defensive = 0,
        int cautious = 0,
        int positive = 0,
        int attacking = 0) => value switch
        {
            MatchMentality.Defensive => defensive,
            MatchMentality.Cautious => cautious,
            MatchMentality.Balanced => 0,
            MatchMentality.Positive => positive,
            MatchMentality.Attacking => attacking,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown mentality."),
        };

    private static int Tempo(MatchTempo value, int low, int high) => value switch
    {
        MatchTempo.Low => low,
        MatchTempo.Normal => 0,
        MatchTempo.High => high,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown tempo."),
    };

    private static int Passing(MatchPassingStyle value, int shortPassing, int direct) => value switch
    {
        MatchPassingStyle.ShortPassing => shortPassing,
        MatchPassingStyle.MixedPassing => 0,
        MatchPassingStyle.DirectPassing => direct,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown passing style."),
    };

    private static int Width(MatchWidth value, int narrow, int wide) => value switch
    {
        MatchWidth.Narrow => narrow,
        MatchWidth.Normal => 0,
        MatchWidth.Wide => wide,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown width."),
    };

    private static int Pressing(MatchPressing value, int lowBlock, int highPress) => value switch
    {
        MatchPressing.LowBlock => lowBlock,
        MatchPressing.MidBlock => 0,
        MatchPressing.HighPress => highPress,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown pressing scheme."),
    };

    private static int Line(MatchDefensiveLine value, int deep, int high) => value switch
    {
        MatchDefensiveLine.Deep => deep,
        MatchDefensiveLine.Normal => 0,
        MatchDefensiveLine.High => high,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown defensive line."),
    };

    private static int Tackling(MatchTacklingStyle value, int stayOnFeet, int aggressive) => value switch
    {
        MatchTacklingStyle.StayOnFeet => stayOnFeet,
        MatchTacklingStyle.Normal => 0,
        MatchTacklingStyle.Aggressive => aggressive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown tackling style."),
    };

    private static int TimeWasting(MatchTimeWasting value, int situational, int on) => value switch
    {
        MatchTimeWasting.Off => 0,
        MatchTimeWasting.Situational => situational,
        MatchTimeWasting.On => on,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown time-wasting setting."),
    };
}
