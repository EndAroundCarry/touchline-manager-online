using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.Fans;
using Tactical11Manager.Domain.Fans;

namespace Tactical11Manager.Application.Fans;

/// <summary>The result of reading what anyone can see of a club's fans.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Summary">The summary, when the read succeeded.</param>
public sealed record GetClubFanSummaryResult(SquadReadOutcome Outcome, FanSummaryResponse? Summary);

/// <summary>
/// Reads how many fans any club has and how they feel, in a word (`FAN-1`, `FAN-2`).
/// </summary>
/// <remarks>
/// Public game data like a club's page, so a manager can see how big a rival is. It is a summary on purpose: the exact mood, the
/// fans' target and the history stay with the manager who holds the club (<see cref="GetFanBase"/>).
/// </remarks>
public sealed class GetClubFanSummary
{
    private readonly IFanQueries _fans;

    /// <summary>Initializes the query.</summary>
    public GetClubFanSummary(IFanQueries fans) => _fans = fans;

    /// <summary>Reads the club's fans, or reports that the club does not exist.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetClubFanSummaryResult> ExecuteAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await _fans.GetClubAsync(clubId, cancellationToken);

        if (club is null)
        {
            return new GetClubFanSummaryResult(SquadReadOutcome.ClubNotFound, null);
        }

        var word = FanMoods.WordFor(club.MoodBp);

        return new GetClubFanSummaryResult(
            SquadReadOutcome.Found,
            new FanSummaryResponse(club.ClubId, club.Name, club.Fans, FanMoods.Code(word), FanMoods.Phrase(word)));
    }
}
