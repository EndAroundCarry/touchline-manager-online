namespace TouchlineManager.Contracts.Squad;

/// <summary>A request for a renewal quote (`CON-3`, master plan §10.3).</summary>
/// <param name="Seasons">The proposed contract length, 1–3 game seasons (`CON-1`).</param>
public sealed record RenewalQuoteRequest(int Seasons);

/// <summary>A request to accept a renewal for a term (`CON-4`, master plan §10.3).</summary>
/// <param name="Seasons">The contract length to sign, 1–3 game seasons (`CON-1`).</param>
public sealed record RenewContractRequest(int Seasons);
