using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Contracts.Market;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The market surface over HTTP, against the real composition root (master plan §10.6; `SCT-*`, `TRF-*`).
/// </summary>
/// <remarks>
/// The interface-level half of the market: a manager searches players and keeps a shortlist, lists a player
/// with an idempotency key, and a rival bids — refused when the bid is too low, and reserving funds when it
/// leads (`TRF-5`, `TRF-7`).
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class MarketTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MarketTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task Scouting_refuses_an_unauthenticated_caller()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/scouting/players?pageSize=5");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_manager_searches_players_and_keeps_a_shortlist()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var search = await client.GetFromJsonAsync<PlayerSearchResponse>("/api/v1/scouting/players?pageSize=5");

        search.Should().NotBeNull();
        search!.Players.Should().NotBeEmpty("a seeded world has thousands of players (SCT-1)");

        // The scouting payload is public attributes only: no hidden potential or reputation (I-1).
        var first = search.Players[0];

        first.Attributes.Should().NotBeNull();

        var add = await client.PostAsJsonAsync(
            $"/api/v1/shortlist/{first.PlayerId}",
            new ShortlistRequest("ready to move"));

        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await client.GetFromJsonAsync<ShortlistResponse>("/api/v1/shortlist");

        list!.Entries.Should().ContainSingle(entry => entry.PlayerId == first.PlayerId, "SCT-3");
        list.Entries[0].Notes.Should().Be("ready to move");

        var removed = await client.DeleteAsync($"/api/v1/shortlist/{first.PlayerId}");

        removed.StatusCode.Should().Be(HttpStatusCode.OK);

        var empty = await client.GetFromJsonAsync<ShortlistResponse>("/api/v1/shortlist");

        empty!.Entries.Should().BeEmpty();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Listing_needs_an_idempotency_key_and_replays_with_the_same_key()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var playerId = await FirstListablePlayerAsync(client, clubId);
        var body = new CreateListingRequest(playerId, MinimumFeeMinor: 5_000_000, Seasons: 2);

        var missing = await client.PostAsJsonAsync("/api/v1/transfers/listings", body);

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest, "INT-2");
        (await CodeAsync(missing)).Should().Be(MarketErrorCodes.IdempotencyKeyRequired);

        var key = Guid.CreateVersion7().ToString();
        var created = await PostWithKeyAsync(client, "/api/v1/transfers/listings", body, key);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var listing = await ReadAsync<TransferListingResponse>(created);

        listing.PlayerId.Should().Be(playerId, "TRF-1");
        listing.Status.Should().Be("open");
        listing.GeneratedBuyerWageMinor.Should().BePositive("CON-5: the buyer's terms are shown before a bid");

        // The same key and request returns the listing it already opened, not a second one (T-4).
        var replayed = await PostWithKeyAsync(client, "/api/v1/transfers/listings", body, key);

        replayed.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync<TransferListingResponse>(replayed)).ListingId.Should().Be(listing.ListingId);

        // A different key for the same player is refused, because it already has an open listing (TRF-14).
        var again = await PostWithKeyAsync(
            client,
            "/api/v1/transfers/listings",
            body,
            Guid.CreateVersion7().ToString());

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be(MarketErrorCodes.PlayerAlreadyListed);

        var cancelled = await DeleteWithKeyAsync(client, $"/api/v1/transfers/listings/{listing.ListingId}");

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_bid_below_the_floor_is_refused_and_a_leading_bid_reserves_funds()
    {
        using var sellerClient = CreateClient();
        using var bidderClient = CreateClient();

        var seller = await AuthScenario.CreateVerifiedManagerAsync(_fixture, sellerClient);
        var bidder = await AuthScenario.CreateVerifiedManagerAsync(_fixture, bidderClient);

        sellerClient.WithBearer(seller.AccessToken);
        bidderClient.WithBearer(bidder.AccessToken);

        var sellerClubId = await OnboardAsync(sellerClient);
        await OnboardAsync(bidderClient);

        var playerId = await FirstListablePlayerAsync(sellerClient, sellerClubId);
        const long floor = 5_000_000;

        var created = await PostWithKeyAsync(
            sellerClient,
            "/api/v1/transfers/listings",
            new CreateListingRequest(playerId, floor, 2),
            Guid.CreateVersion7().ToString());
        var listing = await ReadAsync<TransferListingResponse>(created);

        // Too low: below the seller's minimum fee (TRF-5).
        var tooLow = await PostWithKeyAsync(
            bidderClient,
            $"/api/v1/transfers/listings/{listing.ListingId}/bids",
            new PlaceBidRequest(1_000_000),
            Guid.CreateVersion7().ToString());

        tooLow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(tooLow)).Should().Be(MarketErrorCodes.BidTooLow);

        // A bid that clears the floor leads and reserves the amount (TRF-4, TRF-7).
        var bid = await PostWithKeyAsync(
            bidderClient,
            $"/api/v1/transfers/listings/{listing.ListingId}/bids",
            new PlaceBidRequest(floor),
            Guid.CreateVersion7().ToString());

        bid.StatusCode.Should().Be(HttpStatusCode.OK);

        var leading = await ReadAsync<TransferListingResponse>(bid);

        leading.LeadingAmountMinor.Should().Be(floor);
        leading.YourBidMinor.Should().Be(floor);

        var summary = await bidderClient.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary");

        summary!.ReservedMinor.Should().Be(floor, "FIN-10, TRF-7");

        // A club cannot bid on its own player (TRF-1).
        var own = await PostWithKeyAsync(
            sellerClient,
            $"/api/v1/transfers/listings/{listing.ListingId}/bids",
            new PlaceBidRequest(floor),
            Guid.CreateVersion7().ToString());

        own.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(own)).Should().Be(MarketErrorCodes.CannotBidOnOwnPlayer);

        await bidderClient.PostAsync("/api/v1/club-tenure/resign", content: null);
        await sellerClient.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>();

        value.Should().NotBeNull();

        return value!;
    }

    private static async Task<Guid> FirstSquadPlayerAsync(HttpClient client, Guid clubId)
    {
        var squad = await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad");

        return squad!.Players[0].Id;
    }

    /// <summary>
    /// Picks a player in the club who does not already hold an open listing, so a leftover listing from
    /// another test in the shared world cannot collide with this one (`TRF-14`).
    /// </summary>
    private static async Task<Guid> FirstListablePlayerAsync(HttpClient client, Guid clubId)
    {
        var search = await client.GetFromJsonAsync<PlayerSearchResponse>(
            $"/api/v1/scouting/players?clubId={clubId}&pageSize=50");

        var player = search!.Players.FirstOrDefault(candidate => !candidate.IsListed);

        return player is null
            ? await FirstSquadPlayerAsync(client, clubId)
            : player.PlayerId;
    }

    private static Task<HttpResponseMessage> PostWithKeyAsync<TBody>(
        HttpClient client,
        string path,
        TBody body,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> DeleteWithKeyAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        return client.SendAsync(request);
    }

    private static async Task<Guid> OnboardAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var claim = await ClaimAsync(client, clubId, Guid.CreateVersion7().ToString());

        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
    }

    private static async Task<(Guid CountryId, Guid ClubId)> FirstAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return (country.Id, available.Id);
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid clubId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
