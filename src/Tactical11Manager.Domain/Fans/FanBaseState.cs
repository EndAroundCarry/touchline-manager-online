namespace Tactical11Manager.Domain.Fans;

/// <summary>What one club's fans are at a moment: how many there are, how they feel, and how the club stood against their hopes last week.</summary>
/// <remarks>
/// The fan rules (<c>FanRuleSet</c>) take one of these and hand back the next, so a result, a week, a season or a takeover is a plain
/// function from state to state with nothing to read from the database.
/// </remarks>
/// <param name="Fans">The fans the club has. The people who come to a home match are a share of them (<c>FanRuleSet.WantToComeShareBp</c>).</param>
/// <param name="MoodBp">How satisfied they are, from 0 (furious) to 10,000 (euphoric), in basis points; 5,000 is calm.</param>
/// <param name="LastWeekGap">
/// Where the club stood against the fans' target at the last weekly review, as projected place minus target place (positive is worse
/// than hoped), or null before the first review of a season and after a takeover.
/// </param>
public readonly record struct FanBaseState(int Fans, int MoodBp, int? LastWeekGap = null);
