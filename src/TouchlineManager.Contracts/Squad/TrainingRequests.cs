namespace TouchlineManager.Contracts.Squad;

/// <summary>
/// The request to set or replace a club's training plan (`TRN-1`, master plan §10.4).
/// </summary>
/// <remarks>
/// The plan is one current row per club holding the club-wide intensity, so create and revise carry the same
/// body and the use case decides which it is. What each player trains is a per-player programme
/// (<see cref="SetPlayerTrainingProgrammeRequest"/>). A revise must carry the version the client last read
/// in <c>If-Match</c>, exactly as a tactical plan does, so two devices editing training cannot silently
/// overwrite each other (`CONC-1`).
/// </remarks>
public sealed record SaveTrainingRequest
{
    /// <summary>Gets the intensity code, e.g. <c>normal</c>.</summary>
    public required string Intensity { get; init; }
}

/// <summary>
/// The request to set or clear one player's training programme (`TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// A null or empty <see cref="Programme"/> clears the override, returning the player to the programme that
/// matches their position. Setting it selects exactly one programme from the catalogue the training read
/// serves.
/// </remarks>
public sealed record SetPlayerTrainingProgrammeRequest
{
    /// <summary>Gets the programme code, e.g. <c>forward</c>, or null to train the position default.</summary>
    public string? Programme { get; init; }
}
