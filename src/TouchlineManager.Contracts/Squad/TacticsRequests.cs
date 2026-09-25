namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The request to create or replace a tactical plan (master plan §10.4, §11.1).
/// </summary>
/// <remarks>
/// <para>
/// One shape for both commands: <c>POST /tactics</c> creates a plan and <c>PUT /tactics/{planId}</c>
/// replaces one, and the body they carry is the same plan. Splitting them into two types would invite
/// the two to drift.
/// </para>
/// <para>
/// The layout and the lineup are separate on purpose. <see cref="Slots"/> is what the manager dragged —
/// a slot's family, role, and coordinates — and is omitted when the plan should simply be laid out from
/// its <see cref="FormationPreset"/>. <see cref="Lineup"/> is who occupies which slot, so picking
/// players without moving anything stays one small body. The server owns the preset's default
/// arrangement (`TAC-1`…`TAC-6`), so a client never has to reproduce it.
/// </para>
/// </remarks>
public sealed record SaveTacticalPlanRequest
{
    /// <summary>Gets the manager-facing plan name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the formation preset code, e.g. <c>4-4-2</c> (`TAC-1`…`TAC-6`).</summary>
    public required string FormationPreset { get; init; }

    /// <summary>Gets the mentality code (`INS-1`).</summary>
    public required string Mentality { get; init; }

    /// <summary>Gets the tempo code (`INS-2`).</summary>
    public required string Tempo { get; init; }

    /// <summary>Gets the passing code (`INS-3`).</summary>
    public required string Passing { get; init; }

    /// <summary>Gets the width code (`INS-4`).</summary>
    public required string Width { get; init; }

    /// <summary>Gets the pressing code (`INS-5`).</summary>
    public required string Pressing { get; init; }

    /// <summary>Gets the defensive-line code (`INS-6`).</summary>
    public required string DefensiveLine { get; init; }

    /// <summary>Gets the tackling code (`INS-7`).</summary>
    public required string Tackling { get; init; }

    /// <summary>Gets the time-wasting code (`INS-8`).</summary>
    public required string TimeWasting { get; init; }

    /// <summary>
    /// Gets the slot layout, or null to lay the plan out from its formation preset.
    /// </summary>
    /// <remarks>
    /// When present it must name all eleven slots. When absent the server uses the preset's default
    /// arrangement, which is what makes creating a plan one small request.
    /// </remarks>
    public IReadOnlyList<TacticalSlotRequest>? Slots { get; init; }

    /// <summary>
    /// Gets the player assigned to each slot, or null to leave the plan unassigned.
    /// </summary>
    /// <remarks>
    /// Omitted entirely, the plan is a legal template with nobody picked. When any slot is named, all
    /// eleven must be (`SQ-4`) — the validator refuses a half-filled side rather than fielding one short.
    /// </remarks>
    public IReadOnlyList<TacticalLineupEntryRequest>? Lineup { get; init; }
}

/// <summary>One slot's layout in a submitted plan (`TAC-7`…`TAC-9`).</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The position family code, e.g. <c>defence</c>.</param>
/// <param name="Role">The role code, e.g. <c>full_back</c> (`TAC-8`).</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000 (`TAC-9`).</param>
/// <param name="NormalizedY">The normalized width, 0–10,000 (`TAC-9`).</param>
public sealed record TacticalSlotRequest
{
    /// <summary>Gets the slot number.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>Gets the position family code.</summary>
    public required string PositionFamily { get; init; }

    /// <summary>Gets the role code.</summary>
    public required string Role { get; init; }

    /// <summary>Gets the normalized x coordinate.</summary>
    public required int NormalizedX { get; init; }

    /// <summary>Gets the normalized y coordinate.</summary>
    public required int NormalizedY { get; init; }
}

/// <summary>One player's place in the default lineup (`SQ-4`).</summary>
/// <param name="SlotNumber">The slot the player occupies, 1–11.</param>
/// <param name="PlayerId">The selected player.</param>
public sealed record TacticalLineupEntryRequest
{
    /// <summary>Gets the slot number.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>Gets the selected player.</summary>
    public required Guid PlayerId { get; init; }
}
