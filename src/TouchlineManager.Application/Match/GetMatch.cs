using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Contracts.Match;

namespace TouchlineManager.Application.Match;

/// <summary>The result of reading a match's summary.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Match">The summary, when the read succeeded.</param>
public sealed record GetMatchResult(MatchReadOutcome Outcome, MatchResponse? Match);

/// <summary>
/// Reads a played match's summary (master plan §9.5, §10.5).
/// </summary>
/// <remarks>
/// Public game data, like the fixture calendar and the table, so nothing gates it on the caller holding a
/// club: a result is the thing a manager looks at after the whistle and one they can share. Only a
/// published match is readable, which the query enforces rather than this use case, so a staged score
/// cannot leak through a read (`MAT-7`).
/// </remarks>
public sealed class GetMatch
{
    private readonly IMatchQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the query.</summary>
    public GetMatch(IMatchQueries queries, IClock clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads a match's summary.</summary>
    /// <param name="matchId">The match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetMatchResult> ExecuteAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetMatchAsync(matchId, cancellationToken);

        return snapshot is null
            ? new GetMatchResult(MatchReadOutcome.MatchNotFound, null)
            : new GetMatchResult(MatchReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow));
    }
}
