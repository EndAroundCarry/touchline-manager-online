namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The request to set or replace a club's training plan (`TRN-1`, master plan §10.4).
/// </summary>
/// <remarks>
/// The plan is one current row per club, so create and revise carry the same body and the use case decides
/// which it is. A revise must carry the version the client last read in <c>If-Match</c>, exactly as a
/// tactical plan does, so two devices editing training cannot silently overwrite each other (`CONC-1`).
/// </remarks>
public sealed record SaveTrainingRequest
{
    /// <summary>Gets the club-wide focus code (`TRN-1`), e.g. <c>balanced</c>.</summary>
    public required string TeamFocus { get; init; }

    /// <summary>Gets the intensity code, e.g. <c>normal</c>.</summary>
    public required string Intensity { get; init; }
}

/// <summary>
/// The request to set or clear one player's individual training focus (`TRN-2`).
/// </summary>
/// <remarks>
/// A null or empty <see cref="FocusFamily"/> clears the focus, returning the player to the club's team
/// plan alone. Setting it selects exactly one attribute family, which is what `TRN-2` permits.
/// </remarks>
public sealed record SetPlayerTrainingFocusRequest
{
    /// <summary>Gets the attribute family code, e.g. <c>technical</c>, or null to clear the focus.</summary>
    public string? FocusFamily { get; init; }
}
