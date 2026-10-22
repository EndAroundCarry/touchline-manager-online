using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Market;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Market;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The free-agent pool as the market reads it over HTTP, against the real composition root (`FA-5`).
/// </summary>
/// <remarks>
/// The week's batch is opened through the use case the worker runs, so these tests read the same listings a
/// manager would. Opening a batch is idempotent per week, so every test (and every run) shares the one pool.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FreeAgentMarketTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public FreeAgentMarketTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world and opens this week's batch. Idempotent, so a second run is a no-op.</summary>
    public async Task InitializeAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<SeedWorld>()
            .ExecuteAsync(new SeedWorldRequest("api-integration-world"), CancellationToken.None);

        var week = FreeAgentWeeks.WeekStartAtOrBefore(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow);

        await scope.ServiceProvider
            .GetRequiredService<RunFreeAgentBatch>()
            .ExecuteAsync(week, CancellationToken.None);
    }

    /// <summary>Nothing to tear down; the collection's fixture owns the database.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Browsing_the_free_agents_shows_no_seller_and_a_displayed_ability()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var page = await GetAsync<TransferListingsResponse>(
            client,
            "/api/v1/transfers/listings?kind=free_agent&pageSize=50");

        page.Listings.Should().HaveCount(50);
        page.Listings.Should().OnlyContain(row => row.Kind == "free_agent");
        page.Listings.Should().OnlyContain(row => row.SellerClubId == null && row.SellerClubName == null);
        page.Listings.Should().OnlyContain(row => row.Ability >= 1 && row.Ability <= 20);
        page.Listings.Should().Contain(
            row => row.Ability >= 6 && row.Ability <= 10,
            "a generated free agent shows 6 to 10 (FA-2)");
        page.Listings.Should().OnlyContain(row => row.GeneratedBuyerSeasonWageMinor > 0 && row.MinimumFeeMinor > 0);
        page.NextCursor.Should().NotBeNull();

        // Every free agent is a purchase, so each carries what buying him does to the viewer's wage bill and cash (WAGE-1).
        page.Listings.Should().OnlyContain(row => row.WageImpact != null && row.WageImpact.AddedSeasonWageMinor == row.GeneratedBuyerSeasonWageMinor);
        page.Listings.Should().OnlyContain(row => row.WageImpact!.NeedsConfirmation == (row.WageImpact.Level == "danger" || row.WageImpact.Level == "administration_risk"));

        var next = await GetAsync<TransferListingsResponse>(
            client,
            $"/api/v1/transfers/listings?kind=free_agent&pageSize=50&cursor={Uri.EscapeDataString(page.NextCursor!)}");

        next.Listings.Select(row => row.ListingId).Should()
            .NotIntersectWith(page.Listings.Select(row => row.ListingId), "a cursor never repeats a row");

        await ResignAsync(client);
    }

    [Fact]
    public async Task The_kind_filter_keeps_clubs_and_free_agents_apart_and_refuses_an_unknown_kind()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var clubs = await GetAsync<TransferListingsResponse>(client, "/api/v1/transfers/listings?kind=club&pageSize=50");

        // The shared world may hold no club listing at all, which is as good as none of the wrong kind.
        clubs.Listings.Any(row => row.Kind != "club" || row.SellerClubId is null).Should().BeFalse();

        var goalkeepers = await GetAsync<TransferListingsResponse>(
            client,
            "/api/v1/transfers/listings?kind=free_agent&family=Goalkeeper&pageSize=50");

        goalkeepers.Listings.Should().NotBeEmpty();
        goalkeepers.Listings.Should().OnlyContain(row => row.PrimaryPosition == "gk");

        var unknown = await client.GetAsync("/api/v1/transfers/listings?kind=pool");

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(unknown)).Should().Be(MarketErrorCodes.InvalidListingKind);

        var history = await client.GetAsync("/api/v1/transfers/history");

        history.StatusCode.Should().Be(HttpStatusCode.OK, "a null seller must not break the history read");

        await ResignAsync(client);
    }

    [Fact]
    public async Task The_hover_card_and_the_profile_work_for_a_free_agent_with_no_club()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var page = await GetAsync<TransferListingsResponse>(
            client,
            "/api/v1/transfers/listings?kind=free_agent&pageSize=5");
        var listing = page.Listings[0];

        var card = await GetAsync<PlayerSearchResultResponse>(client, $"/api/v1/scouting/players/{listing.PlayerId}");

        card.PlayerId.Should().Be(listing.PlayerId);
        card.ClubId.Should().BeNull();
        card.ClubName.Should().BeNull();
        card.IsListed.Should().BeTrue();
        card.Age.Should().Be(listing.Age);
        card.Attributes.Should().NotBeNull();

        var profile = await GetAsync<PlayerResponse>(client, $"/api/v1/players/{listing.PlayerId}");

        profile.Id.Should().Be(listing.PlayerId);
        profile.ClubId.Should().BeNull();
        profile.Club.Should().BeNull();
        profile.IsOwnClub.Should().BeFalse();
        profile.Status.Should().Be("free_agent");
        profile.Contract.Should().BeNull("the contract terms are a club's own");
        profile.State.Should().BeNull();

        var matches = await client.GetAsync($"/api/v1/players/{listing.PlayerId}/matches");

        matches.StatusCode.Should().Be(HttpStatusCode.OK);

        await ResignAsync(client);
    }

    [Fact]
    public async Task No_free_agent_response_carries_a_potential_or_a_gem_flag()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var listings = await client.GetStringAsync("/api/v1/transfers/listings?kind=free_agent&pageSize=50");
        var page = JsonSerializer.Deserialize<TransferListingsResponse>(listings, WebOptions);
        var playerId = page!.Listings[0].PlayerId;

        var bodies = new[]
        {
            listings,
            await client.GetStringAsync($"/api/v1/scouting/players/{playerId}"),
            await client.GetStringAsync($"/api/v1/players/{playerId}"),
            await client.GetStringAsync("/api/v1/transfers/history"),
            await client.GetStringAsync("/api/v1/transfers/mine"),
        };

        foreach (var body in bodies)
        {
            // The property names are what a client could read a flag from; the values are names and numbers.
            var names = PropertyNames(JsonDocument.Parse(body).RootElement).ToList();

            names.Should().NotContain(
                name => name.Contains("potential", StringComparison.OrdinalIgnoreCase),
                "hidden potential is server-only (I-1)");
            names.Should().NotContain(
                name => name.Equals("gem", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Gem", StringComparison.Ordinal),
                "nothing marks a hidden gem (FA-2)");
            names.Should().NotContain(
                name => name.Contains("batch", StringComparison.OrdinalIgnoreCase),
                "the batch behind a listing is server-only");
        }

        await ResignAsync(client);
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
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        var claim = await client.SendAsync(request);

        claim.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;

                    foreach (var inner in PropertyNames(property.Value))
                    {
                        yield return inner;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var inner in PropertyNames(item))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

    private static Task<HttpResponseMessage> ResignAsync(HttpClient client) =>
        client.PostAsync("/api/v1/club-tenure/resign", content: null);

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path}");

        var value = await response.Content.ReadFromJsonAsync<T>();

        value.Should().NotBeNull();

        return value!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
