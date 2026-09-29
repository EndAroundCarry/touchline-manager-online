namespace TouchlineManager.Contracts.Auth;

/// <summary>
/// Everything the game holds about one account, in one machine-readable document (master plan §12.4,
/// F-07).
/// </summary>
/// <remarks>
/// <para>
/// A data-subject access request: the account's own record plus the history that identifies it. It is
/// scoped from the authenticated account and never takes a target, so an export can only ever be of the
/// caller's own data (§10.9).
/// </para>
/// <para>
/// The document carries no secret or hidden value: no password hash, no token or token hash, no client
/// fingerprint hash, and no server-only game value (`VOI-4`, §12.4, data classification §4). The ledger
/// section is the caller's current club's ledger, which they already read through the finances screen.
/// </para>
/// </remarks>
/// <param name="GeneratedAt">When the export was produced.</param>
/// <param name="Account">The account's identity and lifecycle record.</param>
/// <param name="Consents">The legal documents the account has accepted, oldest first.</param>
/// <param name="Manager">The manager profile, or null when none has been created.</param>
/// <param name="Tenures">Every club the account has managed, oldest first.</param>
/// <param name="Sessions">The account's active sessions.</param>
/// <param name="Finance">The current club's ledger, or null when the account manages no club.</param>
public sealed record AccountExportResponse(
    DateTimeOffset GeneratedAt,
    ExportedAccountResponse Account,
    IReadOnlyList<ExportedConsentResponse> Consents,
    ExportedManagerResponse? Manager,
    IReadOnlyList<ExportedTenureResponse> Tenures,
    IReadOnlyList<ExportedSessionResponse> Sessions,
    ExportedFinanceResponse? Finance);

/// <summary>The account's own identity and lifecycle record.</summary>
/// <param name="Id">The account identity.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The public display name.</param>
/// <param name="EmailVerified">Whether the address has been verified.</param>
/// <param name="Status">The lifecycle state, as a stable lowercase code.</param>
/// <param name="Roles">The roles the account holds.</param>
/// <param name="LastLoginAt">When the account last authenticated, if ever.</param>
/// <param name="CreatedAt">When the account was created.</param>
public sealed record ExportedAccountResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

/// <summary>One legal document the account accepted, and when.</summary>
/// <param name="DocumentType">The document type, e.g. <c>terms</c> or <c>privacy</c>.</param>
/// <param name="Version">The accepted version.</param>
/// <param name="AcceptedAt">When it was accepted.</param>
public sealed record ExportedConsentResponse(
    string DocumentType,
    string Version,
    DateTimeOffset AcceptedAt);

/// <summary>The account's manager profile.</summary>
/// <param name="Id">The manager identity.</param>
/// <param name="Reputation">The manager's reputation, on the 1–100 scale.</param>
/// <param name="Locale">The preferred locale.</param>
/// <param name="TimeZone">The IANA time zone.</param>
/// <param name="TakeoverCooldownUntil">When the post-resignation cooldown lapses, if one is running.</param>
/// <param name="CreatedAt">When the profile was created.</param>
public sealed record ExportedManagerResponse(
    Guid Id,
    int Reputation,
    string Locale,
    string TimeZone,
    DateTimeOffset? TakeoverCooldownUntil,
    DateTimeOffset CreatedAt);

/// <summary>One club the account has managed.</summary>
/// <param name="Id">The tenure identity.</param>
/// <param name="ClubId">The club's identity.</param>
/// <param name="ClubName">The club's name.</param>
/// <param name="ControlStatus">The control state: <c>active</c>, <c>inactive</c>, or <c>closed</c>.</param>
/// <param name="StartedAt">When control began.</param>
/// <param name="EndedAt">When control ended, or null while the tenure is open.</param>
/// <param name="EndReason">Why control ended, or null while open.</param>
public sealed record ExportedTenureResponse(
    Guid Id,
    Guid ClubId,
    string ClubName,
    string ControlStatus,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndReason);

/// <summary>One active session, without any token or client-fingerprint material.</summary>
/// <remarks>
/// No "current session" marker: the export is read at <c>/me</c>, which the refresh cookie is not scoped to,
/// so the server cannot tell which session asked. The sessions screen, read under <c>/auth</c>, is where the
/// current session is identified and where a manager revokes one.
/// </remarks>
/// <param name="Id">The session identity.</param>
/// <param name="IssuedAt">When the session was started.</param>
/// <param name="ExpiresAt">When it would expire if never used again.</param>
/// <param name="LastUsedAt">When it was last used to refresh, or null.</param>
public sealed record ExportedSessionResponse(
    Guid Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt);

/// <summary>The caller's current club's ledger, as the "own transactional history" of the export.</summary>
/// <param name="ClubId">The club whose ledger this is.</param>
/// <param name="Entries">The ledger entries, newest first.</param>
/// <param name="Truncated">Whether the club's ledger continues past the entries included.</param>
public sealed record ExportedFinanceResponse(
    Guid ClubId,
    IReadOnlyList<ExportedLedgerEntryResponse> Entries,
    bool Truncated);

/// <summary>One ledger line, projected for the export without its internal correlation key.</summary>
/// <param name="Sequence">The entry's position in the club's ledger, starting at one.</param>
/// <param name="Category">What the entry is, as a stable lowercase code.</param>
/// <param name="CashDeltaMinor">The signed change to cash, in minor units.</param>
/// <param name="ReservedDeltaMinor">The signed change to reserved funds, in minor units.</param>
/// <param name="ResultingCashMinor">The cash the club held after the move.</param>
/// <param name="ResultingReservedMinor">The reserved funds the club held after the move.</param>
/// <param name="DescriptionTemplate">The stable template key that describes the entry.</param>
/// <param name="CreatedAt">When the entry was written.</param>
public sealed record ExportedLedgerEntryResponse(
    long Sequence,
    string Category,
    long CashDeltaMinor,
    long ReservedDeltaMinor,
    long ResultingCashMinor,
    long ResultingReservedMinor,
    string DescriptionTemplate,
    DateTimeOffset CreatedAt);
