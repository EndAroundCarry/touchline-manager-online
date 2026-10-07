using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Application.World;

/// <summary>What happened when a manager chose their club's colours.</summary>
public enum ChangeClubColoursOutcome
{
    /// <summary>The colours were saved.</summary>
    Changed = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 2,

    /// <summary>The manager holds no club, so there are no colours to choose.</summary>
    NoClub = 3,

    /// <summary>The club no longer exists.</summary>
    ClubNotFound = 4,
}

/// <summary>The result of choosing the club's colours.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Colours">The colours the club now plays in, when the change succeeded.</param>
public sealed record ChangeClubColoursResult(ChangeClubColoursOutcome Outcome, ClubColoursResponse? Colours);

/// <summary>
/// Lets the manager choose the two colours their club plays in.
/// </summary>
/// <remarks>
/// <para>
/// Only the club the authenticated manager holds can be changed, and the club is never named in the request:
/// it is read from the manager's open tenure (master plan §10.9). A club's colours are the manager's to
/// choose for as long as they hold it, and the next manager inherits whatever was left.
/// </para>
/// <para>
/// The change does not reach back into matches already locked: a snapshot freezes the colours it was built
/// with, so a replay is always drawn in the kit the match was played in.
/// </para>
/// </remarks>
public sealed class ChangeClubColours
{
    private readonly ResolveOwnedClub _access;
    private readonly IClubRepository _clubs;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly ISecureTokenService _secureTokens;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initializes the use case.</summary>
    public ChangeClubColours(
        ResolveOwnedClub access,
        IClubRepository clubs,
        IClock clock,
        IAuditWriter audit,
        ISecureTokenService secureTokens,
        IRequestContext requestContext,
        IUnitOfWork unitOfWork)
    {
        _access = access;
        _clubs = clubs;
        _clock = clock;
        _audit = audit;
        _secureTokens = secureTokens;
        _requestContext = requestContext;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Saves the colours.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="request">The two colours.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChangeClubColoursResult> ExecuteAsync(
        Guid userId,
        ChangeClubColoursRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Refused(access.Outcome switch
            {
                ClubAccessOutcome.WorldNotSeeded => ChangeClubColoursOutcome.WorldNotSeeded,
                ClubAccessOutcome.NoManagerProfile => ChangeClubColoursOutcome.NoManagerProfile,
                ClubAccessOutcome.NoClub => ChangeClubColoursOutcome.NoClub,
                _ => ChangeClubColoursOutcome.ClubNotFound,
            });
        }

        var club = await _clubs.FindAsync(access.ClubId, cancellationToken);

        if (club is null)
        {
            return Refused(ChangeClubColoursOutcome.ClubNotFound);
        }

        club.ChangeColours(request.PrimaryColour, request.SecondaryColour, _clock.UtcNow);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ClubColoursChanged,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.Club,
            club.Id,
            _requestContext.CorrelationId,
            _secureTokens.HashClientValue(_requestContext.IpAddress),
            Reason: $"{club.PrimaryColour} {club.SecondaryColour}"));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ChangeClubColoursResult(
            ChangeClubColoursOutcome.Changed,
            new ClubColoursResponse(club.Id, club.PrimaryColour!, club.SecondaryColour!));
    }

    private static ChangeClubColoursResult Refused(ChangeClubColoursOutcome outcome) => new(outcome, null);
}
