namespace Tactical11Manager.Domain.Fans;

/// <summary>What the fans expect from a match, in words, from the side they are talking about (`FAN-4`).</summary>
/// <remarks>The members run from the least to the most hopeful, so a higher value is always a better expectation.</remarks>
public enum FanExpectationLabel
{
    /// <summary>Expected points below 0.5.</summary>
    DamageLimitation = 0,

    /// <summary>Expected points from 0.5 to 0.8.</summary>
    ToughGame = 1,

    /// <summary>Expected points from 0.8 to 1.2.</summary>
    APointWouldDo = 2,

    /// <summary>Expected points from 1.2 to 1.6.</summary>
    CouldGoEitherWay = 3,

    /// <summary>Expected points from 1.6 to 2.0.</summary>
    ShouldWin = 4,

    /// <summary>Expected points from 2.0 to 2.4.</summary>
    ExpectAWin = 5,

    /// <summary>Expected points of 2.4 or more.</summary>
    ShouldHammerThem = 6,
}

/// <summary>The words and stored codes of the fans' expectation labels.</summary>
public static class FanExpectationLabels
{
    /// <summary>The phrase the game shows for a label.</summary>
    /// <param name="label">The label.</param>
    public static string Phrase(FanExpectationLabel label) => label switch
    {
        FanExpectationLabel.DamageLimitation => "Damage limitation",
        FanExpectationLabel.ToughGame => "Tough game",
        FanExpectationLabel.APointWouldDo => "A point would do",
        FanExpectationLabel.CouldGoEitherWay => "Could go either way",
        FanExpectationLabel.ShouldWin => "Should win",
        FanExpectationLabel.ExpectAWin => "Expect a win",
        FanExpectationLabel.ShouldHammerThem => "Should hammer them",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown fan expectation label."),
    };

    /// <summary>The stable code a label is stored and sent under.</summary>
    /// <param name="label">The label.</param>
    public static string Code(FanExpectationLabel label) => label switch
    {
        FanExpectationLabel.DamageLimitation => "damage_limitation",
        FanExpectationLabel.ToughGame => "tough_game",
        FanExpectationLabel.APointWouldDo => "a_point_would_do",
        FanExpectationLabel.CouldGoEitherWay => "could_go_either_way",
        FanExpectationLabel.ShouldWin => "should_win",
        FanExpectationLabel.ExpectAWin => "expect_a_win",
        FanExpectationLabel.ShouldHammerThem => "should_hammer_them",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown fan expectation label."),
    };

    /// <summary>Reads a stored code back to its label.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The label, or null when the code is not one of ours.</returns>
    public static FanExpectationLabel? FromCode(string code) =>
        Enum.GetValues<FanExpectationLabel>().Cast<FanExpectationLabel?>().FirstOrDefault(label => Code(label!.Value) == code);
}

/// <summary>
/// What the fans of one side expect from one match (`FAN-4`): a label, the chances they give each result, and the score they would bet on.
/// </summary>
/// <remarks>
/// The label, the chances and the score all describe the same side, so a "Should win" never carries a predicted defeat. The two sides of
/// a fixture each have their own expectation, and the away side's is the home side's mirrored.
/// </remarks>
/// <param name="ExpectedPointsCentis">The points the fans expect the side to take, in hundredths: 3 for a win and 1 for a draw, weighted by the chances.</param>
/// <param name="WinBp">The chance of a win, in basis points.</param>
/// <param name="DrawBp">The chance of a draw, in basis points.</param>
/// <param name="LossBp">The chance of a defeat, in basis points. The three chances add up to 10,000.</param>
/// <param name="Label">The label the expected points fall under.</param>
/// <param name="PredictedGoalsFor">The goals the side scores in the score the fans would bet on.</param>
/// <param name="PredictedGoalsAgainst">The goals the side concedes in it.</param>
public sealed record FanExpectation(
    int ExpectedPointsCentis,
    int WinBp,
    int DrawBp,
    int LossBp,
    FanExpectationLabel Label,
    int PredictedGoalsFor,
    int PredictedGoalsAgainst);
