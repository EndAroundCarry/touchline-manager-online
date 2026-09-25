namespace TouchlineManager.Contracts.Competition;

/// <summary>One player's place in a submitted fixture team sheet (`SQ-4`).</summary>
/// <param name="SlotNumber">
/// The slot the player occupies. Starters take 1–11 and substitutes 12–18, which is what makes a slot
/// number mean one thing across the whole sheet (`data-model.md` §3.2).
/// </param>
/// <param name="PlayerId">The selected player.</param>
public sealed record TeamSheetSelectionRequest
{
    /// <summary>Gets the slot number, 1–18.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>Gets the selected player.</summary>
    public required Guid PlayerId { get; init; }
}

/// <summary>
/// The request to save a club's selection for one fixture (master plan §10.4).
/// </summary>
/// <remarks>
/// <para>
/// The whole selection travels on every save, so what the manager submitted is exactly what is stored —
/// there is no per-slot patch to fall out of step with the pitch. The server owns the club, the fixture,
/// the plan the sheet references, and the deadline: none of them is taken from the request.
/// </para>
/// <para>
/// A substitution's role override is deliberately absent. It is a post-MVP nuance that the slot's own
/// role already covers, and exposing it before the snapshot builder reads it would be surface without a
/// consumer.
/// </para>
/// </remarks>
public sealed record SaveFixtureTeamSheetRequest
{
    /// <summary>Gets the selection, which must name all eleven starters and up to seven substitutes.</summary>
    public required IReadOnlyList<TeamSheetSelectionRequest> Selection { get; init; }
}
