using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// Freezes what each side's fans expect from a fixture at the moment its team sheets lock (`FAN-4`).
/// </summary>
/// <remarks>
/// <para>
/// The strength of a side is the best eleven the AI ordering would field from the squad the snapshot froze, and its form is the season so
/// far in the published table (<c>FanRuleSet.ExpectationFor</c>). A manager's own sheet is not read: the fans size up the squad, and this is
/// the same number the Fan Base page works out live before the lock.
/// </para>
/// <para>
/// A club that is not in the division-season's table, a visitor to a promotion play-off, is read in its own league's table. A fixture that already has its expectations is left alone, so a lock that repeats, or a resolution that freezes a fixture the lock never
/// reached, cannot change what the fans held.
/// </para>
/// </remarks>
public sealed class FreezeFanExpectations
{
    private readonly IFanRepository _fans;
    private readonly IFanQueries _queries;

    /// <summary>Initializes the collaborator.</summary>
    public FreezeFanExpectations(IFanRepository fans, IFanQueries queries)
    {
        _fans = fans;
        _queries = queries;
    }

    /// <summary>Freezes the expectations of both sides of some fixtures of one division-season.</summary>
    /// <param name="divisionSeasonId">The division-season the fixtures belong to, whose table gives the form.</param>
    /// <param name="sides">The fixtures' sides, as the snapshot builder reads them.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many fixtures were frozen.</returns>
    public async Task<int> FreezeAsync(
        Guid divisionSeasonId,
        IReadOnlyList<FixtureSidesSnapshot> sides,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sides);

        if (sides.Count == 0)
        {
            return 0;
        }

        var held = (await _fans.LoadExpectationsAsync([.. sides.Select(fixture => fixture.FixtureId)], cancellationToken))
            .Select(expectation => expectation.FixtureId)
            .ToHashSet();

        var open = sides.Where(fixture => !held.Contains(fixture.FixtureId)).OrderBy(fixture => fixture.FixtureId).ToList();

        if (open.Count == 0)
        {
            return 0;
        }

        var form = (await _queries.GetFormAsync(divisionSeasonId, cancellationToken)).ToDictionary(entry => entry.Key, entry => entry.Value);

        // A promotion play-off is hosted in one league and played by clubs of two, so a club the host's table does not hold is read in its
        // own league: its form is its own season, not a blank one.
        var strangers = open
            .SelectMany(fixture => new[] { fixture.Home.ClubId, fixture.Away.ClubId })
            .Where(clubId => !form.ContainsKey(clubId))
            .Distinct()
            .ToList();

        if (strangers.Count > 0)
        {
            foreach (var (clubId, own) in await _queries.GetSeasonFormAsync(open[0].SeasonId, strangers, cancellationToken))
            {
                form[clubId] = own;
            }
        }

        foreach (var fixture in open)
        {
            var homeStrength = TeamStrength.OfSquad(FanBaseKeeper.SquadOf(fixture.Home.Players));
            var awayStrength = TeamStrength.OfSquad(FanBaseKeeper.SquadOf(fixture.Away.Players));
            var homeForm = form.GetValueOrDefault(fixture.Home.ClubId, FanForm.None);
            var awayForm = form.GetValueOrDefault(fixture.Away.ClubId, FanForm.None);

            _fans.AddExpectation(FixtureFanExpectation.Freeze(
                fixture.FixtureId,
                fixture.Home.ClubId,
                FanRuleSet.ExpectationFor(homeStrength, homeForm, awayStrength, awayForm, atHome: true),
                now));

            _fans.AddExpectation(FixtureFanExpectation.Freeze(
                fixture.FixtureId,
                fixture.Away.ClubId,
                FanRuleSet.ExpectationFor(awayStrength, awayForm, homeStrength, homeForm, atHome: false),
                now));
        }

        return open.Count;
    }
}
