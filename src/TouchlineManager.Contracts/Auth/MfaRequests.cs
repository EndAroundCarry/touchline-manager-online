namespace TouchlineManager.Contracts.Auth;

/// <summary>Request to confirm a pending multi-factor enrolment with the first valid code.</summary>
public sealed record MfaConfirmRequest
{
    /// <summary>Gets the code derived from the pending secret.</summary>
    public required string Code { get; init; }
}

/// <summary>Request to complete a two-step login.</summary>
public sealed record MfaLoginRequest
{
    /// <summary>Gets the challenge token the password step returned.</summary>
    public required string ChallengeToken { get; init; }

    /// <summary>Gets a current code, or one unused recovery code.</summary>
    public required string Code { get; init; }
}

/// <summary>Request to turn off multi-factor authentication.</summary>
public sealed record MfaDisableRequest
{
    /// <summary>Gets a current code, or one unused recovery code, proving possession.</summary>
    public required string Code { get; init; }
}

/// <summary>Request to issue a fresh set of recovery codes.</summary>
public sealed record RecoveryCodesRequest
{
    /// <summary>Gets a current code, proving possession.</summary>
    public required string Code { get; init; }
}
