using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Contracts.Status;

namespace TouchlineManager.Application.Status;

/// <summary>
/// Reads the public service status and the published document versions (master plan §16 Stage 15, `F-55`,
/// ADR-0050).
/// </summary>
/// <remarks>
/// <para>
/// One anonymous read serves the status page and the versioned legal pages. It composes reads the game
/// already exposes rather than adding a table or a repository: the read-only state comes from the incident
/// flag reader (`F-51`), the running season from the world, and the next round from the same matchday read
/// the stepped clock's status uses (`ADR-0049`).
/// </para>
/// <para>
/// Nothing here is personal data: the response is public game data, the operator's maintenance reason, and
/// the document versions a consent records, so it is safe for a signed-out visitor (`LGL-1`).
/// </para>
/// </remarks>
public sealed class GetPublicStatus
{
    /// <summary>How far ahead the next round is looked for.</summary>
    private static readonly TimeSpan MatchdayHorizon = TimeSpan.FromDays(400);

    private readonly IClock _clock;
    private readonly IReadOnlyMode _readOnlyMode;
    private readonly IWorldRepository _world;
    private readonly IMatchdayRepository _matchdays;
    private readonly AuthOptions _authOptions;

    /// <summary>Initializes the read.</summary>
    public GetPublicStatus(
        IClock clock,
        IReadOnlyMode readOnlyMode,
        IWorldRepository world,
        IMatchdayRepository matchdays,
        IOptions<AuthOptions> authOptions)
    {
        ArgumentNullException.ThrowIfNull(authOptions);

        _clock = clock;
        _readOnlyMode = readOnlyMode;
        _world = world;
        _matchdays = matchdays;
        _authOptions = authOptions.Value;
    }

    /// <summary>Reads the status and the document versions.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<PublicStatusResponse> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var readOnly = await _readOnlyMode.GetStateAsync(cancellationToken);
        var world = await _world.FindWorldAsync(cancellationToken);

        var nextMatchdayAt = await FindNextMatchdayAsync(now, cancellationToken);

        return new PublicStatusResponse(
            now,
            readOnly.Enabled,
            readOnly.Message,
            world?.CurrentSeasonNumber,
            nextMatchdayAt,
            new DocumentVersionsResponse(_authOptions.TermsVersion, _authOptions.PrivacyVersion));
    }

    private async Task<DateTimeOffset?> FindNextMatchdayAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var upcoming = await _matchdays.ListPendingLockingBetweenAsync(
            now,
            now.Add(MatchdayHorizon),
            cancellationToken);

        return upcoming.Count > 0 ? upcoming[0].KickoffAt : null;
    }
}
