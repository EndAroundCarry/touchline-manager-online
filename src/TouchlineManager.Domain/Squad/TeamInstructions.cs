namespace TouchlineManager.Domain.Squad;

/// <summary>The team's overall approach (`INS-1`).</summary>
public enum Mentality
{
    /// <summary>Sit deep and protect.</summary>
    Defensive = 0,

    /// <summary>Stay compact and take few risks.</summary>
    Cautious = 1,

    /// <summary>Neither instruct nor inhibit.</summary>
    Balanced = 2,

    /// <summary>Commit players forward.</summary>
    Positive = 3,

    /// <summary>Chase the game.</summary>
    Attacking = 4,
}

/// <summary>How quickly the team moves the ball (`INS-2`).</summary>
public enum Tempo
{
    /// <summary>Slow and deliberate.</summary>
    Low = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Fast and direct in transition.</summary>
    High = 2,
}

/// <summary>What kind of pass the team favours (`INS-3`).</summary>
public enum PassingStyle
{
    /// <summary>Short passes into feet.</summary>
    ShortPassing = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    MixedPassing = 1,

    /// <summary>Long, forward passes.</summary>
    DirectPassing = 2,
}

/// <summary>How far the team spreads across the pitch (`INS-4`).</summary>
public enum Width
{
    /// <summary>Cramped, congesting the middle.</summary>
    Narrow = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Stretched, using the full pitch.</summary>
    Wide = 2,
}

/// <summary>From where the team begins to press (`INS-5`).</summary>
public enum Pressing
{
    /// <summary>Defend from a low block.</summary>
    LowBlock = 0,

    /// <summary>Defend from a mid block.</summary>
    MidBlock = 1,

    /// <summary>Press high up the pitch.</summary>
    HighPress = 2,
}

/// <summary>How high the defensive line holds (`INS-6`).</summary>
public enum DefensiveLine
{
    /// <summary>Drop off.</summary>
    Deep = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Hold a high line.</summary>
    High = 2,
}

/// <summary>How committed the tackling is (`INS-7`).</summary>
public enum TacklingStyle
{
    /// <summary>Jockey and delay.</summary>
    StayOnFeet = 0,

    /// <summary>Neither instruct nor inhibit.</summary>
    Normal = 1,

    /// <summary>Commit to the challenge, risking cards.</summary>
    Aggressive = 2,
}

/// <summary>Whether the team runs down the clock (`INS-8`).</summary>
public enum TimeWasting
{
    /// <summary>Never.</summary>
    Off = 0,

    /// <summary>Only when leading late.</summary>
    Situational = 1,

    /// <summary>Whenever possible.</summary>
    On = 2,
}

/// <summary>
/// The eight team-level settings a tactical plan carries (`INS-1`…`INS-8`).
/// </summary>
/// <remarks>
/// Grouped into one value so a plan's instructions travel together and so comparing two plans is one
/// comparison, as a <see cref="PlayerAttributeSet"/> is for attributes.
/// </remarks>
public sealed record TeamInstructionSet
{
    /// <summary>
    /// Gets the neutral instruction set a side takes the field with when its club has saved no plan.
    /// </summary>
    /// <remarks>
    /// Every value is the one that neither instructs nor inhibits, which is what makes it a fair starting
    /// point rather than a tactic: a club whose manager has never opened the tactics screen fields a side
    /// that is penalised for nothing and rewarded for nothing. It is deliberately explicit rather than a
    /// default-constructed record, because the enums' zero values are <c>Defensive</c>, <c>Low</c>, and
    /// <c>Narrow</c> — the extremes, not the middle.
    /// </remarks>
    public static TeamInstructionSet Neutral { get; } = new()
    {
        Mentality = Mentality.Balanced,
        Tempo = Tempo.Normal,
        Passing = PassingStyle.MixedPassing,
        Width = Width.Normal,
        Pressing = Pressing.MidBlock,
        DefensiveLine = DefensiveLine.Normal,
        Tackling = TacklingStyle.Normal,
        TimeWasting = TimeWasting.Off,
    };

    /// <summary>Gets the overall approach.</summary>
    public Mentality Mentality { get; init; }

    /// <summary>Gets how quickly the team moves the ball.</summary>
    public Tempo Tempo { get; init; }

    /// <summary>Gets what kind of pass the team favours.</summary>
    public PassingStyle Passing { get; init; }

    /// <summary>Gets how far the team spreads across the pitch.</summary>
    public Width Width { get; init; }

    /// <summary>Gets where the team begins to press.</summary>
    public Pressing Pressing { get; init; }

    /// <summary>Gets how high the defensive line holds.</summary>
    public DefensiveLine DefensiveLine { get; init; }

    /// <summary>Gets how committed the tackling is.</summary>
    public TacklingStyle Tackling { get; init; }

    /// <summary>Gets whether the team runs down the clock.</summary>
    public TimeWasting TimeWasting { get; init; }
}

/// <summary>
/// Stable codes and storage representation for the eight team instructions (`INS-1`…`INS-8`).
/// </summary>
/// <remarks>
/// Every instruction has a bounded effect and a counter-cost (`INS-9`); this type only names and stores
/// the choice. The trade-offs are the engine's, and arrive with it in Stage 5.
/// </remarks>
public static class TeamInstructions
{
    /// <summary>The longest code across all eight enums, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 12;

    /// <summary>Converts a mentality to its stable code.</summary>
    /// <param name="value">The mentality.</param>
    public static string ToCode(this Mentality value) => value switch
    {
        Mentality.Defensive => "defensive",
        Mentality.Cautious => "cautious",
        Mentality.Balanced => "balanced",
        Mentality.Positive => "positive",
        Mentality.Attacking => "attacking",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown mentality."),
    };

    /// <summary>Parses a stable code back to its mentality.</summary>
    /// <param name="code">The stable code.</param>
    public static Mentality MentalityFromCode(string code) => code switch
    {
        "defensive" => Mentality.Defensive,
        "cautious" => Mentality.Cautious,
        "balanced" => Mentality.Balanced,
        "positive" => Mentality.Positive,
        "attacking" => Mentality.Attacking,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown mentality code."),
    };

    /// <summary>Converts a tempo to its stable code.</summary>
    /// <param name="value">The tempo.</param>
    public static string ToCode(this Tempo value) => value switch
    {
        Tempo.Low => "low",
        Tempo.Normal => "normal",
        Tempo.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown tempo."),
    };

    /// <summary>Parses a stable code back to its tempo.</summary>
    /// <param name="code">The stable code.</param>
    public static Tempo TempoFromCode(string code) => code switch
    {
        "low" => Tempo.Low,
        "normal" => Tempo.Normal,
        "high" => Tempo.High,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown tempo code."),
    };

    /// <summary>Converts a passing style to its stable code.</summary>
    /// <param name="value">The passing style.</param>
    public static string ToCode(this PassingStyle value) => value switch
    {
        PassingStyle.ShortPassing => "short",
        PassingStyle.MixedPassing => "mixed",
        PassingStyle.DirectPassing => "direct",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown passing style."),
    };

    /// <summary>Parses a stable code back to its passing style.</summary>
    /// <param name="code">The stable code.</param>
    public static PassingStyle PassingFromCode(string code) => code switch
    {
        "short" => PassingStyle.ShortPassing,
        "mixed" => PassingStyle.MixedPassing,
        "direct" => PassingStyle.DirectPassing,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown passing style code."),
    };

    /// <summary>Converts a width to its stable code.</summary>
    /// <param name="value">The width.</param>
    public static string ToCode(this Width value) => value switch
    {
        Width.Narrow => "narrow",
        Width.Normal => "normal",
        Width.Wide => "wide",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown width."),
    };

    /// <summary>Parses a stable code back to its width.</summary>
    /// <param name="code">The stable code.</param>
    public static Width WidthFromCode(string code) => code switch
    {
        "narrow" => Width.Narrow,
        "normal" => Width.Normal,
        "wide" => Width.Wide,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown width code."),
    };

    /// <summary>Converts a pressing scheme to its stable code.</summary>
    /// <param name="value">The pressing scheme.</param>
    public static string ToCode(this Pressing value) => value switch
    {
        Pressing.LowBlock => "low_block",
        Pressing.MidBlock => "mid_block",
        Pressing.HighPress => "high_press",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown pressing scheme."),
    };

    /// <summary>Parses a stable code back to its pressing scheme.</summary>
    /// <param name="code">The stable code.</param>
    public static Pressing PressingFromCode(string code) => code switch
    {
        "low_block" => Pressing.LowBlock,
        "mid_block" => Pressing.MidBlock,
        "high_press" => Pressing.HighPress,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown pressing scheme code."),
    };

    /// <summary>Converts a defensive line to its stable code.</summary>
    /// <param name="value">The defensive line.</param>
    public static string ToCode(this DefensiveLine value) => value switch
    {
        DefensiveLine.Deep => "deep",
        DefensiveLine.Normal => "normal",
        DefensiveLine.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown defensive line."),
    };

    /// <summary>Parses a stable code back to its defensive line.</summary>
    /// <param name="code">The stable code.</param>
    public static DefensiveLine LineFromCode(string code) => code switch
    {
        "deep" => DefensiveLine.Deep,
        "normal" => DefensiveLine.Normal,
        "high" => DefensiveLine.High,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown defensive line code."),
    };

    /// <summary>Converts a tackling style to its stable code.</summary>
    /// <param name="value">The tackling style.</param>
    public static string ToCode(this TacklingStyle value) => value switch
    {
        TacklingStyle.StayOnFeet => "stay_on_feet",
        TacklingStyle.Normal => "normal",
        TacklingStyle.Aggressive => "aggressive",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown tackling style."),
    };

    /// <summary>Parses a stable code back to its tackling style.</summary>
    /// <param name="code">The stable code.</param>
    public static TacklingStyle TacklingFromCode(string code) => code switch
    {
        "stay_on_feet" => TacklingStyle.StayOnFeet,
        "normal" => TacklingStyle.Normal,
        "aggressive" => TacklingStyle.Aggressive,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown tackling style code."),
    };

    /// <summary>Converts a time-wasting setting to its stable code.</summary>
    /// <param name="value">The time-wasting setting.</param>
    public static string ToCode(this TimeWasting value) => value switch
    {
        TimeWasting.Off => "off",
        TimeWasting.Situational => "situational",
        TimeWasting.On => "on",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown time-wasting setting."),
    };

    /// <summary>Parses a stable code back to its time-wasting setting.</summary>
    /// <param name="code">The stable code.</param>
    public static TimeWasting TimeWastingFromCode(string code) => code switch
    {
        "off" => TimeWasting.Off,
        "situational" => TimeWasting.Situational,
        "on" => TimeWasting.On,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown time-wasting code."),
    };
}
