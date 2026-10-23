using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Competition;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Infrastructure.Tests.Competition;

/// <summary>
/// Several divisions publishing one round at the same kickoff (`CONC-2`, §7.4).
/// </summary>
/// <remarks>
/// Its own world, because it publishes the opening round of four divisions, which the shared matchday collection's tests expect to find
/// unplayed. The use case is called directly, with no worker around it to rerun a collision, so a serialization failure fails the test.
/// </remarks>
public sealed class ConcurrentPublicationTests : IAsyncLifetime, IDisposable
{
    private const int Divisions = 4;

    private readonly MatchdayFixture _fixture = new();

    /// <summary>Starts a freshly seeded world.</summary>
    public Task InitializeAsync() => _fixture.InitializeAsync();

    /// <summary>Stops and removes the world.</summary>
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Divisions_publishing_the_same_round_at_once_take_turns_and_all_publish()
    {
        // Two divisions share no row, but their accounts, tables, fan bases and player states share pages, and serializable publications
        // that ran side by side rolled each other back (40001). Taking turns is what lets every one of them commit at the first try.
        List<Guid> rounds;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            rounds = await db.Matchdays
                .Where(matchday => matchday.RoundNumber == 1)
                .OrderBy(matchday => matchday.Id)
                .Select(matchday => matchday.Id)
                .Take(Divisions)
                .ToListAsync();

            foreach (var round in rounds)
            {
                await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(round, CancellationToken.None);
                await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(round, jobId: null, CancellationToken.None);
            }
        }

        var results = await Task.WhenAll(rounds.Select(async round =>
        {
            await using var scope = _fixture.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(round, CancellationToken.None);
        }));

        results.Should().OnlyContain(result => result.Outcome == PublishMatchdayOutcome.Published);

        await using var read = _fixture.CreateScope();
        var check = read.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

        (await check.Matchdays.CountAsync(matchday => rounds.Contains(matchday.Id) && matchday.PublicationStatus == MatchdayPublicationStatus.Published))
            .Should().Be(Divisions);
    }

    [Fact]
    public async Task A_publication_that_waited_behind_another_of_the_same_round_finds_it_published()
    {
        // A redelivered job queues behind the first and must not publish the round a second time.
        Guid round;

        await using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            round = await db.Matchdays
                .Where(matchday => matchday.RoundNumber == 1)
                .OrderByDescending(matchday => matchday.Id)
                .Select(matchday => matchday.Id)
                .FirstAsync();

            await scope.ServiceProvider.GetRequiredService<LockMatchday>().ExecuteAsync(round, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<ResolveMatchday>().ExecuteAsync(round, jobId: null, CancellationToken.None);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var scope = _fixture.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<PublishMatchday>().ExecuteAsync(round, CancellationToken.None);
        }));

        results.Select(result => result.Outcome).Should().BeEquivalentTo(
            [PublishMatchdayOutcome.Published, PublishMatchdayOutcome.AlreadyPublished]);
    }
}
