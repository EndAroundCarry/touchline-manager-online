using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// Gives a club a clean slate when a human manager takes it over (`FAN-9`).
/// </summary>
/// <remarks>
/// <para>
/// The fans go back to the tier's opening numbers whatever state the club was in: the count at the tier's start, the mood at calm, and
/// no review behind it. The place the fans hope for is worked out again from the squad as it is now, so a manager who inherits a weak
/// club mid-season is judged against what that squad can do and not against an old prediction.
/// </para>
/// <para>
/// It runs in the takeover's own transaction, beside the tenure, so a claimed club always has a reset fan base and a refused or retried
/// claim never resets twice. A resignation does not come here: the club goes back to the AI with its fans as they are. The rows before a
/// takeover stay, with a takeover event carrying the tenure that caused it.
/// </para>
/// </remarks>
public sealed class FanBaseTakeover
{
    private readonly IFanQueries _queries;
    private readonly FanBaseKeeper _keeper;

    /// <summary>Initializes the collaborator.</summary>
    public FanBaseTakeover(IFanQueries queries, FanBaseKeeper keeper)
    {
        _queries = queries;
        _keeper = keeper;
    }

    /// <summary>Resets a club's fans for the manager who takes it over.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="tier">The tier it plays in, starting at 1.</param>
    /// <param name="seasonId">The season being played.</param>
    /// <param name="tenureId">The new tenure.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResetAsync(
        Guid clubId,
        int tier,
        Guid seasonId,
        Guid tenureId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fanBase = (await _keeper.LoadAsync([clubId], _ => tier, seasonId, now, cancellationToken))[clubId];
        var division = await _queries.ListDivisionOfClubAsync(clubId, cancellationToken);

        int? target = null;

        if (division.Count > 0)
        {
            var strengths = await _queries.GetSquadStrengthsAsync([.. division.Select(row => row.ClubId)], cancellationToken);

            target = FanBaseKeeper.RankByStrength(strengths).TryGetValue(clubId, out var rank) ? rank : null;
        }

        _keeper.Change(fanBase, FanRuleSet.OnTakeover(tier), FanEventKind.Takeover, seasonId, now, tenureId: tenureId);
        fanBase.SetTarget(target, now);
    }
}
