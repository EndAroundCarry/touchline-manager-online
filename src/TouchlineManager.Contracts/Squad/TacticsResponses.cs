namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The club's tactical plans and the squad they are picked from (master plan §10.4, §11.1).
/// </summary>
/// <remarks>
/// One response for the whole tactics screen: the saved plans, the players a manager may assign, and the
/// formation presets' own arrangements. The screen needs all three to render a first, empty plan — the
/// presets are reference data the server owns, so the client never reproduces a formation's coordinates.
/// </remarks>
/// <param name="ClubId">The club the plans belong to.</param>
/// <param name="ClubName">The generated club name.</param>
/// <param name="ClubShortName">The club abbreviation.</param>
/// <param name="CountryCode">The club's country code.</param>
/// <param name="SeasonNumber">The season the plans are read against.</param>
/// <param name="Plans">The saved plans, the default first.</param>
/// <param name="SelectablePlayers">The players who may be assigned to a slot.</param>
/// <param name="Formations">Every formation preset and its default arrangement (`TAC-1`…`TAC-6`).</param>
/// <param name="ServerTime">The instant the response was produced.</param>
public sealed record TacticsResponse(
    Guid ClubId,
    string ClubName,
    string ClubShortName,
    string CountryCode,
    int SeasonNumber,
    IReadOnlyList<TacticalPlanResponse> Plans,
    IReadOnlyList<SelectablePlayerResponse> SelectablePlayers,
    IReadOnlyList<FormationPresetResponse> Formations,
    DateTimeOffset ServerTime);

/// <summary>One saved tactical plan (`INS-11`).</summary>
/// <remarks>
/// <see cref="Version"/> is the plan's optimistic-concurrency token. The client sends it back in
/// <c>If-Match</c> when it saves, and a stale value is answered with <c>412</c> rather than silently
/// overwriting a change made on another device (`CONC-1`, ADR-0009).
/// </remarks>
/// <param name="Id">The plan identity.</param>
/// <param name="Name">The manager-facing name.</param>
/// <param name="FormationPreset">The formation preset code.</param>
/// <param name="IsDefault">Whether this is the club's default plan (`INS-11`).</param>
/// <param name="Instructions">The eight team instructions (`INS-1`…`INS-8`).</param>
/// <param name="Version">The plan version, returned as the strong entity tag.</param>
/// <param name="AssignedCount">How many of the eleven slots name a player.</param>
/// <param name="IsComplete">Whether all eleven slots name a player.</param>
/// <param name="Slots">The eleven slots, in slot order.</param>
public sealed record TacticalPlanResponse(
    Guid Id,
    string Name,
    string FormationPreset,
    bool IsDefault,
    TeamInstructionsResponse Instructions,
    long Version,
    int AssignedCount,
    bool IsComplete,
    IReadOnlyList<TacticalSlotResponse> Slots);

/// <summary>The eight team-level settings, as stable codes (`INS-1`…`INS-8`).</summary>
/// <param name="Mentality">The mentality code.</param>
/// <param name="Tempo">The tempo code.</param>
/// <param name="Passing">The passing code.</param>
/// <param name="Width">The width code.</param>
/// <param name="Pressing">The pressing code.</param>
/// <param name="DefensiveLine">The defensive-line code.</param>
/// <param name="Tackling">The tackling code.</param>
/// <param name="TimeWasting">The time-wasting code.</param>
public sealed record TeamInstructionsResponse(
    string Mentality,
    string Tempo,
    string Passing,
    string Width,
    string Pressing,
    string DefensiveLine,
    string Tackling,
    string TimeWasting);

/// <summary>One slot in a saved plan, with whoever occupies it (`TAC-7`…`TAC-9`).</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The position family code.</param>
/// <param name="Role">The role code.</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000.</param>
/// <param name="NormalizedY">The normalized width, 0–10,000.</param>
/// <param name="AssignedPlayer">The player in the slot, or null when it is empty.</param>
/// <param name="IsOutOfPosition">
/// Whether the assigned player is not at home in the slot's family (`INS-10`). A penalty the engine
/// applies and the screen warns about, never a refusal.
/// </param>
public sealed record TacticalSlotResponse(
    int SlotNumber,
    string PositionFamily,
    string Role,
    int NormalizedX,
    int NormalizedY,
    AssignedPlayerResponse? AssignedPlayer,
    bool IsOutOfPosition);

/// <summary>The player occupying a slot.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown on the pitch.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="IsUnavailable">Whether the player has an open injury or suspension.</param>
public sealed record AssignedPlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string PrimaryPosition,
    bool IsUnavailable);

/// <summary>A player who may be assigned to a slot.</summary>
/// <param name="Id">The player identity.</param>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviation shown on the pitch.</param>
/// <param name="PrimaryPosition">The stable code of the player's position.</param>
/// <param name="PositionFamily">The stable code of the family that position belongs to.</param>
/// <param name="IsUnavailable">Whether the player has an open injury or suspension (`TRN-12`).</param>
public sealed record SelectablePlayerResponse(
    Guid Id,
    string FullName,
    string ShortName,
    string PrimaryPosition,
    string PositionFamily,
    bool IsUnavailable);

/// <summary>A formation preset and its default arrangement (`TAC-1`…`TAC-6`).</summary>
/// <param name="Code">The preset's stable code, e.g. <c>4-4-2</c>.</param>
/// <param name="Slots">The eleven default slots.</param>
public sealed record FormationPresetResponse(
    string Code,
    IReadOnlyList<FormationSlotResponse> Slots);

/// <summary>One slot of a formation preset's default arrangement.</summary>
/// <param name="SlotNumber">The slot number, 1–11.</param>
/// <param name="PositionFamily">The position family code.</param>
/// <param name="Role">The role code.</param>
/// <param name="NormalizedX">The normalized depth, 0–10,000.</param>
/// <param name="NormalizedY">The normalized width, 0–10,000.</param>
public sealed record FormationSlotResponse(
    int SlotNumber,
    string PositionFamily,
    string Role,
    int NormalizedX,
    int NormalizedY);

/// <summary>
/// Why a plan was refused, as a validation preview (master plan §10.4).
/// </summary>
/// <remarks>
/// A save that fails the tactics rules answers with this rather than a bare field-error map, because the
/// problems are about the shape of a pitch rather than about a form control: a slot number, a player, or
/// the whole lineup. The client draws each issue on the board it belongs to.
/// </remarks>
/// <param name="IsValid">Whether the plan is valid.</param>
/// <param name="AssignedCount">How many of the eleven slots name a player.</param>
/// <param name="IsComplete">Whether all eleven slots name a player.</param>
/// <param name="Issues">Every reason the plan is not valid, in slot order.</param>
public sealed record TacticalPlanValidationResponse(
    bool IsValid,
    int AssignedCount,
    bool IsComplete,
    IReadOnlyList<TacticalPlanIssueResponse> Issues);

/// <summary>One reason a plan is not valid, in the stable-code vocabulary of the validator.</summary>
/// <param name="Code">The stable issue code, e.g. <c>DUPLICATE_PLAYER</c>.</param>
/// <param name="SlotNumber">The slot the issue concerns, when it concerns one.</param>
/// <param name="PlayerId">The player the issue concerns, when it concerns one.</param>
public sealed record TacticalPlanIssueResponse(
    string Code,
    int? SlotNumber,
    Guid? PlayerId);
