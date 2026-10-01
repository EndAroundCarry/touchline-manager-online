using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>
/// Handles tactical adjustments when a team is reduced to 10 men (red card or unreplaced injury).
/// Leaves the vacated positional zone exposed, redistributing workload onto remaining players.
/// </summary>
public static class TacticalImbalanceHandler
{
    public const int ShorthandedStaminaMultiplier = 125; // 125% (+25%) stamina drain for teammates covering extra space
    public const int DefensiveDisorganizationPenalty = 350; // -3.5% defensive cohesion

    /// <summary>
    /// Adjusts effective slot coordinates when a side is short-handed so the formation can compensate
    /// while still leaving a visible vulnerability.
    /// </summary>
    public static int ApplyShorthandedModifier(int baseRating, int playersOnPitch)
    {
        if (playersOnPitch >= MatchInputV1.StartersOnPitch)
        {
            return baseRating;
        }

        var missing = MatchInputV1.StartersOnPitch - playersOnPitch;
        var penalty = missing * DefensiveDisorganizationPenalty;
        return Math.Max(0, baseRating - penalty);
    }
}
