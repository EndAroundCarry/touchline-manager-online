using Tactical11Manager.Application.Abstractions.Match;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Match;

namespace Tactical11Manager.Application.Match;

/// <summary>The result of reading the names of a match's instructions.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Names">The caller's own instruction names, when the match was found.</param>
public sealed record GetMatchInstructionNamesResult(MatchReadOutcome Outcome, MatchInstructionNamesResponse? Names);

/// <summary>
/// Reads the names of the match instructions the caller's own club played with in a match.
/// </summary>
/// <remarks>
/// <para>
/// The replay is public game data, so it carries a rule only as a number. A rule's name is what the manager typed, so it is
/// answered here, from the frozen snapshot, to the club that wrote it and to nobody else.
/// </para>
/// <para>
/// A caller who holds no club, or whose club did not play, is not refused: watching someone else's match is the ordinary case
/// and the answer is simply that there is nothing of theirs to name.
/// </para>
/// </remarks>
public sealed class GetMatchInstructionNames
{
    private readonly ResolveOwnedClub _access;
    private readonly IMatchQueries _queries;

    /// <summary>Initializes the query.</summary>
    public GetMatchInstructionNames(ResolveOwnedClub access, IMatchQueries queries)
    {
        _access = access;
        _queries = queries;
    }

    /// <summary>Reads the caller's instruction names for a match.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="matchId">The match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetMatchInstructionNamesResult> ExecuteAsync(
        Guid userId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _queries.GetMatchAsync(matchId, cancellationToken);

        if (snapshot is null)
        {
            return new GetMatchInstructionNamesResult(MatchReadOutcome.MatchNotFound, null);
        }

        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return Found(null, []);
        }

        var side = access.ClubId == snapshot.Home.Id ? "home" : access.ClubId == snapshot.Away.Id ? "away" : null;

        if (side is null)
        {
            return Found(null, []);
        }

        var input = MatchSnapshotFactory.ReadVerified(snapshot.Snapshot);
        var plan = (side == "home" ? input.Home : input.Away).MatchPlan;

        return Found(
            side,
            [.. plan.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Name))
                .OrderBy(rule => rule.Ordinal)
                .Select(rule => new MatchInstructionNameResponse(rule.Ordinal, rule.Name!))]);
    }

    private static GetMatchInstructionNamesResult Found(string? side, IReadOnlyList<MatchInstructionNameResponse> rules) =>
        new(MatchReadOutcome.Found, new MatchInstructionNamesResponse(side, rules));
}
