using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The competition-rules read over HTTP: a division's points, its tie-break order, and its stored draw
/// (master plan §10.5, `TBL-1`…`TBL-11`).
/// </summary>
/// <remarks>
/// Public game data, so an authenticated manager who holds no club can read it — which is the point:
/// `TBL-11` asks for the draw to be visible, and a rule only some managers can inspect would not be.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CompetitionRulesTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public CompetitionRulesTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world this collection reads. Idempotent, so a second run is a no-op.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest("api-integration-world"), CancellationToken.None);
    }

    /// <summary>Nothing to tear down; the collection's fixture owns the database.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_division_reads_its_points_its_order_and_its_stored_draw()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var divisionId = await AnyDivisionIdAsync();

        var rules = (await client.GetFromJsonAsync<DivisionRulesResponse>(
            $"/api/v1/divisions/{divisionId}/rules"))!;

        rules.Points.Win.Should().Be(StandingsCalculator.PointsForWin, "TBL-1");
        rules.Points.Draw.Should().Be(StandingsCalculator.PointsForDraw);
        rules.Points.Loss.Should().Be(StandingsCalculator.PointsForLoss);

        rules.TieBreakers.Select(tieBreaker => tieBreaker.Code)
            .Should()
            .Equal(TieBreakers.Ordered, "the page lists the order the table applies, the draw last");

        rules.TieDrawSeed.Should().NotBeEmpty("TBL-11: the season committed to a draw before it began");
        rules.TieDrawHash.Should().HaveLength(64, "the published hash is a SHA-256 digest");

        rules.Clubs.Should().HaveCount(WorldRuleSet.ClubsPerDivision, "WORLD-4");
        rules.Clubs.Should().OnlyContain(club => !string.IsNullOrEmpty(club.ClubName));
        rules.Clubs.Select(club => club.DrawKey).Should().OnlyHaveUniqueItems(
            "the draw separates every club");

        rules.ServerTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), "TIME-5");
    }

    [Fact]
    public async Task An_unknown_division_answers_with_a_stable_code()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/rules");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(CompetitionErrorCodes.DivisionNotFound);
    }

    [Fact]
    public async Task An_unauthenticated_visitor_is_refused()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/rules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Reads any division identity out of the seeded world.</summary>
    private async Task<Guid> AnyDivisionIdAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<TouchlineManagerDbContext>()
            .Divisions
            .OrderBy(division => division.CreatedAt)
            .Select(division => division.Id)
            .FirstAsync();
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
