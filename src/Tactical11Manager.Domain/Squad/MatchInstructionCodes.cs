namespace Tactical11Manager.Domain.Squad;

/// <summary>What a match instruction asks of the scoreline (`MAT-6`).</summary>
public enum MatchInstructionScore
{
    /// <summary>Any score.</summary>
    Any = 0,

    /// <summary>The club leads by at least the margin.</summary>
    Leading = 1,

    /// <summary>The score is level.</summary>
    Level = 2,

    /// <summary>The club trails by at least the margin.</summary>
    Trailing = 3,

    /// <summary>The club is not ahead: level or behind.</summary>
    NotWinning = 4,

    /// <summary>The club is not behind: level or ahead.</summary>
    NotLosing = 5,
}

/// <summary>The event a match instruction waits for, besides the minute and the score (`MAT-6`).</summary>
public enum MatchInstructionEvent
{
    /// <summary>No event: the minute and the score are all the instruction asks.</summary>
    None = 0,

    /// <summary>One of the club's players has been sent off.</summary>
    OwnSentOff = 1,

    /// <summary>One of the opponent's players has been sent off.</summary>
    OpponentSentOff = 2,

    /// <summary>One of the club's players on the pitch has been booked. He is the instruction's subject.</summary>
    OwnBooked = 3,

    /// <summary>A player is playing badly: his live rating is below the threshold, or, with no threshold, he is the lowest rated of the family.</summary>
    PlayingBadly = 4,

    /// <summary>A player's condition is below the threshold.</summary>
    Tired = 5,

    /// <summary>The opponent's mentality is at least the threshold: it chases the game.</summary>
    OpponentMentalityAtLeast = 6,

    /// <summary>The opponent's mentality is at most the threshold: it parks the bus.</summary>
    OpponentMentalityAtMost = 7,
}

/// <summary>Which player a match instruction's substitution takes off (`MAT-6`).</summary>
public enum MatchInstructionOff
{
    /// <summary>A named player of the plan's lineup.</summary>
    Player = 0,

    /// <summary>The player the instruction's event is about: the booked, the tired or the badly playing one.</summary>
    Subject = 1,

    /// <summary>The most tired player on the pitch in a family.</summary>
    MostTiredInFamily = 2,

    /// <summary>The lowest rated player on the pitch in a family.</summary>
    LowestRatedInFamily = 3,
}

/// <summary>Which player a match instruction's substitution brings on (`MAT-6`).</summary>
public enum MatchInstructionOn
{
    /// <summary>A named player of the plan's bench.</summary>
    Player = 0,

    /// <summary>The substitute of a family who fits the vacated slot best and is freshest.</summary>
    BestFitInFamily = 1,
}

/// <summary>Stable codes and storage representation for the match instruction enums.</summary>
public static class MatchInstructionCodes
{
    /// <summary>The longest code across the four enums, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 28;

    /// <summary>Converts a score condition to its stable code.</summary>
    /// <param name="value">The score condition.</param>
    public static string ToCode(this MatchInstructionScore value) => value switch
    {
        MatchInstructionScore.Any => "any",
        MatchInstructionScore.Leading => "leading",
        MatchInstructionScore.Level => "level",
        MatchInstructionScore.Trailing => "trailing",
        MatchInstructionScore.NotWinning => "not_winning",
        MatchInstructionScore.NotLosing => "not_losing",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown score condition."),
    };

    /// <summary>Parses a stable code back to its score condition.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchInstructionScore ScoreFromCode(string code) => code switch
    {
        "any" => MatchInstructionScore.Any,
        "leading" => MatchInstructionScore.Leading,
        "level" => MatchInstructionScore.Level,
        "trailing" => MatchInstructionScore.Trailing,
        "not_winning" => MatchInstructionScore.NotWinning,
        "not_losing" => MatchInstructionScore.NotLosing,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown score condition code."),
    };

    /// <summary>Converts an event to its stable code.</summary>
    /// <param name="value">The event.</param>
    public static string ToCode(this MatchInstructionEvent value) => value switch
    {
        MatchInstructionEvent.None => "none",
        MatchInstructionEvent.OwnSentOff => "own_sent_off",
        MatchInstructionEvent.OpponentSentOff => "opponent_sent_off",
        MatchInstructionEvent.OwnBooked => "own_booked",
        MatchInstructionEvent.PlayingBadly => "playing_badly",
        MatchInstructionEvent.Tired => "tired",
        MatchInstructionEvent.OpponentMentalityAtLeast => "opponent_mentality_at_least",
        MatchInstructionEvent.OpponentMentalityAtMost => "opponent_mentality_at_most",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown match instruction event."),
    };

    /// <summary>Parses a stable code back to its event.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchInstructionEvent EventFromCode(string code) => code switch
    {
        "none" => MatchInstructionEvent.None,
        "own_sent_off" => MatchInstructionEvent.OwnSentOff,
        "opponent_sent_off" => MatchInstructionEvent.OpponentSentOff,
        "own_booked" => MatchInstructionEvent.OwnBooked,
        "playing_badly" => MatchInstructionEvent.PlayingBadly,
        "tired" => MatchInstructionEvent.Tired,
        "opponent_mentality_at_least" => MatchInstructionEvent.OpponentMentalityAtLeast,
        "opponent_mentality_at_most" => MatchInstructionEvent.OpponentMentalityAtMost,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown match instruction event code."),
    };

    /// <summary>Converts a take-off selector to its stable code.</summary>
    /// <param name="value">The selector.</param>
    public static string ToCode(this MatchInstructionOff value) => value switch
    {
        MatchInstructionOff.Player => "player",
        MatchInstructionOff.Subject => "subject",
        MatchInstructionOff.MostTiredInFamily => "most_tired_in_family",
        MatchInstructionOff.LowestRatedInFamily => "lowest_rated_in_family",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown take-off selector."),
    };

    /// <summary>Parses a stable code back to its take-off selector.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchInstructionOff OffFromCode(string code) => code switch
    {
        "player" => MatchInstructionOff.Player,
        "subject" => MatchInstructionOff.Subject,
        "most_tired_in_family" => MatchInstructionOff.MostTiredInFamily,
        "lowest_rated_in_family" => MatchInstructionOff.LowestRatedInFamily,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown take-off selector code."),
    };

    /// <summary>Converts a bring-on selector to its stable code.</summary>
    /// <param name="value">The selector.</param>
    public static string ToCode(this MatchInstructionOn value) => value switch
    {
        MatchInstructionOn.Player => "player",
        MatchInstructionOn.BestFitInFamily => "best_fit_in_family",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown bring-on selector."),
    };

    /// <summary>Parses a stable code back to its bring-on selector.</summary>
    /// <param name="code">The stable code.</param>
    public static MatchInstructionOn OnFromCode(string code) => code switch
    {
        "player" => MatchInstructionOn.Player,
        "best_fit_in_family" => MatchInstructionOn.BestFitInFamily,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown bring-on selector code."),
    };
}
