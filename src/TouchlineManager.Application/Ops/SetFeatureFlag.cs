using System.Text.Json;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;

namespace TouchlineManager.Application.Ops;

/// <summary>What happened when an operator set a feature flag.</summary>
public enum SetFeatureFlagOutcome
{
    /// <summary>The flag did not exist and was created.</summary>
    Created = 0,

    /// <summary>The flag existed and its value was replaced.</summary>
    Updated = 1,

    /// <summary>The key or the value is not one this surface accepts.</summary>
    InvalidFlag = 2,
}

/// <summary>The result of setting a feature flag.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Flag">The stored flag, when one was written.</param>
public sealed record SetFeatureFlagResult(SetFeatureFlagOutcome Outcome, FeatureFlagSnapshot? Flag = null);

/// <summary>
/// Sets a feature flag on an operator's authority (master plan §10.8, §13, `F-46`, ADR-0045).
/// </summary>
/// <remarks>
/// The flag's value is an opaque JSON document, so a switch can be a boolean today and a percentage
/// tomorrow without a schema change. This command only writes the store; reading a flag to gate behaviour is
/// the incident-control milestone (F-51), which will read through the same port. The change and its audit row
/// commit together, so a switch cannot be flipped without a record of the operator and the reason.
/// </remarks>
public sealed class SetFeatureFlag
{
    /// <summary>The scope every flag this surface writes belongs to.</summary>
    public const string WorldScope = "world";

    /// <summary>The longest flag key.</summary>
    public const int KeyMaxLength = 100;

    /// <summary>The longest flag value document, in characters.</summary>
    public const int ValueMaxLength = 4096;

    private readonly IClock _clock;
    private readonly IFeatureFlagStore _flags;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public SetFeatureFlag(
        IClock clock,
        IFeatureFlagStore flags,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _flags = flags;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Sets the flag, creating it when it does not exist.</summary>
    /// <param name="key">The flag's key.</param>
    /// <param name="valueJson">The flag's value, as a JSON document.</param>
    /// <param name="rolloutMetadataJson">Optional rollout metadata, as a JSON document, or null.</param>
    /// <param name="reason">Why the operator is setting it. Required, and stored in the audit trail.</param>
    /// <param name="idempotencyKey">The operator's idempotency key, recorded on the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SetFeatureFlagResult> ExecuteAsync(
        string key,
        string? valueJson,
        string? rolloutMetadataJson,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (!IsValidKey(key)
            || valueJson is null
            || valueJson.Length > ValueMaxLength
            || !IsJson(valueJson)
            || (rolloutMetadataJson is not null && !IsJson(rolloutMetadataJson)))
        {
            return new SetFeatureFlagResult(SetFeatureFlagOutcome.InvalidFlag);
        }

        var existing = await _flags.FindAsync(WorldScope, key, cancellationToken);

        var flag = _flags.Upsert(WorldScope, key, valueJson, rolloutMetadataJson, _clock.UtcNow);

        _audit.Record(new AuditEntry(
            AdminAuditActions.FeatureFlagSet,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.FeatureFlag,
            flag.Id,
            idempotencyKey,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetFeatureFlagResult(
            existing is null ? SetFeatureFlagOutcome.Created : SetFeatureFlagOutcome.Updated,
            flag);
    }

    /// <summary>A flag key is lower-case, dotted, and namespaced by convention, e.g. <c>matchday.enabled</c>.</summary>
    private static bool IsValidKey(string key)
    {
        if (string.IsNullOrEmpty(key)
            || key.Length > KeyMaxLength
            || (!char.IsAsciiLetterLower(key[0]) && !char.IsAsciiDigit(key[0])))
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!char.IsAsciiLetterLower(character)
                && !char.IsAsciiDigit(character)
                && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
