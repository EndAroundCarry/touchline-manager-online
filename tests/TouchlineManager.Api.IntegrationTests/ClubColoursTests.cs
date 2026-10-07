using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// A manager choosing the two colours their club plays in, over HTTP against the real composition root.
/// </summary>
/// <remarks>
/// The tests share one world and a club outlives the manager who held it, so each reads the colours it was
/// given and asserts what its own change did, and each gives its club back when it is done.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ClubColoursTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ClubColoursTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these tests walk. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_club_always_reports_the_colours_it_plays_in_chosen_or_generated()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var tenure = await ReadTenureAsync(client);

        // Whether a previous manager of this club chose colours is not this test's business: either way the
        // club has a pair, so the colour picker always has something to start from.
        tenure.PrimaryColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        tenure.SecondaryColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        tenure.PrimaryColour.Should().NotBe(tenure.SecondaryColour);

        await ResignAsync(client);
    }

    [Fact]
    public async Task Choosing_colours_saves_them_and_the_tenure_and_the_stadium_both_wear_them()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var response = await client.PutAsJsonAsync(
            "/api/v1/club-tenure/colours",
            new { primaryColour = "#C0392B", secondaryColour = "#fcd116" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var saved = (await response.Content.ReadFromJsonAsync<ClubColoursResponse>())!;

        saved.PrimaryColour.Should().Be("#c0392b", "colours are stored in one lower-case form");
        saved.SecondaryColour.Should().Be("#fcd116");

        var tenure = await ReadTenureAsync(client);

        tenure.HasChosenColours.Should().BeTrue();
        tenure.PrimaryColour.Should().Be("#c0392b");
        tenure.SecondaryColour.Should().Be("#fcd116");

        var stadium = (await client.GetFromJsonAsync<StadiumColours>("/api/v1/stadium"))!;

        stadium.PrimaryColour.Should().Be("#c0392b");
        stadium.SecondaryColour.Should().Be("#fcd116");

        await ResignAsync(client);
    }

    [Fact]
    public async Task Colours_can_be_chosen_again_and_the_last_choice_stands()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        (await PutColoursAsync(client, "#c0392b", "#fcd116")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutColoursAsync(client, "#12284c", "#ffffff")).StatusCode.Should().Be(HttpStatusCode.OK);

        var tenure = await ReadTenureAsync(client);

        tenure.PrimaryColour.Should().Be("#12284c");
        tenure.SecondaryColour.Should().Be("#ffffff");

        await ResignAsync(client);
    }

    [Theory]
    [InlineData("#1f4e79", "#1F4E79", "SecondaryColour")]
    [InlineData("blue", "#d6e4f0", "PrimaryColour")]
    [InlineData("#1f4e79", "#12", "SecondaryColour")]
    public async Task Colours_that_are_not_two_different_hex_colours_are_refused_and_nothing_changes(
        string primary,
        string secondary,
        string field)
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadTenureAsync(client);
        var response = await PutColoursAsync(client, primary, secondary);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field, "the answer names the field that is wrong");

        var after = await ReadTenureAsync(client);

        after.PrimaryColour.Should().Be(before.PrimaryColour);
        after.SecondaryColour.Should().Be(before.SecondaryColour);
        after.HasChosenColours.Should().Be(before.HasChosenColours);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_manager_with_no_club_has_no_colours_to_choose()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await PutColoursAsync(client, "#c0392b", "#fcd116");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("NO_ACTIVE_TENURE");
    }

    [Fact]
    public async Task Choosing_colours_needs_a_signed_in_manager()
    {
        using var client = CreateClient();

        var response = await PutColoursAsync(client, "#c0392b", "#fcd116");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    private async Task SignInAndOnboardAsync(HttpClient client)
    {
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var clubId = await FirstAvailableClubAsync(client);
        var claim = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        claim.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        (await client.SendAsync(claim)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<Guid> FirstAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return available.Id;
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    private static async Task<ClubTenureSummaryResponse> ReadTenureAsync(HttpClient client)
    {
        var state = await client.GetFromJsonAsync<OnboardingStateResponse>("/api/v1/club-tenure");

        return state!.Tenure!;
    }

    private static Task<HttpResponseMessage> PutColoursAsync(HttpClient client, string primary, string secondary) =>
        client.PutAsJsonAsync(
            "/api/v1/club-tenure/colours",
            new { primaryColour = primary, secondaryColour = secondary });

    private static Task<HttpResponseMessage> ResignAsync(HttpClient client) =>
        client.PostAsync("/api/v1/club-tenure/resign", content: null);

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The two fields of the stadium response these tests read.</summary>
    private sealed record StadiumColours(string PrimaryColour, string SecondaryColour);
}
