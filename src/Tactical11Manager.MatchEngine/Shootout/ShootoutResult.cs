using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.MatchEngine.Shootout;

/// <summary>How one shoot-out kick ended.</summary>
public enum ShootoutKickOutcome
{
    /// <summary>The kick went in.</summary>
    Scored = 0,

    /// <summary>The goalkeeper stopped it.</summary>
    Saved = 1,

    /// <summary>The kick missed the target.</summary>
    Wide = 2,
}

/// <summary>One kick of a shoot-out.</summary>
/// <param name="Number">The kick's place in the whole shoot-out, from 1.</param>
/// <param name="Round">The round it belongs to, from 1: each side kicks once a round.</param>
/// <param name="Side">The side that took it.</param>
/// <param name="TakerId">The participant who took it.</param>
/// <param name="GoalkeeperId">The goalkeeper who faced it, or null when the other side had none on the pitch.</param>
/// <param name="Outcome">How it ended.</param>
public sealed record ShootoutKick(
    int Number,
    int Round,
    MatchSide Side,
    Guid TakerId,
    Guid? GoalkeeperId,
    ShootoutKickOutcome Outcome)
{
    /// <summary>Gets whether the kick went in.</summary>
    public bool IsGoal => Outcome == ShootoutKickOutcome.Scored;
}

/// <summary>The decided result of a penalty shoot-out.</summary>
/// <param name="Version">The shoot-out rules that produced it.</param>
/// <param name="FirstSide">The side that kicked first.</param>
/// <param name="HomeScore">The home side's penalties scored.</param>
/// <param name="AwayScore">The away side's penalties scored.</param>
/// <param name="Kicks">Every kick, in the order it was taken.</param>
public sealed record ShootoutResult(
    string Version,
    MatchSide FirstSide,
    int HomeScore,
    int AwayScore,
    IReadOnlyList<ShootoutKick> Kicks)
{
    /// <summary>Gets the side that won.</summary>
    public MatchSide Winner => HomeScore > AwayScore ? MatchSide.Home : MatchSide.Away;
}
