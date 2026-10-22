using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The two ways of taking a club over, over HTTP: the preview that shows both, and the claim that chooses one (`TKO-1`…`TKO-4`).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TakeoverTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TakeoverTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_manager_reads_both_options_for_a_club_before_choosing()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var preview = await client.GetFromJsonAsync<TakeoverPreviewResponse>($"/api/v1/clubs/{clubId}/takeover-preview");

        preview.Should().NotBeNull();
        preview!.ClubId.Should().Be(clubId);
        preview.TierNumber.Should().Be(1);
        preview.Keep.Start.Should().Be("keep");
        preview.Keep.Players.Should().BePositive();
        preview.Keep.SeasonWageMinor.Should().BePositive();
        preview.Keep.GroundPlaces.Should().BeGreaterThanOrEqualTo(5_000);

        preview.CleanStart.Start.Should().Be("clean_start");
        preview.CleanStart.CashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));
        preview.CleanStart.DebtMinor.Should().Be(0);
        preview.CleanStart.GroundPlaces.Should().Be(5_000);
        preview.CleanStart.SnackBarLevel.Should().Be(1);
        preview.CleanStart.SeasonWageMinor.Should().BeLessThan(
            preview.CleanStart.SeasonIncomeForecastMinor,
            "a clean-start club earns more than its squad costs, whatever sponsor deal the season already has");
        preview.CleanStart.OverdraftLimitMinor.Should().BePositive();
    }

    [Fact]
    public async Task The_preview_of_an_unknown_club_is_not_found_and_needs_a_signed_in_manager()
    {
        using var anonymous = CreateClient();

        (await anonymous.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/takeover-preview")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/takeover-preview");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(WorldErrorCodes.ClubNotFound);
    }

    [Fact]
    public async Task A_claim_that_asks_for_a_start_nobody_offers_is_a_bad_request()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await CreateProfileAsync(client);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var response = await ClaimAsync(client, clubId, "fresh");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_manager_who_starts_clean_gets_the_squad_the_preview_showed_and_the_opening_cash()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await CreateProfileAsync(client);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var preview = await client.GetFromJsonAsync<TakeoverPreviewResponse>($"/api/v1/clubs/{clubId}/takeover-preview");
        var claim = await ClaimAsync(client, clubId, "clean_start");

        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        var summary = await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary");
        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        summary!.CashMinor.Should().Be(preview!.CleanStart.CashMinor);
        summary.ContractedPlayers.Should().Be(preview.CleanStart.Players);
        contracts!.SeasonWageTotalMinor.Should().Be(preview.CleanStart.SeasonWageMinor, "the squad the preview showed is the squad the manager has");

        var after = await client.GetAsync($"/api/v1/clubs/{clubId}/takeover-preview");

        after.StatusCode.Should().Be(HttpStatusCode.Conflict, "the club has a manager now");
        (await CodeAsync(after)).Should().Be(WorldErrorCodes.ClubAlreadyClaimed);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_claim_with_no_start_keeps_the_club_as_it_is()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await CreateProfileAsync(client);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var preview = await client.GetFromJsonAsync<TakeoverPreviewResponse>($"/api/v1/clubs/{clubId}/takeover-preview");
        var claim = await ClaimAsync(client, clubId, start: null);

        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        contracts!.SeasonWageTotalMinor.Should().Be(preview!.Keep.SeasonWageMinor);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    private static async Task CreateProfileAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    /// <summary>Finds the first club in the world that still has no manager.</summary>
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

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid clubId, string? start)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId, start }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        return client.SendAsync(request);
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
