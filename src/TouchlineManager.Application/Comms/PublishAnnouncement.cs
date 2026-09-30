using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Comms;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Ops;
using TouchlineManager.Domain.Comms;

namespace TouchlineManager.Application.Comms;

/// <summary>What happened when an operator published an announcement.</summary>
public enum PublishAnnouncementOutcome
{
    /// <summary>The announcement was published.</summary>
    Published = 0,

    /// <summary>The title or body is missing or too long, or it would expire before it is published.</summary>
    InvalidAnnouncement = 1,

    /// <summary>The country or division the announcement is scoped to does not exist.</summary>
    ScopeNotFound = 2,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 3,
}

/// <summary>The result of publishing an announcement.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="NewsItemId">The news item that was published, when one was.</param>
/// <param name="PublishedAt">When it became public, when one was published.</param>
/// <param name="ExpiresAt">When it stops being shown, when one was published.</param>
public sealed record PublishAnnouncementResult(
    PublishAnnouncementOutcome Outcome,
    Guid? NewsItemId = null,
    DateTimeOffset? PublishedAt = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// Publishes an operator's announcement as a scoped news item (master plan §10.8, §13, `F-46`, ADR-0045).
/// </summary>
/// <remarks>
/// <para>
/// An announcement is a broadcast the feed already knows how to carry: it is a <see cref="NewsItem"/> scoped
/// to the whole world, one country, or one division, read by whoever can see that scope (`COM-6`). Reusing the
/// feed means the read, its paging, and its caching boundaries are unchanged.
/// </para>
/// <para>
/// The row still stores a stable template key and a parameter document, so the envelope is re-renderable; the
/// operator-authored title and body are the content those parameters carry. That is a deliberate exception to
/// `COM-2`'s "no stored prose" rule, which exists so engine-written messages stay localizable: a notice an
/// operator writes is not a message the game generates and translates.
/// </para>
/// </remarks>
public sealed class PublishAnnouncement
{
    /// <summary>The longest headline an announcement may carry.</summary>
    public const int TitleMaxLength = 120;

    /// <summary>The longest body an announcement may carry.</summary>
    public const int BodyMaxLength = 600;

    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly INewsRepository _news;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public PublishAnnouncement(
        IClock clock,
        IWorldRepository world,
        INewsRepository news,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _clock = clock;
        _world = world;
        _news = news;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Publishes the announcement.</summary>
    /// <param name="title">The headline.</param>
    /// <param name="body">The notice.</param>
    /// <param name="countryId">The country to scope it to, or null for the whole world.</param>
    /// <param name="divisionId">The division to scope it to, or null for a broader scope.</param>
    /// <param name="expiresAt">When it stops being shown, or null when it does not expire.</param>
    /// <param name="reason">Why the operator is publishing it. Required, and stored in the audit trail.</param>
    /// <param name="idempotencyKey">The operator's idempotency key, recorded on the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PublishAnnouncementResult> ExecuteAsync(
        string title,
        string body,
        Guid? countryId,
        Guid? divisionId,
        DateTimeOffset? expiresAt,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        AccountAdministration.ValidateReason(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var now = _clock.UtcNow;

        if (string.IsNullOrWhiteSpace(title)
            || title.Length > TitleMaxLength
            || string.IsNullOrWhiteSpace(body)
            || body.Length > BodyMaxLength
            || expiresAt <= now)
        {
            return new PublishAnnouncementResult(PublishAnnouncementOutcome.InvalidAnnouncement);
        }

        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return new PublishAnnouncementResult(PublishAnnouncementOutcome.WorldNotSeeded);
        }

        if (countryId is { } country && await _world.FindCountryAsync(country, cancellationToken) is null)
        {
            return new PublishAnnouncementResult(PublishAnnouncementOutcome.ScopeNotFound);
        }

        if (divisionId is { } division)
        {
            var found = await _world.FindDivisionAsync(division, cancellationToken);

            if (found is null || (countryId is { } scopedCountry && found.CountryId != scopedCountry))
            {
                return new PublishAnnouncementResult(PublishAnnouncementOutcome.ScopeNotFound);
            }
        }

        var draft = NewsTemplates.Announcement(title, body, countryId, divisionId, expiresAt);

        var item = NewsItem.Publish(
            Guid.CreateVersion7(),
            world.Id,
            draft.CountryId,
            draft.DivisionId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            publishedAt: now,
            expiresAt: draft.ExpiresAt,
            now);

        _news.Add(item);

        _audit.Record(new AuditEntry(
            AdminAuditActions.AnnouncementPublished,
            AuditActorTypes.User,
            _requestContext.ActorUserId,
            AuditTargetTypes.NewsItem,
            item.Id,
            idempotencyKey,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            reason));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PublishAnnouncementResult(
            PublishAnnouncementOutcome.Published,
            item.Id,
            item.PublishedAt,
            item.ExpiresAt);
    }
}
