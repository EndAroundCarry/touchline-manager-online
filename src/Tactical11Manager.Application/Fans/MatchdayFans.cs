using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Fans;

/// <summary>
/// Moves both clubs' fans for every fixture a round published (`FAN-2`, `FAN-5`, `FAN-6`).
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <c>MatchdayFinances</c> for the crowd: publication calls it in the same transaction that made the results public, so
/// the result and what it did to the fans become public together. It stages its changes and never saves, which is what lets the caller
/// commit them with the table it just rebuilt.
/// </para>
/// <para>
/// Each result is judged against the expectation the fans held when the team sheets locked (<see cref="FreezeFanExpectations"/>). A fixture
/// locked before the fan module existed has none, and is judged as an even game at the ground it was played on. A republication returns
/// before it reaches here, and a fixture's match event is unique, so a result cannot move the fans twice.
/// </para>
/// </remarks>
public sealed class MatchdayFans
{
    private readonly IFanRepository _fans;
    private readonly IFanQueries _queries;
    private readonly FanBaseKeeper _keeper;

    /// <summary>Initializes the collaborator.</summary>
    public MatchdayFans(IFanRepository fans, IFanQueries queries, FanBaseKeeper keeper)
    {
        _fans = fans;
        _queries = queries;
        _keeper = keeper;
    }

    /// <summary>Applies one round's results to the fans of every club that played.</summary>
    /// <param name="seasonId">The season the round belongs to.</param>
    /// <param name="divisionId">The division the round belongs to, whose tier sets the fans' scale.</param>
    /// <param name="published">The fixtures the round published.</param>
    /// <param name="now">The current instant, taken from the caller's clock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many clubs' fans moved.</returns>
    public async Task<int> ApplyAsync(
        Guid seasonId,
        Guid divisionId,
        IReadOnlyList<Fixture> published,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(published);

        if (published.Count == 0)
        {
            return 0;
        }

        var tier = await _queries.GetDivisionTierAsync(divisionId, cancellationToken)
            ?? throw new InvalidOperationException($"Division {divisionId:D} has no tier, so its fans cannot be moved (FAN-5).");

        var clubIds = published.SelectMany(fixture => new[] { fixture.HomeClubId, fixture.AwayClubId }).Distinct().ToList();
        var bases = await _keeper.LoadAsync(clubIds, _ => tier, seasonId, now, cancellationToken);
        var firstSeason = await _queries.GetFirstSeasonClubIdsAsync(clubIds, cancellationToken);

        var expected = (await _fans.LoadExpectationsAsync([.. published.Select(fixture => fixture.Id)], cancellationToken))
            .ToDictionary(expectation => (expectation.FixtureId, expectation.ClubId));

        var moved = 0;

        foreach (var fixture in published.OrderBy(fixture => fixture.Id))
        {
            var homeGoals = fixture.HomeScore!.Value;
            var awayGoals = fixture.AwayScore!.Value;

            Apply(fixture, fixture.HomeClubId, atHome: true, homeGoals, awayGoals);
            Apply(fixture, fixture.AwayClubId, atHome: false, awayGoals, homeGoals);
            moved += 2;
        }

        return moved;

        void Apply(Fixture fixture, Guid clubId, bool atHome, int goalsFor, int goalsAgainst)
        {
            var fanBase = bases[clubId];
            var expectedPoints = expected.TryGetValue((fixture.Id, clubId), out var frozen)
                ? frozen.ExpectedPointsCentis
                : FanRuleSet.ExpectationForGap(0, atHome).ExpectedPointsCentis;

            var after = FanRuleSet.AfterMatch(
                fanBase.State,
                tier,
                expectedPoints,
                goalsFor,
                goalsAgainst,
                fixture.Id,
                clubId,
                firstSeason.Contains(clubId));

            _keeper.Change(
                fanBase,
                after,
                FanEventKind.Match,
                seasonId,
                now,
                fixtureId: fixture.Id,
                expectedPointsCentis: expectedPoints,
                pointsWon: goalsFor > goalsAgainst ? 3 : goalsFor == goalsAgainst ? 1 : 0);
        }
    }
}
