using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// Builds the match center's lineups from the frozen snapshot and the result (`replay-v2`, §9.5).
/// </summary>
/// <remarks>
/// <para>
/// The lineup is a projection of two things the engine already has: who was named, from the snapshot, and
/// what they did, from the player lines the simulation produced. Nothing here is a second copy of a fact —
/// goals, assists, cards, minutes, condition, and rating are read from the result's own lines, and the
/// names, shirt numbers, and positions from the snapshot the result was produced from.
/// </para>
/// <para>
/// The three things that are neither are derived instead: the formation label from the side's own roles,
/// the short name from the club's frozen name, and the colours from the colours the snapshot froze, or the
/// generated palette when the club's manager chose none. All three are
/// functions of the frozen input, deliberately — a presentation is immutable and cached under the result's
/// own hash, so a field read from live club state would let the same entity tag describe two different
/// bodies (`MAT-8`, §9.5).
/// </para>
/// </remarks>
public static class MatchLineupBuilder
{
    /// <summary>The slot number a player who was not in the eleven carries.</summary>
    public const int NoSlot = 0;

    /// <summary>The label a side fields when its slots match none of the standard shapes.</summary>
    private const string CustomFormation = "custom";

    /// <summary>
    /// Builds one side's lineup for the match center.
    /// </summary>
    /// <param name="input">The frozen snapshot.</param>
    /// <param name="result">The simulated result.</param>
    /// <param name="side">Which side to build.</param>
    public static MatchLineupV1 Build(MatchInputV1 input, MatchResultV1 result, MatchSide side)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);

        var frozen = input.SideOf(side);
        var participants = frozen.Squad.ToDictionary(participant => participant.ParticipantId);
        var lines = result.PlayerLines
            .Where(line => line.Side == side)
            .ToDictionary(line => line.ParticipantId);
        var slots = frozen.Slots.OrderBy(slot => slot.SlotNumber).ToList();
        var starting = slots.Select(slot => slot.ParticipantId).ToHashSet();

        // A result carries a line for every player the snapshot named, so a missing one is a defect worth
        // naming rather than a case to invent a line for: a panel showing a made-up goal would be worse
        // than a read that refuses.
        foreach (var participant in frozen.Squad)
        {
            if (!lines.ContainsKey(participant.ParticipantId))
            {
                throw new InvalidOperationException(
                    $"The result carries no line for participant {participant.ParticipantId:D} of "
                    + $"club {frozen.ClubId:D}, so its lineup cannot be built.");
            }
        }

        var colours = ClubPalette.Resolve(frozen.ClubId, frozen.PrimaryColour, frozen.SecondaryColour);

        return new MatchLineupV1
        {
            ClubName = frozen.ClubName,
            ShortName = ShortName(frozen.ClubName),
            PrimaryColour = colours.Primary,
            SecondaryColour = colours.Secondary,
            Formation = FormationOf(slots),

            // The eleven in slot order, then the bench in the order the snapshot named it: the sheet a
            // manager reads is the sheet the lock froze, not a re-sort the screen invented.
            Starters =
            [
                .. slots.Select(slot => Line(
                    participants[slot.ParticipantId],
                    lines[slot.ParticipantId],
                    isStarter: true,
                    slot.SlotNumber,
                    slot.Family,
                    PositionCode(slot))),
            ],
            Bench =
            [
                .. frozen.Squad
                    .Where(participant => !starting.Contains(participant.ParticipantId))
                    .Select(participant => Line(
                        participant,
                        lines[participant.ParticipantId],
                        isStarter: false,
                        NoSlot,
                        participant.Position.FamilyOf(),
                        PositionCode(participant.Position))),
            ],
        };
    }

    /// <summary>Projects one player into their match center line.</summary>
    /// <param name="participant">The player, from the frozen squad.</param>
    /// <param name="line">The player's own result line.</param>
    /// <param name="isStarter">Whether they were in the eleven.</param>
    /// <param name="slotNumber">The slot they started in, or zero for a substitute.</param>
    /// <param name="family">The band they played in: the slot's for a starter, their own for a substitute.</param>
    /// <param name="position">The abbreviation a badge shows.</param>
    private static MatchLineupPlayerV1 Line(
        MatchParticipantV1 participant,
        MatchPlayerLineV1 line,
        bool isStarter,
        int slotNumber,
        MatchPositionFamily family,
        string position) =>
        new()
        {
            ParticipantId = participant.ParticipantId,
            PlayerId = participant.PlayerId,
            ShirtNumber = participant.ShirtNumber,
            Name = participant.DisplayName,
            Position = position,
            Family = family,
            IsStarter = isStarter,
            SlotNumber = slotNumber,
            KickoffCondition = participant.State.ConditionBasisPoints,
            FinalCondition = line.FinalConditionBasisPoints,

            // The badge the match center shows is the live rating the viewer watched fluctuate, not the
            // summary rating the season projection reads: the panel and the curve beside it describe the
            // same performance, and only one of them can be the figure on the pitch (`engine-v3`). Zero for
            // a player who never came on, which a panel draws as no rating at all.
            FinalRating = line.LiveRatingBasisPoints,
            Goals = line.Goals,
            Assists = line.Assists,
            YellowCards = line.YellowCards,
            SentOff = line.SentOff,
            SubbedOutMinute = line.SubbedOutMinute,
            SubbedInMinute = line.SubbedInMinute,
            IsInjured = line.IsInjured,
        };

    /// <summary>
    /// Names the shape a side played, from its own roles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The thirteen standard shapes are matched by the roles their slots ask for, in slot order, rather than by
    /// their coordinates. Two of them — a four-three-three and a four-one-four-one — differ only in how
    /// close the holding midfielder stands to the two in front of him, so a coordinate rule that named one
    /// correctly renamed the other; the roles a manager assigned are the shape they chose, and are exact.
    /// </para>
    /// <para>
    /// A side whose roles match nothing standard — a manager may change any slot's role — falls back to
    /// describing its own lines, the outfield slots grouped by how deep they stand. A label is a display
    /// fact, and "4-4-2" derived honestly beats a standard name that is wrong.
    /// </para>
    /// </remarks>
    private static string FormationOf(IReadOnlyList<MatchSlotV1> slots)
    {
        var roles = slots.Select(slot => slot.Role).ToArray();

        foreach (var (code, pattern) in Formations)
        {
            if (pattern.AsSpan().SequenceEqual(roles))
            {
                return code;
            }
        }

        return ClusterFormationOf(slots);
    }

    /// <summary>The thirteen standard shapes, as the role each slot asks for in slot order.</summary>
    private static readonly (string Code, MatchRole[] Roles)[] Formations =
    [
        ("4-4-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("4-3-3",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
            MatchRole.Winger,
        ]),
        ("4-2-3-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.DefensiveMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
        ]),
        ("4-1-4-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
        ]),
        ("3-5-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.DefensiveMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.WingBack,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("5-3-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.WingBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("4-4-1-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
        ]),
        ("4-5-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.Winger,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.Striker,
        ]),
        ("4-3-2-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
        ]),
        ("4-2-2-2",
        [
            MatchRole.Goalkeeper,
            MatchRole.FullBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.FullBack,
            MatchRole.DefensiveMidfielder,
            MatchRole.DefensiveMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
            MatchRole.Striker,
        ]),
        ("3-4-3",
        [
            MatchRole.Goalkeeper,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Winger,
            MatchRole.WingBack,
            MatchRole.Striker,
            MatchRole.Winger,
        ]),
        ("3-4-2-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.AttackingMidfielder,
            MatchRole.WingBack,
            MatchRole.AttackingMidfielder,
            MatchRole.Striker,
        ]),
        ("5-4-1",
        [
            MatchRole.Goalkeeper,
            MatchRole.WingBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.CentreBack,
            MatchRole.WingBack,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.CentralMidfielder,
            MatchRole.Striker,
        ]),
    ];

    /// <summary>
    /// Describes a non-standard shape by the lines its outfield players stand in, deepest first.
    /// </summary>
    /// <remarks>
    /// A line is a run of consecutive slots no further apart up the pitch than the rules' own slot scale
    /// allows for a line: a full back standing two hundred units deeper than his centre back is in the same
    /// line, a midfielder fourteen hundred units ahead of the holding player may or may not be — which is
    /// exactly why the standard shapes take the exact path above and this one only describes the rest.
    /// </remarks>
    private static string ClusterFormationOf(IReadOnlyList<MatchSlotV1> slots)
    {
        var outfield = slots
            .Where(slot => slot.Family != MatchPositionFamily.Goalkeeper)
            .OrderBy(slot => slot.X)
            .ToList();

        if (outfield.Count == 0)
        {
            return CustomFormation;
        }

        var lines = new List<int>();
        var depth = outfield[0].X;
        var count = 0;

        foreach (var slot in outfield)
        {
            if (slot.X - depth > LineTolerance)
            {
                lines.Add(count);
                count = 0;
            }

            depth = slot.X;
            count++;
        }

        lines.Add(count);

        return string.Join('-', lines);
    }

    /// <summary>How far apart up the pitch two slots may stand and still be described as one line.</summary>
    private const int LineTolerance = 1_400;

    /// <summary>
    /// Names the position a slot asks its occupant to play, in the abbreviation a match center badge uses.
    /// </summary>
    /// <remarks>
    /// A flank is read from the slot's own Y, which the tactics board normalizes with the right touchline at
    /// the top of its scale — the convention every standard layout follows, where slot 2 is the right back.
    /// </remarks>
    private static string PositionCode(MatchSlotV1 slot) => slot.Role switch
    {
        MatchRole.Goalkeeper => "GK",
        MatchRole.CentreBack => "DC",
        MatchRole.FullBack => Flank("RB", "LB", slot.Y),
        MatchRole.WingBack => Flank("RWB", "LWB", slot.Y),
        MatchRole.DefensiveMidfielder => "DM",
        MatchRole.CentralMidfielder => "MC",
        MatchRole.AttackingMidfielder => "AM",
        MatchRole.Winger => Flank("RW", "LW", slot.Y),
        MatchRole.Striker => "ST",
        _ => "??",
    };

    /// <summary>Names a player's own position, for a substitute who occupied no slot.</summary>
    /// <param name="position">The position.</param>
    private static string PositionCode(MatchPosition position) => position switch
    {
        MatchPosition.Goalkeeper => "GK",
        MatchPosition.RightBack => "RB",
        MatchPosition.CentreBack => "DC",
        MatchPosition.LeftBack => "LB",
        MatchPosition.DefensiveMidfielder => "DM",
        MatchPosition.CentralMidfielder => "MC",
        MatchPosition.AttackingMidfielder => "AM",
        MatchPosition.RightWinger => "RW",
        MatchPosition.LeftWinger => "LW",
        MatchPosition.Striker => "ST",
        _ => "??",
    };

    /// <summary>Picks the right- or left-flank code from a slot's normalized Y.</summary>
    private static string Flank(string right, string left, int y) =>
        y >= EngineRulesV2.SlotCoordinateScale / 2 ? right : left;

    /// <summary>Abbreviates the club's frozen name, because the snapshot carries no short name of its own.</summary>
    /// <param name="clubName">The club's display name.</param>
    private static string ShortName(string clubName)
    {
        var letters = new string([.. clubName.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

        return letters.Length switch
        {
            0 => "CLB",
            <= 3 => letters,
            _ => letters[..3],
        };
    }
}
