using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad;

/// <summary>The versioned identity of the AI club policy (`INS-12`, `FIC-8`).</summary>
/// <remarks>
/// A changed draw order, a changed formation list, or a changed selection order is a new policy version,
/// exactly as a training or match-load change is: the version is part of the seed, so a club's decisions
/// are reproducible from this label and its identity alone.
/// </remarks>
public static class AiPolicyVersions
{
    /// <summary>The current policy label, folded into every draw the policy makes.</summary>
    public const string Version = "ai-policy-v1";
}

/// <summary>
/// One player as the AI policy weighs them (`INS-12`).
/// </summary>
/// <remarks>
/// The same facts the snapshot builder selects from — position, condition, ability, and availability — so
/// the AI's side is chosen by the same ordering a manager's repaired side is. It carries no name, wage, or
/// hidden value, because nothing the policy decides depends on them.
/// </remarks>
/// <param name="PlayerId">The player.</param>
/// <param name="PrimaryPosition">The position the player is most at home in.</param>
/// <param name="SecondaryPositions">The further positions the player covers.</param>
/// <param name="Attributes">The twenty-eight attributes, in canonical order.</param>
/// <param name="ConditionBp">Condition in basis points.</param>
/// <param name="IsAvailable">Whether the player has no open injury or suspension (`TRN-12`, `DIS-5`).</param>
public sealed record AiSquadPlayer(
    Guid PlayerId,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    IReadOnlyList<int> Attributes,
    int ConditionBp,
    bool IsAvailable);

/// <summary>One slot of an AI club's default plan, with the player the policy picked for it.</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The family the slot asks for (`TAC-8`).</param>
/// <param name="Role">The role the slot asks for (`TAC-8`).</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000 (`TAC-9`).</param>
/// <param name="NormalizedY">The normalized width, 0–10,000 (`TAC-9`).</param>
/// <param name="AssignedPlayerId">The player the policy picked, or null when no legal eleven could be fielded.</param>
public sealed record AiSlotAssignment(
    int SlotNumber,
    PositionFamily PositionFamily,
    PlayerRole Role,
    int NormalizedX,
    int NormalizedY,
    Guid? AssignedPlayerId);

/// <summary>Everything the AI decides for one club: its tactics, its default eleven, and its training.</summary>
/// <param name="Formation">The formation preset the side plays (`TAC-1`…`TAC-6`, `TAC-11`…`TAC-17`).</param>
/// <param name="Instructions">The eight team instructions (`INS-1`…`INS-8`).</param>
/// <param name="Slots">The eleven slots, in slot order, with the picked player where one was chosen.</param>
/// <param name="TrainingIntensity">How hard the club trains (`TRN-1`).</param>
public sealed record AiClubPlan(
    FormationPreset Formation,
    TeamInstructionSet Instructions,
    IReadOnlyList<AiSlotAssignment> Slots,
    TrainingIntensity TrainingIntensity);

/// <summary>
/// The deterministic policy a club nobody manages plays and trains by (`INS-12`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function of the club's identity and its squad. It reads no clock, repository, culture,
/// or global random source, so the same club always fields the same side and a world can be replayed. It is
/// seeded from the club identity rather than drawn at random so that two clubs in one world differ while a
/// given club never does.
/// </para>
/// <para>
/// The policy invents no privilege: every choice it makes is one a manager could make on the tactics and
/// training screens, and the plan it produces is accepted by the same <see cref="TacticalPlanValidator"/>
/// a human's save goes through. That is the whole of `INS-12` — the AI receives no bypass, only a
/// different author.
/// </para>
/// <para>
/// The tactics vary by club, because a division of eighteen identical 4-4-2s is not a football league; the
/// training does not, because the training choice is a persistent development path rather than a
/// match-to-match lever, and a club permanently dealt a random intensity would be a fairness problem
/// rather than variety. The AI therefore trains at <see cref="TrainingIntensity.Normal"/> and gives no
/// player a programme override, so each trains the programme matching their position
/// (<see cref="TrainingProgrammes.DefaultFor"/>) — the same rules a manager's players follow (`INS-12`).
/// </para>
/// </remarks>
public static class AiClubPolicy
{
    /// <summary>The name an AI-authored plan carries, which a takeover inherits (`WORLD-9`).</summary>
    public const string PlanName = "Default";

    /// <summary>The training intensity every AI club trains at (`TRN-1`).</summary>
    public const TrainingIntensity DefaultTrainingIntensity = TrainingIntensity.Normal;

    /// <summary>Decides a club's tactics, default eleven, and training.</summary>
    /// <param name="clubId">The club, which is the seed's identity.</param>
    /// <param name="squad">
    /// Every player the club may pick, available and unavailable alike; the policy filters availability
    /// itself, exactly as the snapshot builder does.
    /// </param>
    /// <returns>The decision. A squad that cannot field a legal eleven yields a plan with no lineup.</returns>
    public static AiClubPlan Decide(Guid clubId, IReadOnlyList<AiSquadPlayer> squad)
    {
        ArgumentNullException.ThrowIfNull(squad);

        var draws = new Pcg32(DeterministicDigest.SeedOf(AiPolicyVersions.Version, clubId.ToString("D")));

        var formation = Pick(draws, FormationPresets.AiPool);

        var instructions = new TeamInstructionSet
        {
            Mentality = Pick(draws, Enum.GetValues<Mentality>()),
            Tempo = Pick(draws, Enum.GetValues<Tempo>()),
            Passing = Pick(draws, Enum.GetValues<PassingStyle>()),
            Width = Pick(draws, Enum.GetValues<Width>()),
            Pressing = Pick(draws, Enum.GetValues<Pressing>()),
            DefensiveLine = Pick(draws, Enum.GetValues<DefensiveLine>()),
            Tackling = Pick(draws, Enum.GetValues<TacklingStyle>()),
            TimeWasting = Pick(draws, Enum.GetValues<TimeWasting>()),
        };

        var layout = FormationLayouts.DefaultSlots(formation);
        var lineup = ChooseLineup(layout, squad);

        return new AiClubPlan(
            formation,
            instructions,
            [.. layout.Select(slot => new AiSlotAssignment(
                slot.SlotNumber,
                slot.PositionFamily,
                slot.Role,
                slot.NormalizedX,
                slot.NormalizedY,
                lineup.TryGetValue(slot.SlotNumber, out var playerId) ? playerId : null))],
            DefaultTrainingIntensity);
    }

    /// <summary>
    /// Picks a legal eleven: exactly one goalkeeper in the goalkeeping slot and the best available
    /// outfield player for each other slot (`DIS-6`'s ordering).
    /// </summary>
    /// <remarks>
    /// Returns an empty mapping when the squad cannot field a legal eleven — no available goalkeeper, or
    /// too few available outfielders. An empty lineup is a valid plan (`TAC-10`): the shape and the
    /// instructions are still the AI's, and the snapshot builder decides the eleven at the lock, which is
    /// the same repair a half-empty sheet gets.
    /// </remarks>
    private static Dictionary<int, Guid> ChooseLineup(
        IReadOnlyList<FormationSlot> layout,
        IReadOnlyList<AiSquadPlayer> squad)
    {
        var available = squad.Where(player => player.IsAvailable).ToList();
        var chosen = new Dictionary<int, Guid>();
        var used = new HashSet<Guid>();

        foreach (var slot in layout.OrderBy(slot => slot.SlotNumber))
        {
            var wantsGoalkeeper = slot.PositionFamily == PositionFamily.Goalkeeper;

            var pick = available
                .Where(player => !used.Contains(player.PlayerId))
                .Where(player => wantsGoalkeeper
                    ? player.PrimaryPosition == PlayerPosition.Goalkeeper
                    : player.PrimaryPosition != PlayerPosition.Goalkeeper)
                .OrderByDescending(player => Suitability(player, slot.PositionFamily))
                .ThenByDescending(player => player.ConditionBp)
                .ThenByDescending(Ability)
                .ThenBy(player => player.PlayerId)
                .FirstOrDefault();

            if (pick is null)
            {
                return [];
            }

            chosen[slot.SlotNumber] = pick.PlayerId;
            used.Add(pick.PlayerId);
        }

        return chosen;
    }

    /// <summary>
    /// How at home a player is in a slot's family: 2 for the position they play, 1 for one they cover,
    /// 0 for neither (`INS-10`'s familiarity, used here only to choose between candidates).
    /// </summary>
    private static int Suitability(AiSquadPlayer player, PositionFamily family)
    {
        if (PlayerPositions.FamilyOf(player.PrimaryPosition) == family)
        {
            return 2;
        }

        return player.SecondaryPositions.Any(position => PlayerPositions.FamilyOf(position) == family) ? 1 : 0;
    }

    /// <summary>
    /// A player's standing among their team-mates, as the mean of their twenty-eight attributes.
    /// </summary>
    /// <remarks>
    /// Deliberately not an "overall": no single number is authoritative, and this one only separates two
    /// candidates that suitability and condition have already failed to separate.
    /// </remarks>
    private static int Ability(AiSquadPlayer player)
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

    /// <summary>Draws one element from a fixed list, consuming exactly one draw.</summary>
    private static T Pick<T>(Pcg32 draws, IReadOnlyList<T> candidates)
    {
        // Enum.GetValues returns in value order, so the draw is a pure function of the seed rather than of
        // a reflection order that could change between runtimes.
        return candidates[draws.NextInt(candidates.Count)];
    }
}
