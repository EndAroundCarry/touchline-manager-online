using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Contracts.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Commentary;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Highlights;

namespace TouchlineManager.Application.Match;

/// <summary>The result of reading a match's replay.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Presentation">The replay, when the read succeeded.</param>
/// <param name="EntityTag">The strong entity tag the replay is cached under, when the read succeeded.</param>
public sealed record GetMatchPresentationResult(
    MatchReadOutcome Outcome,
    MatchPresentationResponse? Presentation,
    string? EntityTag);

/// <summary>
/// Reads a played match's commentary and highlights (master plan §9.5, §10.5).
/// </summary>
/// <remarks>
/// <para>
/// The replay is re-derived rather than stored: the engine is pure and deterministic, the snapshot is
/// frozen and verified, and re-simulating from it reproduces the published result byte for byte. That is
/// what makes the commentary and the highlights impossible to drift from the result they describe — a
/// stored presentation would be a second copy that a later change could leave behind (`MAT-8`, `MAT-9`).
/// </para>
/// <para>
/// The re-derived output hash is compared with the stored one before anything is returned. A mismatch means
/// the engine changed without a version bump, which is a deployment defect rather than a manager's problem,
/// so it fails loudly instead of serving a replay of a different match.
/// </para>
/// </remarks>
public sealed class GetMatchPresentation
{
    private readonly IMatchQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetMatchPresentation(IMatchQueries queries) => _queries = queries;

    /// <summary>Reads a match's replay.</summary>
    /// <param name="matchId">The match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetMatchPresentationResult> ExecuteAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetMatchAsync(matchId, cancellationToken);

        if (snapshot is null)
        {
            return new GetMatchPresentationResult(MatchReadOutcome.MatchNotFound, null, null);
        }

        var input = MatchSnapshotFactory.ReadVerified(snapshot.Snapshot);
        var result = MatchSimulator.Simulate(input, EngineRulesV1.Default);

        if (!string.Equals(result.OutputHash, snapshot.OutputHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Re-simulating match {snapshot.MatchId:D} did not reproduce its stored output hash. "
                + "The engine or its rules changed without a version bump (MAT-9).");
        }

        var commentary = CommentaryTokenBuilder.Build(input, result);
        var highlights = HighlightDirector.Build(input, result);

        return new GetMatchPresentationResult(
            MatchReadOutcome.Found,
            snapshot.ToResponse(highlights, commentary),
            snapshot.OutputHash);
    }
}
