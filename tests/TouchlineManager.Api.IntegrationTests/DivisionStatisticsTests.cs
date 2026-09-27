using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The division statistics read over HTTP: a played season's player totals (master plan §10.5, §11.1).
/// </summary>
/// <remarks>
/// Arranged by playing a round through the real workflow, because a statistics projection exists only once
/// the worker has published something. The read is public game data, so an authenticated manager who holds
/// no club can read it.
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class DivisionStatisticsTests
{
    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public DivisionStatisticsTests(MatchApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_played_division_reads_its_player_statistics_most_goals_first()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var divisionId = await PublishNextRoundAsync();

        var statistics = (await client.GetFromJsonAsync<DivisionStatisticsResponse>(
            $"/api/v1/divisions/{divisionId}/statistics"))!;

        statistics.Rows.Should().NotBeEmpty("a published round gives the players who appeared a line");
        statistics.ServerTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), "TIME-5");

        statistics.Rows.Select(row => row.Goals).Should().BeInDescendingOrder();

        foreach (var row in statistics.Rows)
        {
            row.PlayerName.Should().NotBeEmpty();
            row.Appearances.Should().BeGreaterThanOrEqualTo(1);
            row.Starts.Should().BeLessThanOrEqualTo(row.Appearances);
            row.ShotsOnTarget.Should().BeLessThanOrEqualTo(row.Shots);

            if (row.AverageRating is decimal rating)
            {
                rating.Should().BeInRange(0.0m, 10.0m, "TRN-8: the rating arrives on its display scale");
            }
        }

        // Every goal a side scored is attributed to exactly one player, so the division's player goals and
        // the goals its published fixtures record are the same number (MAT-5, applied to the projection).
        var recorded = await RecordedGoalsAsync(divisionId);

        statistics.Rows.Sum(row => row.Goals).Should().Be(recorded);
    }

    [Fact]
    public async Task An_unknown_division_answers_with_a_stable_code()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/statistics");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(CompetitionErrorCodes.DivisionNotFound);
    }

    [Fact]
    public async Task An_unauthenticated_visitor_is_refused()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/statistics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Plays the next untouched round and returns the division it belongs to.</summary>
    private async Task<Guid> PublishNextRoundAsync()
    {
        Guid matchdayId;
        Guid divisionSeasonId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var matchday = await dbContext.Matchdays
                .Where(candidate => candidate.PublicationStatus == MatchdayPublicationStatus.Pending
                    && !dbContext.Fixtures.Any(fixture => fixture.MatchdayId == candidate.Id
                        && fixture.Status != FixtureStatus.Scheduled))
                .OrderBy(candidate => candidate.RoundNumber)
                .Select(candidate => new { candidate.Id, candidate.DivisionSeasonId })
                .FirstAsync();

            matchdayId = matchday.Id;
            divisionSeasonId = matchday.DivisionSeasonId;
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LockMatchday>()
                .ExecuteAsync(matchdayId, CancellationToken.None);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
                .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
                .ExecuteAsync(matchdayId, CancellationToken.None);

            published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        }

        await using var read = _fixture.Factory.Services.CreateAsyncScope();

        return await read.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>()
            .DivisionSeasons
            .Where(candidate => candidate.Id == divisionSeasonId)
            .Select(candidate => candidate.DivisionId)
            .SingleAsync();
    }

    /// <summary>Sums the goals every published fixture of the division records.</summary>
    private async Task<int> RecordedGoalsAsync(Guid divisionId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await (
            from fixture in dbContext.Fixtures
            join matchday in dbContext.Matchdays on fixture.MatchdayId equals matchday.Id
            join divisionSeason in dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.DivisionId == divisionId
                && fixture.Status == FixtureStatus.Published
            select (fixture.HomeScore ?? 0) + (fixture.AwayScore ?? 0))
            .SumAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
