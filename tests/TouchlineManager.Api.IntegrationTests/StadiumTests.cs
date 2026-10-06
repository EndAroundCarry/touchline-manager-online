using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The stadium over HTTP, against the real composition root (`STAD-1`…`STAD-6`).
/// </summary>
/// <remarks>
/// The tests share one world, and a club's ground outlives the manager who built it, so none of them assumes
/// the opening ground: each reads the stadium it was given and asserts what its own order changed. The
/// opening ground itself is covered where it is generated.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class StadiumTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public StadiumTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_manager_reads_the_stadium_with_its_prices_costs_and_the_club_colours()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var response = await client.GetAsync("/api/v1/stadium");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull("the ground's version is the entity tag a build order carries");

        var stadium = (await response.Content.ReadFromJsonAsync<StadiumResponse>())!;

        stadium.Stands.Select(stand => stand.Stand)
            .Should().Equal("standing", "seating", "covered_seating", "vip");
        stadium.Capacity.Should().Be(stadium.Stands.Sum(stand => stand.Seats), "the ground is its four stands");
        stadium.Level.Should().Be((stadium.Capacity + 4_999) / 5_000, "the level is the number of 5,000-place blocks");
        stadium.MaxLevel.Should().Be(10);
        stadium.MaxCapacity.Should().Be(50_000);

        stadium.Stands.Select(stand => stand.TicketPriceMinor)
            .Should().BeInAscendingOrder("a better place costs more").And.OnlyHaveUniqueItems();
        stadium.Stands.Select(stand => stand.BuildCostMinor)
            .Should().BeInAscendingOrder("a better place costs more to build").And.OnlyHaveUniqueItems();

        stadium.PrimaryColour.Should().MatchRegex("^#[0-9a-f]{6}$", "the seats are drawn in the club's colour");
        stadium.SecondaryColour.Should().MatchRegex("^#[0-9a-f]{6}$");
        stadium.AvailableMinor.Should().BePositive();
        stadium.FullHouseMinor.Should().BeGreaterThan(stadium.ExpectedGateMinor - 1);

        await ResignAsync(client);
    }

    [Fact]
    public async Task Building_places_charges_the_club_grows_the_ground_and_lands_on_the_ledger()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadStadiumAsync(client);
        var standing = before.Stands.Single(stand => stand.Stand == "standing");

        var response = await BuildAsync(client, "standing", 10, before.Version);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await response.Content.ReadFromJsonAsync<StadiumResponse>())!;

        after.Stands.Single(stand => stand.Stand == "standing").Seats.Should().Be(standing.Seats + 10);
        after.Capacity.Should().Be(before.Capacity + 10);
        after.Version.Should().BeGreaterThan(before.Version);
        after.AvailableMinor.Should().Be(
            before.AvailableMinor - (standing.BuildCostMinor * 10),
            "ten places at the tier's price leave the purse (STAD-4)");
        response.Headers.ETag!.Tag.Should().Be($"\"{after.Version}\"");

        var ledger = (await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger"))!;
        var entry = ledger.Entries[0];

        entry.Category.Should().Be("stadium_construction");
        entry.CashDeltaMinor.Should().Be(-(standing.BuildCostMinor * 10));
        entry.Description.Should().Be("Stadium works: 10 standing places");

        await ResignAsync(client);
    }

    [Fact]
    public async Task An_order_without_a_version_is_refused()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var response = await BuildAsync(client, "standing", 1, version: null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionRequired);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_repeated_order_is_refused_rather_than_paid_for_twice()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadStadiumAsync(client);
        var first = await BuildAsync(client, "standing", 5, before.Version);

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // The same order again, still holding the version it read: a double-click or a retry.
        var second = await BuildAsync(client, "standing", 5, before.Version);

        second.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await CodeAsync(second)).Should().Be(ApiErrorCodes.PreconditionFailed);

        var after = await ReadStadiumAsync(client);

        after.Capacity.Should().Be(before.Capacity + 5, "only the first order was built");

        await ResignAsync(client);
    }

    [Fact]
    public async Task An_order_the_club_cannot_pay_for_is_refused_and_builds_nothing()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadStadiumAsync(client);
        var vip = before.Stands.Single(stand => stand.Stand == "vip");
        var tooMany = (int)(before.AvailableMinor / vip.BuildCostMinor) + 1;

        // The ground must have room, or the order is refused for being too big rather than too dear.
        before.MaxCapacity.Should().BeGreaterThan(before.Capacity + tooMany);

        var response = await BuildAsync(client, "vip", tooMany, before.Version);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(StadiumErrorCodes.InsufficientFunds);

        (await ReadStadiumAsync(client)).Capacity.Should().Be(before.Capacity);

        await ResignAsync(client);
    }

    [Fact]
    public async Task An_order_past_the_largest_ground_is_refused()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadStadiumAsync(client);
        var response = await BuildAsync(client, "standing", before.MaxCapacity, before.Version);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be(StadiumErrorCodes.StadiumFull);

        await ResignAsync(client);
    }

    [Theory]
    [InlineData("terrace", 5)]
    [InlineData("standing", 0)]
    [InlineData("standing", -3)]
    [InlineData(null, 5)]
    public async Task An_order_for_an_unknown_stand_or_a_bad_count_is_a_validation_error(string? stand, int count)
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var before = await ReadStadiumAsync(client);
        var response = await BuildAsync(client, stand, count, before.Version);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.ValidationFailed);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var read = await client.GetAsync("/api/v1/stadium");
        var build = await BuildAsync(client, "standing", 1, version: 1);

        read.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(read)).Should().Be(SquadErrorCodes.NoClub);
        build.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(build)).Should().Be(SquadErrorCodes.NoClub);
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

    private static async Task<StadiumResponse> ReadStadiumAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<StadiumResponse>("/api/v1/stadium"))!;

    private static Task<HttpResponseMessage> BuildAsync(HttpClient client, string? stand, int count, long? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stadium/seats")
        {
            Content = JsonContent.Create(new { stand, count }),
        };

        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ResignAsync(HttpClient client) =>
        client.PostAsync("/api/v1/club-tenure/resign", content: null);

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
