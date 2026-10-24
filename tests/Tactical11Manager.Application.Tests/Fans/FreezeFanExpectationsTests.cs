using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Fans;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Tests.Fans;

/// <summary>
/// What each side's fans expect from a fixture when its sheets lock, for a visitor from another league (`FAN-3`, `FAN-4`, ADR-0087).
/// </summary>
/// <remarks>
/// A promotion play-off is hosted in one league's division-season and played by clubs of two, so the host's table holds only one of the two
/// clubs. The visitor's form is read in its own league, which is the whole of what is under test: without it the visitor's fans would
/// expect an even game from a side that had a very good or a very bad season.
/// </remarks>
public sealed class FreezeFanExpectationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 15, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_visitor_the_hosts_table_does_not_hold_is_read_in_its_own_league()
    {
        var host = Guid.CreateVersion7();
        var visitor = Guid.CreateVersion7();
        var fixtureId = Guid.CreateVersion7();
        var hostForm = new FanForm(30, 60);
        var visitorForm = new FanForm(30, 20);

        var fans = new StubFans();
        var queries = new StubQueries(
            hostTable: new Dictionary<Guid, FanForm> { [host] = hostForm },
            ownLeague: new Dictionary<Guid, FanForm> { [visitor] = visitorForm });

        var frozen = await new FreezeFanExpectations(fans, queries)
            .FreezeAsync(Guid.CreateVersion7(), [Sides(fixtureId, host, visitor)], Now, CancellationToken.None);

        frozen.Should().Be(1);
        queries.OwnLeagueAskedFor.Should().Equal([visitor], "only the club the host's table does not hold is read elsewhere");

        var blank = FanRuleSet.ExpectationFor(0, FanForm.None, 0, hostForm, atHome: false);
        var expected = FanRuleSet.ExpectationFor(0, visitorForm, 0, hostForm, atHome: false);

        expected.ExpectedPointsCentis.Should().NotBe(blank.ExpectedPointsCentis, "a poor season must change what the fans expect, or the test proves nothing");

        fans.Added.Single(expectation => expectation.ClubId == visitor).ExpectedPointsCentis
            .Should().Be(expected.ExpectedPointsCentis, "the visitor's fans expect what its own season gives");

        fans.Added.Single(expectation => expectation.ClubId == host).ExpectedPointsCentis
            .Should().Be(FanRuleSet.ExpectationFor(0, hostForm, 0, visitorForm, atHome: true).ExpectedPointsCentis);
    }

    [Fact]
    public async Task A_league_fixture_reads_only_its_own_table()
    {
        var home = Guid.CreateVersion7();
        var away = Guid.CreateVersion7();

        var queries = new StubQueries(
            hostTable: new Dictionary<Guid, FanForm> { [home] = new FanForm(10, 20), [away] = new FanForm(10, 12) },
            ownLeague: new Dictionary<Guid, FanForm>());

        await new FreezeFanExpectations(new StubFans(), queries)
            .FreezeAsync(Guid.CreateVersion7(), [Sides(Guid.CreateVersion7(), home, away)], Now, CancellationToken.None);

        queries.OwnLeagueAskedFor.Should().BeEmpty("both clubs are in the table, so nothing else is read");
    }

    private static FixtureSidesSnapshot Sides(Guid fixtureId, Guid home, Guid away) =>
        new(
            fixtureId,
            SeasonId: Guid.CreateVersion7(),
            WorldId: Guid.CreateVersion7(),
            new ClubSideSource(home, "Host FC", null, [], [], []),
            new ClubSideSource(away, "Visitor FC", null, [], [], []),
            []);

    private sealed class StubFans : IFanRepository
    {
        public List<FixtureFanExpectation> Added { get; } = [];

        public void AddExpectation(FixtureFanExpectation expectation) => Added.Add(expectation);

        public Task<IReadOnlyList<FixtureFanExpectation>> LoadExpectationsAsync(
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FixtureFanExpectation>>([]);

        public void Add(ClubFanBase fanBase) => throw new NotSupportedException();

        public void AddEvent(ClubFanEvent fanEvent) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClubFanBase>> LoadAsync(IReadOnlyCollection<Guid> clubIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> FindClubsReviewedAsync(DateOnly weekOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubQueries(
        IReadOnlyDictionary<Guid, FanForm> hostTable,
        IReadOnlyDictionary<Guid, FanForm> ownLeague) : IFanQueries
    {
        public List<Guid> OwnLeagueAskedFor { get; } = [];

        public Task<IReadOnlyDictionary<Guid, FanForm>> GetFormAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            Task.FromResult(hostTable);

        public Task<IReadOnlyDictionary<Guid, FanForm>> GetSeasonFormAsync(
            Guid seasonId,
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken)
        {
            OwnLeagueAskedFor.AddRange(clubIds);

            return Task.FromResult<IReadOnlyDictionary<Guid, FanForm>>(
                clubIds.Where(ownLeague.ContainsKey).ToDictionary(clubId => clubId, clubId => ownLeague[clubId]));
        }

        public Task<IReadOnlyList<FanSeasonClubRow>> ListCurrentSeasonAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FanSeasonClubRow>> ListDivisionOfClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, int>> GetSquadStrengthsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int?> GetDivisionTierAsync(Guid divisionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetFirstSeasonClubIdsAsync(
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FanClubRow?> GetClubAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FanEventRow>> ListEventsAsync(Guid clubId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, FanExpectation>> GetFrozenExpectationsAsync(
            Guid clubId,
            IReadOnlyCollection<Guid> fixtureIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
