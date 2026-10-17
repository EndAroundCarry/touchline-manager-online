namespace Tactical11Manager.Domain.Squad;

/// <summary>A player on the pitch, as the shape fit sees him: who he is, where he plays, and the slot he fills now.</summary>
/// <param name="Id">The player.</param>
/// <param name="Position">His primary position.</param>
/// <param name="Secondary">His other positions.</param>
/// <param name="Standing">The slot he fills now.</param>
public sealed record ShapeFitPlayer(
    Guid Id,
    PlayerPosition Position,
    IReadOnlyList<PlayerPosition> Secondary,
    FormationSlot Standing);

/// <summary>
/// Fits the players of a lineup into a new formation: who takes which slot when a match instruction changes the shape (`MAT-6`).
/// </summary>
/// <remarks>
/// <para>
/// Pure and without a random draw. It is a greedy assignment followed by a swap pass: every (player, slot) pair has a cost, pairs are taken
/// cheapest first, and then any two players who cost less swapped trade places. The cost is the player's unfamiliarity with the slot, weighted
/// by <see cref="FamiliarityWeight"/>, plus how far the slot is from where he stands now.
/// </para>
/// <para>
/// The same algorithm is written three times: here (what the server can say about a plan's lineup), in the match engine (which fits whoever
/// is on the pitch when the instruction fires), and in the tactics tab's live preview. One table of cases pins all three
/// (<c>tests/shared/shape-fit-cases.json</c>), and a test holds the penalties below to the engine rules' defaults, because the domain may not
/// reference the engine.
/// </para>
/// </remarks>
public static class FormationShapeFit
{
    /// <summary>A player who is wholly at home in the slot plays at full strength, in basis points.</summary>
    public const int Certain = 10_000;

    /// <summary>A player in the slot's family but not a position its role asks for, in basis points: the engine's unfamiliar-role penalty.</summary>
    public const int UnfamiliarRoleBasisPoints = 9_400;

    /// <summary>A player covered by a secondary position, or by a position the role names, in basis points: the engine's secondary-position penalty.</summary>
    public const int SecondaryPositionBasisPoints = 9_600;

    /// <summary>A player outside the slot's family altogether, in basis points: the engine's out-of-position penalty.</summary>
    public const int OutOfPositionBasisPoints = 8_800;

    /// <summary>
    /// How many times a basis point of unfamiliarity weighs against a unit of the 0…10,000 board distance. The penalties are small (a player
    /// out of position plays at 88%, a gap of 1,200), so at 5 being out of position costs as much as a walk of 60% of the pitch's length.
    /// </summary>
    public const int FamiliarityWeight = 5;

    /// <summary>How much a unit across the pitch counts against a unit along it, in percent: the pitch is 68 m wide and 105 m long.</summary>
    public const int LateralDistancePercent = 65;

    /// <summary>Fits the players of a lineup into a shape.</summary>
    /// <param name="onPitch">The players, with the slots they fill now.</param>
    /// <param name="newSlots">The new shape's slots, numbered from 1; the slot with the goalkeeper's family is his.</param>
    /// <param name="overrides">The manager's own placements, new slot number by player, or null. They are placed first.</param>
    /// <returns>
    /// The new slot number each placed player takes. When there are fewer players than slots, the slots nobody takes are missing from the
    /// values: the ones cheapest to lose, the most advanced first.
    /// </returns>
    public static Dictionary<Guid, int> Fit(
        IReadOnlyList<ShapeFitPlayer> onPitch,
        IReadOnlyList<FormationSlot> newSlots,
        IReadOnlyDictionary<Guid, int>? overrides)
    {
        ArgumentNullException.ThrowIfNull(onPitch);
        ArgumentNullException.ThrowIfNull(newSlots);

        var slots = newSlots.OrderBy(slot => slot.SlotNumber).ToList();

        if (slots.Select(slot => slot.SlotNumber).Distinct().Count() != slots.Count)
        {
            throw new ArgumentException("The new shape's slot numbers must be distinct.", nameof(newSlots));
        }

        var assigned = new Dictionary<Guid, int>();
        var taken = new HashSet<int>();

        // The goalkeeper stays in goal: the first slot of his family is his. If he has gone, nobody is put there.
        var keeperSlot = slots.FirstOrDefault(slot => slot.PositionFamily == PositionFamily.Goalkeeper);
        var keeper = onPitch.FirstOrDefault(player => player.Standing.PositionFamily == PositionFamily.Goalkeeper);

        if (keeperSlot is not null)
        {
            taken.Add(keeperSlot.SlotNumber);

            if (keeper is not null)
            {
                assigned[keeper.Id] = keeperSlot.SlotNumber;
            }
        }

        var players = onPitch
            .Where(player => player.Id != keeper?.Id)
            .OrderBy(player => player.Id)
            .ToList();

        PlaceOverrides(players, slots, overrides, keeperSlot, assigned, taken);

        var free = players.Where(player => !assigned.ContainsKey(player.Id)).ToList();
        var open = slots.Where(slot => !taken.Contains(slot.SlotNumber)).ToList();

        if (free.Count < open.Count)
        {
            open = WithoutCheapestToLose(free, open, open.Count - free.Count);
        }

        var pairs = new List<(int Cost, int SlotNumber, Guid Id)>(free.Count * open.Count);

        foreach (var player in free)
        {
            foreach (var slot in open)
            {
                pairs.Add((CostOf(player, slot), slot.SlotNumber, player.Id));
            }
        }

        pairs.Sort(static (left, right) =>
        {
            var byCost = left.Cost.CompareTo(right.Cost);

            if (byCost != 0)
            {
                return byCost;
            }

            var bySlot = left.SlotNumber.CompareTo(right.SlotNumber);

            return bySlot != 0 ? bySlot : left.Id.CompareTo(right.Id);
        });

        foreach (var (_, slotNumber, id) in pairs)
        {
            if (assigned.ContainsKey(id) || taken.Contains(slotNumber))
            {
                continue;
            }

            assigned[id] = slotNumber;
            taken.Add(slotNumber);
        }

        Improve(free, open, assigned);

        return assigned;
    }

    /// <summary>How well a player suits a slot, as a basis-point multiplier: the engine's familiarity grading (`INS-10`).</summary>
    /// <param name="player">The player.</param>
    /// <param name="slot">The slot.</param>
    public static int FamiliarityOf(ShapeFitPlayer player, FormationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(slot);

        var natural = RolePositions.NaturalFor(slot.Role);

        if (PlayerPositions.FamilyOf(player.Position) == slot.PositionFamily)
        {
            return natural.Contains(player.Position) ? Certain : UnfamiliarRoleBasisPoints;
        }

        if (natural.Contains(player.Position)
            || player.Secondary.Any(position => PlayerPositions.FamilyOf(position) == slot.PositionFamily || natural.Contains(position)))
        {
            return SecondaryPositionBasisPoints;
        }

        return OutOfPositionBasisPoints;
    }

    /// <summary>What it costs to put a player in a slot: his unfamiliarity with it, weighted, plus how far it is from where he stands.</summary>
    /// <param name="player">The player and the slot he fills now.</param>
    /// <param name="slot">The slot he would take.</param>
    public static int CostOf(ShapeFitPlayer player, FormationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(slot);

        var unfamiliarity = Certain - FamiliarityOf(player, slot);
        var along = Math.Abs(player.Standing.NormalizedX - slot.NormalizedX);
        var across = Math.Abs(player.Standing.NormalizedY - slot.NormalizedY) * LateralDistancePercent / 100;

        return (unfamiliarity * FamiliarityWeight) + along + across;
    }

    /// <summary>
    /// Swaps two players' slots whenever that lowers what the two cost together, until no swap does. The greedy pass takes the cheapest pairs
    /// first, so the last slot goes to whoever is left; a pairwise swap can put a winger where a striker was left. Players are visited in slot
    /// order and the first strictly improving swap is taken, so the answer does not depend on the order the caller lists anyone in, and every
    /// swap lowers the total, so it ends.
    /// </summary>
    private static void Improve(List<ShapeFitPlayer> free, List<FormationSlot> open, Dictionary<Guid, int> assigned)
    {
        var slotOf = open.ToDictionary(slot => slot.SlotNumber);
        var placed = free.Where(player => assigned.ContainsKey(player.Id)).ToList();
        var improved = true;

        while (improved)
        {
            improved = false;

            var order = placed.OrderBy(player => assigned[player.Id]).ToList();

            for (var left = 0; left < order.Count && !improved; left++)
            {
                for (var right = left + 1; right < order.Count && !improved; right++)
                {
                    var one = order[left];
                    var other = order[right];
                    var oneSlot = slotOf[assigned[one.Id]];
                    var otherSlot = slotOf[assigned[other.Id]];

                    var before = CostOf(one, oneSlot) + CostOf(other, otherSlot);
                    var after = CostOf(one, otherSlot) + CostOf(other, oneSlot);

                    if (after < before)
                    {
                        assigned[one.Id] = otherSlot.SlotNumber;
                        assigned[other.Id] = oneSlot.SlotNumber;
                        improved = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Places the manager's own choices first, in slot order then identity order. A choice is ignored when its player has left the pitch, is the
    /// goalkeeper, names the goalkeeper's slot or a slot that does not exist, or when the slot is already taken.
    /// </summary>
    private static void PlaceOverrides(
        List<ShapeFitPlayer> players,
        List<FormationSlot> slots,
        IReadOnlyDictionary<Guid, int>? overrides,
        FormationSlot? keeperSlot,
        Dictionary<Guid, int> assigned,
        HashSet<int> taken)
    {
        if (overrides is null)
        {
            return;
        }

        var onPitch = players.Select(player => player.Id).ToHashSet();

        foreach (var (id, slotNumber) in overrides.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            if (!onPitch.Contains(id)
                || assigned.ContainsKey(id)
                || taken.Contains(slotNumber)
                || slotNumber == keeperSlot?.SlotNumber
                || !slots.Any(slot => slot.SlotNumber == slotNumber))
            {
                continue;
            }

            assigned[id] = slotNumber;
            taken.Add(slotNumber);
        }
    }

    /// <summary>
    /// Leaves some slots empty when the side is a player short: the ones that are cheapest to lose. A slot's loss costs least when even its best
    /// candidate is a poor fit for it, so the slots to drop are those whose cheapest candidate costs most, the most advanced first when that ties
    /// (a side down to ten drops a forward), then the lower slot number.
    /// </summary>
    private static List<FormationSlot> WithoutCheapestToLose(List<ShapeFitPlayer> free, List<FormationSlot> open, int drop)
    {
        var dropped = open
            .Select(slot => (Slot: slot, Best: free.Count == 0 ? 0 : free.Min(player => CostOf(player, slot))))
            .OrderByDescending(item => item.Best)
            .ThenByDescending(item => item.Slot.NormalizedX)
            .ThenBy(item => item.Slot.SlotNumber)
            .Take(drop)
            .Select(item => item.Slot.SlotNumber)
            .ToHashSet();

        return [.. open.Where(slot => !dropped.Contains(slot.SlotNumber))];
    }
}
