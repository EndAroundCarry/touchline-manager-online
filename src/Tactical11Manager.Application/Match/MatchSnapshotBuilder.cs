using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.MatchEngine;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.Application.Match;

/// <summary>Why the builder put somebody in a slot the club did not put them in (`DIS-6`, `DIS-7`).</summary>
public enum SnapshotRepairReason
{
    /// <summary>The club named nobody for the slot.</summary>
    SlotEmpty = 0,

    /// <summary>The named player has an open injury or suspension (`TRN-12`, `DIS-5`).</summary>
    PlayerUnavailable = 1,

    /// <summary>The named player is no longer a contracted, registered member of the club (`SQ-6`).</summary>
    PlayerIneligible = 2,

    /// <summary>The named player is already named in another slot of the same side (`SQ-4`).</summary>
    PlayerDuplicated = 3,

    /// <summary>The goalkeeping slot was not filled by a recognised goalkeeper (`SQ-2`).</summary>
    GoalkeeperRequired = 4,

    /// <summary>A recognised goalkeeper was named outside the goalkeeping slot (the eleven fields one).</summary>
    GoalkeeperSurplus = 5,
}

/// <summary>Stable codes for <see cref="SnapshotRepairReason"/>.</summary>
public static class SnapshotRepairReasons
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Converts a reason to its stable code.</summary>
    /// <param name="reason">The repair reason.</param>
    public static string ToCode(this SnapshotRepairReason reason) => reason switch
    {
        SnapshotRepairReason.SlotEmpty => "slot_empty",
        SnapshotRepairReason.PlayerUnavailable => "player_unavailable",
        SnapshotRepairReason.PlayerIneligible => "player_ineligible",
        SnapshotRepairReason.PlayerDuplicated => "player_duplicated",
        SnapshotRepairReason.GoalkeeperRequired => "goalkeeper_required",
        SnapshotRepairReason.GoalkeeperSurplus => "goalkeeper_surplus",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown repair reason."),
    };

    /// <summary>Parses a stable code back to its reason.</summary>
    /// <param name="code">The stable code.</param>
    public static SnapshotRepairReason FromCode(string code) => code switch
    {
        "slot_empty" => SnapshotRepairReason.SlotEmpty,
        "player_unavailable" => SnapshotRepairReason.PlayerUnavailable,
        "player_ineligible" => SnapshotRepairReason.PlayerIneligible,
        "player_duplicated" => SnapshotRepairReason.PlayerDuplicated,
        "goalkeeper_required" => SnapshotRepairReason.GoalkeeperRequired,
        "goalkeeper_surplus" => SnapshotRepairReason.GoalkeeperSurplus,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown repair reason code."),
    };
}

/// <summary>One slot the builder decided rather than the club (`DIS-6`, `DIS-7`).</summary>
/// <param name="ClubId">
/// The club whose slot was decided. Carried because a snapshot holds two sides, and "slot 5 was repaired"
/// names two different players without it — and because the manager the repair is reported to (DIS-7) is the
/// one whose side it was.
/// </param>
/// <param name="SlotNumber">The slot the decision was made in, 1–18.</param>
/// <param name="Reason">Why it was not the club's choice.</param>
/// <param name="ReplacedPlayerId">The player the club named, when it named one.</param>
/// <param name="ReplacementPlayerId">The player the builder picked, or null when the slot was left empty.</param>
public sealed record SnapshotRepair(
    Guid ClubId,
    int SlotNumber,
    SnapshotRepairReason Reason,
    Guid? ReplacedPlayerId,
    Guid? ReplacementPlayerId);

/// <summary>The frozen input a fixture is simulated from, before the seed is derived.</summary>
/// <param name="Input">The engine's input, which carries no seed yet.</param>
/// <param name="Repairs">Every slot the builder decided, in slot order.</param>
public sealed record BuiltSnapshot(MatchInputV1 Input, IReadOnlyList<SnapshotRepair> Repairs);

/// <summary>
/// Raised when a club cannot field a legal side at all, so no snapshot can be frozen.
/// </summary>
/// <remarks>
/// A distinct type because it is not a defect: a club with no available goalkeeper is a state the game can
/// genuinely reach, and the honest answer is to refuse and let operations see it rather than to invent a
/// side the rules do not allow or a forfeit the rules do not have (`MAT-10`, §7.4.9). The message names
/// the club and the reason, and the workflow turns it into a dead-lettered job with an alert.
/// </remarks>
public sealed class UnplayableSquadException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="clubId">The club that cannot field a side.</param>
    /// <param name="reason">What is missing.</param>
    public UnplayableSquadException(Guid clubId, string reason)
        : base($"Club {clubId:D} cannot field a legal side: {reason}")
    {
        ClubId = clubId;
    }

    /// <summary>Gets the club that cannot field a side.</summary>
    public Guid ClubId { get; }
}

/// <summary>
/// Builds a fixture's immutable input from what its two clubs have prepared (`MAT-1`, `DIS-6`).
/// </summary>
/// <remarks>
/// <para>
/// One pure function over facts, so the same clubs and the same prepared sheets always produce the same
/// snapshot, and a result can always be explained by what was frozen rather than by what a table happened
/// to hold. It takes no clock, no repository, and no randomness: the seed is derived afterwards, from the
/// snapshot itself (master plan §8.2).
/// </para>
/// <para>
/// <b>Selection.</b> A club's fixture sheet is honoured slot by slot. A slot the sheet did not fill, or
/// filled with somebody who cannot play in it, is decided by the builder in a documented order: position
/// suitability, then condition, then ability, then the player's identity as the tie-break (`DIS-6`). The
/// club's own plan supplies the shape and the instructions; a club that has saved no plan takes the field
/// in the default formation with the neutral instructions, which is what makes the world playable before a
/// manager has opened the tactics screen.
/// </para>
/// <para>
/// <b>Goalkeepers.</b> The eleven must contain exactly one recognised goalkeeper, because the engine
/// refuses anything else: a side without one has an empty goalkeeping unit rather than a makeshift keeper,
/// and a side with two has one of them out of position in a way the contract does not express. A goalkeeper
/// is therefore only ever placed in the goalkeeping slot, and a club with no available goalkeeper has no
/// legal side and is refused by name.
/// </para>
/// <para>
/// <b>Repairs.</b> Every decision the builder made is recorded — including the ones that merely filled a
/// slot the club left empty — so "why did my striker play at left back?" is answered by the record rather
/// than by a log line (`DIS-7`).
/// </para>
/// </remarks>
public static class MatchSnapshotBuilder
{
    /// <summary>The formation a club with no saved plan takes the field in.</summary>
    public const FormationPreset DefaultFormation = FormationPreset.FourFourTwo;

    /// <summary>Builds the frozen input for one fixture.</summary>
    /// <param name="sides">Both clubs, with everything their sides are built from.</param>
    /// <param name="rules">The engine rules in force. Its hash is recorded in the snapshot.</param>
    /// <returns>The input, which carries no seed yet, and every repair the builder made.</returns>
    /// <exception cref="UnplayableSquadException">When a club cannot field a legal side.</exception>
    /// <exception cref="InvalidMatchInputException">When the built input would not simulate.</exception>
    public static BuiltSnapshot Build(FixtureSidesSnapshot sides, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(sides);
        ArgumentNullException.ThrowIfNull(rules);

        var home = BuildSide(sides.Home);
        var away = BuildSide(sides.Away);

        var input = new MatchInputV1
        {
            FixtureId = sides.FixtureId,
            WorldId = sides.WorldId,
            SeasonId = sides.SeasonId,
            EngineVersion = EngineVersions.EngineLabel,
            RuleSetVersion = EngineVersions.RuleSetLabel,
            Home = home.Side,
            Away = away.Side,
            HomeAdvantageBasisPoints = rules.HomeAdvantageBasisPoints,
            FormulaConfigurationHash = EngineConfiguration.HashOf(rules),

            // The seed is derived from the snapshot's own content, so it cannot be part of building one.
            // The caller attaches it, which is what makes the derivation acyclic (master plan §8.2).
            Seed = 0,
        };

        // The engine's own front door, called here rather than trusted: a side that would be refused at
        // simulation time is refused at lock time, when the club can still do something about it.
        input.Validate();

        return new BuiltSnapshot(input, [.. home.Repairs, .. away.Repairs]);
    }

    private static BuiltSide BuildSide(ClubSideSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<SnapshotSlotRow> slots = source.Slots.Count > 0
            ? [.. source.Slots.OrderBy(slot => slot.SlotNumber)]
            : [.. FormationLayouts
                .DefaultSlots(DefaultFormation)
                .Select(slot => new SnapshotSlotRow(
                    slot.SlotNumber,
                    slot.PositionFamily,
                    slot.Role,
                    slot.NormalizedX,
                    slot.NormalizedY))];

        if (slots.Count != WorldRuleSet.TeamSheetStarters)
        {
            throw new UnplayableSquadException(
                source.ClubId,
                $"its plan names {slots.Count} starting slots instead of {WorldRuleSet.TeamSheetStarters} (TAC-8).");
        }

        var players = source.Players.ToDictionary(player => player.PlayerId);
        var selection = source.Selection.ToDictionary(entry => entry.SlotNumber);

        var placed = new Dictionary<int, Guid>();
        var used = new HashSet<Guid>();
        var repairs = new List<SnapshotRepair>();

        // Pass one: what the club prepared, in slot order, keeping only what may actually be fielded.
        foreach (var slot in slots)
        {
            if (!selection.TryGetValue(slot.SlotNumber, out var entry))
            {
                continue;
            }

            var reason = RejectionOf(slot, entry.PlayerId, players, used);

            if (reason is not null)
            {
                repairs.Add(new SnapshotRepair(source.ClubId, slot.SlotNumber, reason.Value, entry.PlayerId, null));

                continue;
            }

            placed[slot.SlotNumber] = entry.PlayerId;
            used.Add(entry.PlayerId);
        }

        // Pass two: every slot still open, in slot order, from what is left.
        foreach (var slot in slots)
        {
            if (placed.ContainsKey(slot.SlotNumber))
            {
                continue;
            }

            var replacement = ChooseFor(slot, players, used, source.ClubId);

            if (!selection.ContainsKey(slot.SlotNumber))
            {
                repairs.Add(new SnapshotRepair(
                    source.ClubId,
                    slot.SlotNumber,
                    SnapshotRepairReason.SlotEmpty,
                    null,
                    replacement));
            }

            placed[slot.SlotNumber] = replacement;
            used.Add(replacement);
        }

        var bench = ResolveBench(source, players, used, repairs);

        var side = new MatchSideV1
        {
            ClubId = source.ClubId,
            ClubName = source.ClubName,
            PrimaryColour = source.PrimaryColour,
            SecondaryColour = source.SecondaryColour,
            Instructions = EngineVocabulary.Instructions(source.Instructions ?? TeamInstructionSet.Neutral),
            Squad = [.. Squad(source, players, slots, placed, bench)],
            Slots = [.. Slots(selection, slots, placed)],
        };

        side = side with { MatchPlan = BuildPlan(source, [.. side.Squad.Select(participant => participant.ParticipantId)]) };

        // A repair knows which player took the slot it opened, including the ones whose club choice was
        // rejected — the replacement is chosen in pass two, after the rejection was recorded.
        var completed = repairs
            .Select(repair => repair.ReplacementPlayerId is null
                && placed.TryGetValue(repair.SlotNumber, out var replacement)
                    ? repair with { ReplacementPlayerId = replacement }
                    : repair)
            .OrderBy(repair => repair.SlotNumber)
            .ToList();

        return new BuiltSide(side, completed);
    }

    /// <summary>
    /// Turns the plan's match instructions into the engine's match plan (`MAT-6`).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A side whose plan has no enabled instruction and leaves the assistant on gets <see cref="MatchPlanV1.None"/>, which is what every side
    /// had before the feature, so its snapshot hashes as it always did. An instruction that is off is left out, and the ones that remain keep
    /// the place the manager gave them (so the match event can say which rule fired).
    /// </para>
    /// <para>
    /// The squad is who this sheet fields: its eleven and its bench. A placement or a named substitution that refers to anyone else is dropped,
    /// because the engine refuses a plan that names a stranger and the club can no longer do anything about it at lock time. An instruction that
    /// ends up changing nothing is dropped too.
    /// </para>
    /// </remarks>
    private static MatchPlanV1 BuildPlan(ClubSideSource source, HashSet<Guid> squad) =>
        PlanOf(source.MatchInstructions, source.AssistantFatigueSubstitutions, squad);

    /// <summary>
    /// Turns a list of match instructions into the engine's match plan, as a side's plan is when its snapshot is built. Public so the
    /// laboratory can give a lab side the plan the AI would write for it and play it exactly as a fixture would.
    /// </summary>
    /// <param name="matchInstructions">The instructions, in the order they are checked, or null for none.</param>
    /// <param name="assistantFatigueSubstitutions">Whether the assistant still makes the fatigue substitutions.</param>
    /// <param name="squad">The participants this sheet fields, which a named player has to be one of.</param>
    public static MatchPlanV1 PlanOf(
        IReadOnlyList<MatchInstructionDefinition>? matchInstructions,
        bool assistantFatigueSubstitutions,
        HashSet<Guid> squad)
    {
        var rules = new List<MatchRuleV1>();
        var instructions = matchInstructions ?? [];

        for (var index = 0; index < instructions.Count && rules.Count < MatchPlanV1.MaxRules; index++)
        {
            if (!instructions[index].IsEnabled)
            {
                continue;
            }

            if (BuildRule(instructions[index], index + 1, squad) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules.Count == 0 && assistantFatigueSubstitutions
            ? MatchPlanV1.None
            : new MatchPlanV1 { Rules = rules, AssistantFatigueSubstitutions = assistantFatigueSubstitutions };
    }

    private static MatchRuleV1? BuildRule(MatchInstructionDefinition instruction, int ordinal, HashSet<Guid> squad)
    {
        // The opponent's mentality arrives as the domain's number and leaves as the engine's.
        int? threshold = instruction.EventThreshold;

        if (instruction.Event is MatchInstructionEvent.OpponentMentalityAtLeast or MatchInstructionEvent.OpponentMentalityAtMost)
        {
            if (threshold is not { } level || !Enum.IsDefined((Mentality)level))
            {
                return null;
            }

            threshold = (int)EngineVocabulary.MentalityOf((Mentality)level);
        }

        List<MatchSlotV1>? slots = instruction.Formation is { } formation
            ? [.. FormationLayouts.DefaultSlots(formation).Select(slot => new MatchSlotV1
            {
                SlotNumber = slot.SlotNumber,
                Family = EngineVocabulary.Family(slot.PositionFamily),
                Role = EngineVocabulary.Role(slot.Role),
                X = slot.NormalizedX,
                Y = slot.NormalizedY,

                // The new shape is a set of places, not of people: the engine fits whoever is on the pitch when the rule fires.
                ParticipantId = Guid.Empty,
            })]
            : null;

        var placed = new HashSet<Guid>();
        var taken = new HashSet<int>();

        var overrides = slots is null
            ? []
            : instruction.Overrides
                .Where(placement => squad.Contains(placement.PlayerId)
                    && placement.SlotNumber is > TacticalSlot.FirstSlotNumber and <= TacticalSlot.LastSlotNumber
                    && placed.Add(placement.PlayerId)
                    && taken.Add(placement.SlotNumber))
                .Select(placement => new MatchSlotOverrideV1(placement.PlayerId, placement.SlotNumber))
                .ToList();

        var substitutions = instruction.Substitutions
            .Where(substitution =>
                (substitution.Off != MatchInstructionOff.Player || (substitution.OffPlayerId is { } off && squad.Contains(off)))
                && (substitution.On != MatchInstructionOn.Player || (substitution.OnPlayerId is { } on && squad.Contains(on))))
            .Take(MatchPlanV1.MaxSubstitutionsPerRule)
            .Select(substitution => new MatchRuleSubstitutionV1
            {
                Off = EngineVocabulary.RuleOff(substitution.Off),
                OffParticipantId = substitution.Off == MatchInstructionOff.Player ? substitution.OffPlayerId : null,
                OffFamily = substitution.OffFamily is { } offFamily ? EngineVocabulary.Family(offFamily) : null,
                On = EngineVocabulary.RuleOn(substitution.On),
                OnParticipantId = substitution.On == MatchInstructionOn.Player ? substitution.OnPlayerId : null,
                OnFamily = substitution.OnFamily is { } onFamily ? EngineVocabulary.Family(onFamily) : null,
            })
            .ToList();

        var changes = EngineVocabulary.InstructionChanges(instruction.Changes ?? MatchInstructionChanges.None);

        if (slots is null && changes is null && substitutions.Count == 0)
        {
            return null;
        }

        return new MatchRuleV1
        {
            Ordinal = ordinal,
            Name = string.IsNullOrWhiteSpace(instruction.Name) ? null : instruction.Name,
            FromMinute = instruction.FromMinute,
            UntilMinute = instruction.UntilMinute,
            Score = EngineVocabulary.RuleScore(instruction.Score),
            ScoreMargin = instruction.ScoreMargin,
            Event = EngineVocabulary.RuleEvent(instruction.Event),
            EventFamily = instruction.EventFamily is { } family ? EngineVocabulary.Family(family) : null,
            EventThreshold = threshold,
            FormationCode = instruction.Formation?.ToCode(),
            Slots = slots,
            Overrides = overrides,
            Instructions = changes,
            Substitutions = substitutions,
        };
    }

    private static IEnumerable<MatchSlotV1> Slots(
        Dictionary<int, SnapshotSelectionRow> selection,
        List<SnapshotSlotRow> slots,
        Dictionary<int, Guid> placed) =>
        slots.Select(slot =>
        {
            // A role override is part of the side only when it agrees with its slot's family, which is the
            // same rule the save path enforces (TAC-8): a snapshot may not contradict the formation it was
            // prepared against, even if a stored row somehow does.
            var role = selection.TryGetValue(slot.SlotNumber, out var entry)
                && entry.RoleOverride is { } roleOverride
                && PlayerRoles.FamilyOf(roleOverride) == slot.PositionFamily
                    ? roleOverride
                    : slot.Role;

            return new MatchSlotV1
            {
                SlotNumber = slot.SlotNumber,
                Family = EngineVocabulary.Family(slot.PositionFamily),
                Role = EngineVocabulary.Role(role),
                X = slot.NormalizedX,
                Y = slot.NormalizedY,
                ParticipantId = placed[slot.SlotNumber],
            };
        });

    private static IEnumerable<MatchParticipantV1> Squad(
        ClubSideSource source,
        Dictionary<Guid, SnapshotPlayerRow> players,
        List<SnapshotSlotRow> slots,
        Dictionary<int, Guid> placed,
        List<(int SlotNumber, Guid PlayerId)> bench)
    {
        foreach (var slot in slots)
        {
            yield return Participant(source.ClubId, players[placed[slot.SlotNumber]], slot.SlotNumber);
        }

        foreach (var (slotNumber, playerId) in bench)
        {
            yield return Participant(source.ClubId, players[playerId], slotNumber);
        }
    }

    /// <summary>Decides whether the club's own choice for a slot can be fielded, and why not if it cannot.</summary>
    private static SnapshotRepairReason? RejectionOf(
        SnapshotSlotRow slot,
        Guid playerId,
        Dictionary<Guid, SnapshotPlayerRow> players,
        HashSet<Guid> used)
    {
        if (!players.TryGetValue(playerId, out var player))
        {
            return SnapshotRepairReason.PlayerIneligible;
        }

        if (!player.IsAvailable)
        {
            return SnapshotRepairReason.PlayerUnavailable;
        }

        if (used.Contains(playerId))
        {
            return SnapshotRepairReason.PlayerDuplicated;
        }

        var isGoalkeeper = player.PrimaryPosition == PlayerPosition.Goalkeeper;
        var wantsGoalkeeper = slot.PositionFamily == PositionFamily.Goalkeeper;

        if (wantsGoalkeeper && !isGoalkeeper)
        {
            return SnapshotRepairReason.GoalkeeperRequired;
        }

        return !wantsGoalkeeper && isGoalkeeper ? SnapshotRepairReason.GoalkeeperSurplus : null;
    }

    /// <summary>
    /// Picks the player the builder would field in a slot, by suitability, condition, ability, and identity
    /// (`DIS-6`).
    /// </summary>
    private static Guid ChooseFor(
        SnapshotSlotRow slot,
        Dictionary<Guid, SnapshotPlayerRow> players,
        HashSet<Guid> used,
        Guid clubId)
    {
        var wantsGoalkeeper = slot.PositionFamily == PositionFamily.Goalkeeper;

        var candidates = players.Values
            .Where(player => !used.Contains(player.PlayerId))
            .Where(player => player.IsAvailable)
            .Where(player => wantsGoalkeeper
                ? player.PrimaryPosition == PlayerPosition.Goalkeeper
                : player.PrimaryPosition != PlayerPosition.Goalkeeper)
            .OrderByDescending(player => Suitability(player, slot))
            .ThenByDescending(player => player.ConditionBp)
            .ThenByDescending(Ability)
            .ThenBy(player => player.PlayerId)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new UnplayableSquadException(
                clubId,
                wantsGoalkeeper
                    ? "it has no available recognised goalkeeper (SQ-2)."
                    : "it has no available outfield player left to fill its eleven.");
        }

        return candidates[0].PlayerId;
    }

    /// <summary>
    /// Fills the bench: what the club named, then the next best available players up to seven.
    /// </summary>
    /// <remarks>
    /// The bench is not a rule the engine enforces — a side may name none — but a side with no substitutes
    /// has no answer to an injury or to tired legs, and the engine's substitution planner can only choose
    /// from what it is given. Filling it deterministically is what makes an untended club's match a match.
    /// At most one of the places goes to a goalkeeper, so the bench covers the one position whose injury
    /// would otherwise be unanswerable.
    /// </remarks>
    private static List<(int SlotNumber, Guid PlayerId)> ResolveBench(
        ClubSideSource source,
        Dictionary<Guid, SnapshotPlayerRow> players,
        HashSet<Guid> used,
        List<SnapshotRepair> repairs)
    {
        var bench = new List<(int SlotNumber, Guid PlayerId)>();
        var hasGoalkeeper = false;

        foreach (var entry in source.Selection
            .Where(entry => entry.SlotNumber > WorldRuleSet.TeamSheetStarters)
            .OrderBy(entry => entry.SlotNumber))
        {
            if (!players.TryGetValue(entry.PlayerId, out var player))
            {
                repairs.Add(new SnapshotRepair(
                    source.ClubId,
                    entry.SlotNumber,
                    SnapshotRepairReason.PlayerIneligible,
                    entry.PlayerId,
                    null));

                continue;
            }

            var reason = !player.IsAvailable
                ? SnapshotRepairReason.PlayerUnavailable
                : used.Contains(entry.PlayerId) || bench.Count >= WorldRuleSet.TeamSheetSubstitutes
                    ? SnapshotRepairReason.PlayerDuplicated
                    : (SnapshotRepairReason?)null;

            if (reason is not null)
            {
                repairs.Add(new SnapshotRepair(source.ClubId, entry.SlotNumber, reason.Value, entry.PlayerId, null));

                continue;
            }

            hasGoalkeeper |= player.PrimaryPosition == PlayerPosition.Goalkeeper;
            bench.Add((entry.SlotNumber, entry.PlayerId));
            used.Add(entry.PlayerId);
        }

        // The slot numbers the club left free, taken in order as candidates are added. A candidate that is
        // passed over — a second goalkeeper — therefore costs a place rather than a shirt number.
        var free = Enumerable
            .Range(
                WorldRuleSet.TeamSheetStarters + 1,
                WorldRuleSet.TeamSheetSubstitutes)
            .Where(slotNumber => bench.All(place => place.SlotNumber != slotNumber))
            .ToList();

        var added = 0;

        var reserve = players.Values
            .Where(player => !used.Contains(player.PlayerId) && player.IsAvailable)
            .OrderByDescending(Ability)
            .ThenByDescending(player => player.ConditionBp)
            .ThenBy(player => player.PlayerId)
            .ToList();

        foreach (var candidate in reserve)
        {
            if (bench.Count >= WorldRuleSet.TeamSheetSubstitutes || added >= free.Count)
            {
                break;
            }

            var isGoalkeeper = candidate.PrimaryPosition == PlayerPosition.Goalkeeper;

            if (isGoalkeeper && hasGoalkeeper)
            {
                continue;
            }

            hasGoalkeeper |= isGoalkeeper;

            var slotNumber = free[added];

            added++;
            bench.Add((slotNumber, candidate.PlayerId));
            repairs.Add(new SnapshotRepair(
                source.ClubId,
                slotNumber,
                SnapshotRepairReason.SlotEmpty,
                null,
                candidate.PlayerId));

            used.Add(candidate.PlayerId);
        }

        return bench;
    }

    /// <summary>
    /// How at home a player is in a slot: 3 for the position the role asks for, 2 for another in its family,
    /// 1 for one they cover, 0 for neither (`INS-10`'s familiarity, used here only to choose between
    /// candidates).
    /// </summary>
    private static int Suitability(SnapshotPlayerRow player, SnapshotSlotRow slot) =>
        RolePositions.Suitability(player.PrimaryPosition, player.SecondaryPositions, slot.PositionFamily, slot.Role);

    /// <summary>
    /// A player's standing among their team-mates, as the mean of their twenty-eight attributes.
    /// </summary>
    /// <remarks>
    /// Deliberately not an "overall": the glossary is explicit that no single number is authoritative, and
    /// this one is only ever used to separate two candidates that suitability and condition have already
    /// failed to separate. It is a squad-ranking yardstick, not a displayed rating.
    /// </remarks>
    private static int Ability(SnapshotPlayerRow player)
    {
        if (player.Attributes.Count == 0)
        {
            return 0;
        }

        var total = 0;

        foreach (var value in player.Attributes)
        {
            total += value;
        }

        return total / player.Attributes.Count;
    }

    private static MatchParticipantV1 Participant(Guid clubId, SnapshotPlayerRow player, int shirtNumber) => new()
    {
        // A player appears once in a match, so the participant and the player share an identity. The two
        // fields exist separately because the engine's contract allows a participant that is not a squad
        // row; nothing in this world produces one, and stored events refer to this value.
        ParticipantId = player.PlayerId,
        PlayerId = player.PlayerId,
        ClubId = clubId,
        DisplayName = player.FullName,
        ShirtNumber = shirtNumber,
        Position = EngineVocabulary.Position(player.PrimaryPosition),
        SecondaryPositions = [.. player.SecondaryPositions.Select(EngineVocabulary.Position)],
        Attributes = PlayerAttributesV1.From(player.Attributes),
        State = new PlayerMatchStateV1
        {
            ConditionBasisPoints = player.ConditionBp,
            FatigueBasisPoints = player.FatigueBp,
            MoraleBasisPoints = player.MoraleBp,
            SharpnessBasisPoints = player.MatchSharpnessBp,
        },
    };

    /// <summary>One side as the builder assembled it, with the repairs that got it there.</summary>
    private sealed record BuiltSide(MatchSideV1 Side, IReadOnlyList<SnapshotRepair> Repairs);
}
