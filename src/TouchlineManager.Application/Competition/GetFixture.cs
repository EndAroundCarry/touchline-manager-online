using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Squad;
using TouchlineManager.Contracts.Competition;

namespace TouchlineManager.Application.Competition;

/// <summary>The result of reading one fixture in full.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Fixture">The fixture, when the read succeeded.</param>
public sealed record GetFixtureResult(
    CompetitionReadOutcome Outcome,
    FixtureDetailResponse? Fixture);

/// <summary>
/// Reads one fixture in full, as the prepare-match screen needs it (master plan §11.1).
/// </summary>
/// <remarks>
/// <para>
/// The fixture itself is public game data, so the read succeeds for any authenticated account. What the
/// caller's own tenure adds is <c>Manageable</c>: the response names the club they hold among the two, and
/// whether it is the host or the visitor, so the screen offers the prepare control rather than the screen
/// guessing and being refused.
/// </para>
/// <para>
/// An account without a club is not a refusal here — it simply has no side — which is why the access
/// outcome is consulted rather than enforced. Every other outcome of the resolver is equally harmless for
/// a public read, so only a granted one changes the answer.
/// </para>
/// </remarks>
public sealed class GetFixture
{
    private readonly ResolveOwnedClub _access;
    private readonly ICompetitionQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetFixture(ResolveOwnedClub access, ICompetitionQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a fixture, resolving the caller's side when they hold one of the clubs.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="fixtureId">The fixture to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetFixtureResult> ExecuteAsync(
        Guid userId,
        Guid fixtureId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetFixtureAsync(fixtureId, cancellationToken);

        if (snapshot is null)
        {
            return new GetFixtureResult(CompetitionReadOutcome.FixtureNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        var managedClubId = access.Outcome == ClubAccessOutcome.Granted
            && access.ClubId != Guid.Empty
            && (snapshot.Home.ClubId == access.ClubId || snapshot.Away.ClubId == access.ClubId)
                ? access.ClubId
                : (Guid?)null;

        return new GetFixtureResult(
            CompetitionReadOutcome.Found,
            snapshot.ToResponse(managedClubId, _clock.UtcNow));
    }
}
