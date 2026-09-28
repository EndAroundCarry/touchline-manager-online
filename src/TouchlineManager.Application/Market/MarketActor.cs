using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Application.Market;

/// <summary>
/// Who is acting on the market, and under what correlation (`INT-2`, `INT-3`).
/// </summary>
/// <remarks>
/// The listing and bid writers are shared by a manager's command and the AI's evaluation, so the actor is
/// the one thing that differs between them: a person is audited as a <see cref="AuditActorTypes.User"/> with
/// their account and hashed IP, and the AI's worker as a <see cref="AuditActorTypes.Service"/> with neither.
/// Nothing else about the write changes, which is how `INS-12`'s "the AI receives no bypass" is enforced by
/// the code path rather than by intention.
/// </remarks>
/// <param name="ActorType">One of <see cref="AuditActorTypes"/>.</param>
/// <param name="UserId">The acting account, when a person is acting.</param>
/// <param name="IpHash">The hashed client IP, when a person is acting.</param>
/// <param name="CorrelationId">The correlation ID that ties the audit rows to logs and jobs.</param>
public sealed record MarketActor(string ActorType, Guid? UserId, string? IpHash, string CorrelationId)
{
    /// <summary>Builds the actor for an authenticated manager's command.</summary>
    /// <param name="userId">The acting account.</param>
    /// <param name="request">The ambient request facts.</param>
    /// <param name="secureTokens">The hasher for the client fingerprint (`data-classification` §4).</param>
    public static MarketActor ForUser(
        Guid userId,
        IRequestContext request,
        ISecureTokenService secureTokens)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(secureTokens);

        return new MarketActor(
            AuditActorTypes.User,
            userId,
            secureTokens.HashClientValue(request.IpAddress),
            request.CorrelationId);
    }

    /// <summary>Builds the actor for the AI's worker evaluation.</summary>
    /// <param name="correlationId">The correlation ID of the evaluation pass.</param>
    public static MarketActor Service(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new MarketActor(AuditActorTypes.Service, UserId: null, IpHash: null, correlationId);
    }
}
