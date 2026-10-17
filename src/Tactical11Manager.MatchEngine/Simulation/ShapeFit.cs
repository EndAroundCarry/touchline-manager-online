using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Ratings;

namespace Tactical11Manager.MatchEngine.Simulation;

/// <summary>
/// Fits the players on the pitch into a new formation: who takes which slot when a match rule changes the shape.
/// </summary>
/// <remarks>
/// <para>
/// Pure and without a random draw, so the same players and the same shape always give the same answer. It is a greedy assignment followed by a swap
/// pass: every (player, slot) pair has a cost, pairs are taken cheapest first, and then any two players who cost less swapped trade
/// places (<see cref="Improve"/>). The cost is the player's unfamiliarity with the slot, weighted
/// by <see cref="FamiliarityWeight"/>, plus how far the slot is from where he stands now, so a winger stays out wide and a centre back
/// stays at the back unless the shape asks otherwise.
/// </para>
/// <para>
/// The same algorithm is written three times (the domain's snapshot builder, the tactics tab's live preview and this engine, which
/// falls back on it when the players on the pitch are not the ones the plan was made for). One table of cases pins all three
/// (`shape-fit-cases.json`).
/// </para>
/// </remarks>
internal static class ShapeFit
{
    /// <summary>
    /// How many times a basis point of unfamiliarity (the gap to a perfect fit) weighs against a unit of the 0…10,000 board
    /// distance: 5. The rules' penalties are small (a player out of position plays at 88%, a gap of 1,200), so at 5 being out of
    /// position costs as much as a walk of 60% of the pitch's length, a secondary position as much as 20%.
    /// </summary>
    public const int FamiliarityWeight = 5;

    /// <summary>
    /// How much a unit across the pitch counts against a unit along it, in percent. The pitch is 68 m wide and 105 m long, so a unit
    /// across is two thirds of a unit along.
    /// </summary>
    public const int LateralDistancePercent = 65;

    /// <summary>Fits the players on the pitch into a shape.</summary>
    /// <param name="onPitch">The players on the pitch, with the slots they fill now.</param>
    /// <param name="newSlots">The new shape's slots, numbered from 1; the slot with the goalkeeper's family is his.</param>
    /// <param name="overrides">The manager's own placements, new slot number by participant, or null. They are placed first.</param>
    /// <param name="rules">The rules in force, which supply the familiarity penalties.</param>
    /// <returns>
    /// The new slot number each placed player takes. When there are fewer players than slots, the slots nobody takes are missing from
    /// the values: the ones cheapest to lose, the most advanced first.
    /// </returns>
    public static Dictionary<Guid, int> Fit(
        IReadOnlyList<ActiveSlot> onPitch,
        IReadOnlyList<MatchSlotV1> newSlots,
        IReadOnlyDictionary<Guid, int>? overrides,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(onPitch);
        ArgumentNullException.ThrowIfNull(newSlots);
        ArgumentNullException.ThrowIfNull(rules);

        var slots = newSlots.OrderBy(slot => slot.SlotNumber).ToList();

        if (slots.Select(slot => slot.SlotNumber).Distinct().Count() != slots.Count)
        {
            throw new ArgumentException("The new shape's slot numbers must be distinct.", nameof(newSlots));
        }

        var assigned = new Dictionary<Guid, int>();
        var taken = new HashSet<int>();

        // The goalkeeper stays in goal: the first slot of his family is his. If he has gone, nobody is put there.
        var keeperSlot = slots.FirstOrDefault(slot => slot.Family == MatchPositionFamily.Goalkeeper);
        var keeper = onPitch.FirstOrDefault(active => active.Slot.Family == MatchPositionFamily.Goalkeeper);

        if (keeperSlot is not null)
        {
            taken.Add(keeperSlot.SlotNumber);

            if (keeper is not null)
            {
                assigned[keeper.Participant.ParticipantId] = keeperSlot.SlotNumber;
            }
        }

        var players = onPitch
            .Where(active => active.Participant.ParticipantId != keeper?.Participant.ParticipantId)
            .OrderBy(active => active.Participant.ParticipantId)
            .ToList();

        PlaceOverrides(players, slots, overrides, keeperSlot, assigned, taken);

        var free = players.Where(active => !assigned.ContainsKey(active.Participant.ParticipantId)).ToList();
        var open = slots.Where(slot => !taken.Contains(slot.SlotNumber)).ToList();

        if (free.Count < open.Count)
        {
            open = WithoutCheapestToLose(free, open, open.Count - free.Count, rules);
        }

        var pairs = new List<(int Cost, int SlotNumber, Guid Id)>(free.Count * open.Count);

        foreach (var player in free)
        {
            foreach (var slot in open)
            {
                pairs.Add((CostOf(player, slot, rules), slot.SlotNumber, player.Participant.ParticipantId));
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

        Improve(free, open, assigned, rules);

        return assigned;
    }

    /// <summary>What it costs to put a player in a slot: his unfamiliarity with it, weighted, plus how far it is from where he stands.</summary>
    /// <param name="player">The player and the slot he fills now.</param>
    /// <param name="slot">The slot he would take.</param>
    /// <param name="rules">The rules in force.</param>
    public static int CostOf(ActiveSlot player, MatchSlotV1 slot, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(rules);

        var unfamiliarity = EngineRulesV2.Certain - LineupResolver.FamiliarityOf(player.Participant, slot, rules);
        var along = Math.Abs(player.Slot.X - slot.X);
        var across = Math.Abs(player.Slot.Y - slot.Y) * LateralDistancePercent / 100;

        return (unfamiliarity * FamiliarityWeight) + along + across;
    }

    /// <summary>
    /// Swaps two players' slots whenever that lowers what the two cost together, until no swap does. The greedy pass takes the cheapest
    /// pairs first, so the last slot goes to whoever is left, and he can be a striker in a holding midfielder's place when a
    /// pairwise swap would have put a winger there. Players are visited in slot order and the first swap that strictly improves is
    /// taken, so the answer does not depend on the order the caller lists anyone in, and every swap lowers the total, so it ends.
    /// </summary>
    private static void Improve(
        List<ActiveSlot> free,
        List<MatchSlotV1> open,
        Dictionary<Guid, int> assigned,
        EngineRulesV2 rules)
    {
        var slotOf = open.ToDictionary(slot => slot.SlotNumber);
        var placed = free.Where(player => assigned.ContainsKey(player.Participant.ParticipantId)).ToList();
        var improved = true;

        while (improved)
        {
            improved = false;

            var order = placed.OrderBy(player => assigned[player.Participant.ParticipantId]).ToList();

            for (var left = 0; left < order.Count && !improved; left++)
            {
                for (var right = left + 1; right < order.Count && !improved; right++)
                {
                    var one = order[left];
                    var other = order[right];
                    var oneSlot = slotOf[assigned[one.Participant.ParticipantId]];
                    var otherSlot = slotOf[assigned[other.Participant.ParticipantId]];

                    var before = CostOf(one, oneSlot, rules) + CostOf(other, otherSlot, rules);
                    var after = CostOf(one, otherSlot, rules) + CostOf(other, oneSlot, rules);

                    if (after < before)
                    {
                        assigned[one.Participant.ParticipantId] = otherSlot.SlotNumber;
                        assigned[other.Participant.ParticipantId] = oneSlot.SlotNumber;
                        improved = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Places the manager's own choices first, in slot order then identity order. A choice is ignored when its player has left the
    /// pitch, is the goalkeeper, names the goalkeeper's slot or a slot that does not exist, or when the slot is already taken.
    /// </summary>
    private static void PlaceOverrides(
        List<ActiveSlot> players,
        List<MatchSlotV1> slots,
        IReadOnlyDictionary<Guid, int>? overrides,
        MatchSlotV1? keeperSlot,
        Dictionary<Guid, int> assigned,
        HashSet<int> taken)
    {
        if (overrides is null)
        {
            return;
        }

        var onPitch = players.Select(player => player.Participant.ParticipantId).ToHashSet();

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
    /// Leaves some slots empty when the side is a player short: the ones that are cheapest to lose. A slot's loss costs least when even its
    /// best candidate is a poor fit for it, so the slots to drop are those whose cheapest candidate costs most, the most advanced first
    /// when that ties (a side down to ten drops a forward), then the lower slot number.
    /// </summary>
    private static List<MatchSlotV1> WithoutCheapestToLose(
        List<ActiveSlot> free,
        List<MatchSlotV1> open,
        int drop,
        EngineRulesV2 rules)
    {
        var dropped = open
            .Select(slot => (Slot: slot, Best: free.Count == 0 ? 0 : free.Min(player => CostOf(player, slot, rules))))
            .OrderByDescending(item => item.Best)
            .ThenByDescending(item => item.Slot.X)
            .ThenBy(item => item.Slot.SlotNumber)
            .Take(drop)
            .Select(item => item.Slot.SlotNumber)
            .ToHashSet();

        return [.. open.Where(slot => !dropped.Contains(slot.SlotNumber))];
    }
}
