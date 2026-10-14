using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// How good a side's attackers are against the other side's defenders (`weaker-side-v2`, Milestone 6): the sharpness of an attacking
/// mentality depends on the players it is asked of. A side that goes forward with forwards better than the defenders it meets gets more
/// out of Positive and Attacking, and one whose forwards are worse gets less, down to less than a Balanced side would: it throws men
/// forward for nothing and the team with the better defenders gains.
/// </summary>
/// <remarks>
/// The edge is worked out from the raw attributes, not the skills the skill curve pulls towards the average, so a gap between two players
/// counts as it was written. It scales only what Positive and Attacking add to Balanced; Balanced, Cautious and Defensive never move, so
/// a deep or counter-attacking side is not touched.
/// </remarks>
internal static class TickAttackEdge
{
    /// <summary>The percent the factor moves for each attribute point of edge.</summary>
    public const int PerPointPercent = 30;

    /// <summary>The lowest the factor goes, in percent: negative, so the extras of an attacking mentality turn into a cost below Balanced.</summary>
    public const int FloorPercent = -50;

    /// <summary>The highest the factor goes, in percent.</summary>
    public const int CeilingPercent = 130;

    /// <summary>The factor of a side with no edge: its mentality's extras count in full.</summary>
    public const int NeutralPercent = 100;

    /// <summary>How many of the midfield count as attackers: the most advanced ones on the board.</summary>
    public const int AdvancedMidfielders = 2;

    private static readonly MatchAttributeName[] AttackSkills =
    [
        MatchAttributeName.Finishing,
        MatchAttributeName.Dribbling,
        MatchAttributeName.FirstTouch,
        MatchAttributeName.Technique,
        MatchAttributeName.Composure,
        MatchAttributeName.Pace,
        MatchAttributeName.Acceleration,
    ];

    private static readonly MatchAttributeName[] DefenceSkills =
    [
        MatchAttributeName.Tackling,
        MatchAttributeName.Marking,
        MatchAttributeName.Positioning,
        MatchAttributeName.Strength,
        MatchAttributeName.Pace,
        MatchAttributeName.Acceleration,
    ];

    /// <summary>Gets a player's attacking quality, in hundredths of an attribute point.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static int AttackQuality(PlayerAttributesV1 attributes) => Mean(attributes, AttackSkills);

    /// <summary>Gets a player's defending quality, in hundredths of an attribute point.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static int DefenceQuality(PlayerAttributesV1 attributes) => Mean(attributes, DefenceSkills);

    /// <summary>Gets the factor for an edge.</summary>
    /// <param name="edgeHundredths">The attackers' quality less the defenders', in hundredths of an attribute point.</param>
    /// <returns>100 for no edge; higher with an edge, lower without, within the floor and the ceiling.</returns>
    public static int Factor(int edgeHundredths) =>
        Math.Clamp(NeutralPercent + (edgeHundredths * PerPointPercent / 100), FloorPercent, CeilingPercent);

    /// <summary>Gets how much of a mentality's extra a side gets, rounded to the nearest whole, away from zero for a half.</summary>
    /// <param name="extra">What the mentality adds to Balanced, in whatever unit the caller counts.</param>
    /// <param name="factorPercent">The side's factor.</param>
    /// <returns>The extra at <paramref name="factorPercent"/>: all of it at 100, none at 0, a cost below.</returns>
    public static int Scaled(int extra, int factorPercent)
    {
        var scaled = extra * factorPercent;

        return scaled >= 0 ? (scaled + 50) / 100 : -((-scaled + 50) / 100);
    }

    /// <summary>
    /// Gets a side's edge: the mean attacking quality of its forwards and its two most advanced midfielders, less the mean defending quality
    /// of the other side's defenders.
    /// </summary>
    /// <param name="attackers">The side that attacks.</param>
    /// <param name="defenders">The side it attacks.</param>
    /// <returns>The edge in hundredths of an attribute point, or 0 when either group is empty.</returns>
    public static int Edge(TickTeam attackers, TickTeam defenders)
    {
        ArgumentNullException.ThrowIfNull(attackers);
        ArgumentNullException.ThrowIfNull(defenders);

        long attackSum = 0;
        var attackCount = 0;
        Span<int> advanced = stackalloc int[AdvancedMidfielders];
        Span<int> advancedSeat = stackalloc int[AdvancedMidfielders];

        advanced.Fill(int.MinValue);
        advancedSeat.Fill(-1);

        for (var seat = 0; seat < attackers.Count; seat++)
        {
            var spec = attackers.Spec[seat];

            if (spec.Family == MatchPositionFamily.Attack)
            {
                attackSum += attackers.AttackQuality[seat];
                attackCount++;
            }
            else if (spec.Family == MatchPositionFamily.Midfield)
            {
                // Keep the most advanced few: slot in by depth, pushing the shallower one out.
                for (var place = 0; place < AdvancedMidfielders; place++)
                {
                    if (spec.OwnX <= advanced[place])
                    {
                        continue;
                    }

                    for (var down = AdvancedMidfielders - 1; down > place; down--)
                    {
                        advanced[down] = advanced[down - 1];
                        advancedSeat[down] = advancedSeat[down - 1];
                    }

                    advanced[place] = spec.OwnX;
                    advancedSeat[place] = seat;

                    break;
                }
            }
        }

        foreach (var seat in advancedSeat)
        {
            if (seat >= 0)
            {
                attackSum += attackers.AttackQuality[seat];
                attackCount++;
            }
        }

        long defenceSum = 0;
        var defenceCount = 0;

        for (var seat = 0; seat < defenders.Count; seat++)
        {
            if (defenders.Spec[seat].Family == MatchPositionFamily.Defence)
            {
                defenceSum += defenders.DefenceQuality[seat];
                defenceCount++;
            }
        }

        return attackCount == 0 || defenceCount == 0 ? 0 : (int)((attackSum / attackCount) - (defenceSum / defenceCount));
    }

    /// <summary>Works out both sides' factors from the players on the pitch: at kick-off and again after any change of who is on it.</summary>
    /// <param name="home">The home side.</param>
    /// <param name="away">The away side.</param>
    public static void Refresh(TickTeam home, TickTeam away)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(away);

        home.AttackEdgeFactorPercent = Factor(Edge(home, away));
        away.AttackEdgeFactorPercent = Factor(Edge(away, home));
    }

    private static int Mean(PlayerAttributesV1 attributes, MatchAttributeName[] names)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var sum = 0;

        foreach (var name in names)
        {
            sum += attributes.ValueOf(name);
        }

        return sum * 100 / names.Length;
    }
}
