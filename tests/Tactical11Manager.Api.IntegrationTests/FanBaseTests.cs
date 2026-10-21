using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Fans;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The fan base over HTTP, against the real composition root (`FAN-1`…`FAN-10`).
/// </summary>
/// <remarks>
/// What is checked here is the seam: the route, the club the page reads (the caller's, never one from the request), the history
/// choice, and the refusals every club read gives. The numbers behind the page are checked where they are made.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FanBaseTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public FanBaseTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these reads walk. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_manager_reads_the_fans_of_their_own_club_from_the_takeover_on()
    {
        using var client = CreateClient();
        var clubId = await SignInAndOnboardAsync(client);

        var response = await client.GetAsync("/api/v1/fans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await response.Content.ReadFromJsonAsync<FanBaseResponse>())!;
        var opening = FanRuleSet.OpeningFor(page.TierNumber);

        page.ClubId.Should().Be(clubId);
        page.ClubName.Should().NotBeNullOrWhiteSpace();
        page.Fans.Should().Be(opening.Fans);
        page.MoodCode.Should().Be("calm");
        page.FloorFans.Should().Be(FanRuleSet.FloorFansFor(page.TierNumber));
        page.SoftCapFans.Should().Be(FanRuleSet.SoftCapFansFor(page.TierNumber));
        page.History.Should().Be(FanHistoryScopes.Tenure);
        page.Events.Should().ContainSingle().Which.Kind.Should().Be("takeover");
        page.TenureStartedAt.Should().Be(page.Events[0].OccurredAt);
        page.NextFixture.Should().NotBeNull();
        page.NextFixture!.Fixture.OpponentName.Should().NotBeNullOrWhiteSpace();
        (page.NextFixture.Expectation.WinBp + page.NextFixture.Expectation.DrawBp + page.NextFixture.Expectation.LossBp).Should().Be(10_000);
        page.NextFixture.Expectation.LabelPhrase.Should().NotBeNullOrWhiteSpace();

        await ResignAsync(client);
    }

    [Fact]
    public async Task The_whole_club_history_can_be_asked_for_and_starts_with_the_opening()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var page = (await client.GetFromJsonAsync<FanBaseResponse>("/api/v1/fans?history=club"))!;

        page.History.Should().Be(FanHistoryScopes.Club);
        page.Events[0].Kind.Should().Be("opening");
        page.Events[^1].Kind.Should().Be("takeover");

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_history_the_page_does_not_serve_is_a_bad_request_by_name()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var response = await client.GetAsync("/api/v1/fans?history=forever");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(FanErrorCodes.InvalidHistory);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync("/api/v1/manager-profile", new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await client.GetAsync("/api/v1/fans");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);
    }

    [Fact]
    public async Task Both_fan_reads_require_a_signed_in_manager()
    {
        using var client = CreateClient();

        (await client.GetAsync("/api/v1/fans")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/fans")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Any_manager_reads_how_big_a_rival_is_but_only_in_words()
    {
        using var client = CreateClient();
        var ownClubId = await SignInAndOnboardAsync(client);
        var rivalId = await AnotherClubAsync(client, ownClubId);

        var response = await client.GetAsync($"/api/v1/clubs/{rivalId}/fans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await response.Content.ReadFromJsonAsync<FanSummaryResponse>())!;

        summary.ClubId.Should().Be(rivalId);
        summary.ClubName.Should().NotBeNullOrWhiteSpace();
        summary.Fans.Should().BeGreaterThan(0);
        summary.MoodCode.Should().NotBeNullOrWhiteSpace();
        summary.MoodPhrase.Should().NotBeNullOrWhiteSpace();

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContain("moodBp", "the number behind the word stays with the club's own manager");
        raw.Should().NotContain("seasonTargetRank");

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_club_that_does_not_exist_has_no_fans_to_read()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var response = await client.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/fans");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await ResignAsync(client);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Signs a new manager in, creates their profile, and claims the first club the world offers.</summary>
    private async Task<Guid> SignInAndOnboardAsync(HttpClient client)
    {
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var profile = await client.PostAsJsonAsync("/api/v1/manager-profile", new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var clubId = await FirstAvailableClubAsync(client);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        var claim = await client.SendAsync(request);

        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
    }

    private static Task<HttpResponseMessage> ResignAsync(HttpClient client) =>
        client.PostAsync("/api/v1/club-tenure/resign", content: null);

    /// <summary>Finds the first club in the world that still has no manager.</summary>
    private static async Task<Guid> FirstAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>($"/api/v1/countries/{country.Id}/available-clubs");
            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return available.Id;
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    /// <summary>Finds a club other than the manager's own, held by no one or not.</summary>
    private static async Task<Guid> AnotherClubAsync(HttpClient client, Guid ownClubId)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");
        var listing = await client.GetFromJsonAsync<AvailableClubsResponse>($"/api/v1/countries/{countries![0].Id}/available-clubs");

        return listing!.Clubs.First(club => club.Id != ownClubId).Id;
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
