using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Finance;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The finance reads over HTTP, against the real composition root (master plan §10.7, §15.4).
/// </summary>
/// <remarks>
/// This is the interface-level half of F-23: a manager reads their club's money, its weekly commitments, and
/// the ledger they are the running total of — while a manager with no club is refused by name and a cursor
/// that does not decode is a client error rather than a missing resource.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class FinanceTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public FinanceTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_manager_reads_their_finance_summary()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var summary = await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary");

        summary.Should().NotBeNull();
        summary!.CashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1), "FIN-1 funds the club at generation");
        summary.ReservedMinor.Should().Be(0);
        summary.AvailableMinor.Should().Be(summary.CashMinor - summary.ReservedMinor, "FIN-10");

        summary.ContractedPlayers.Should().Be(WorldRuleSet.GeneratorSquadTarget, "SQ-1");
        summary.WeeklyWageMinor.Should().BePositive("FIN-7 charges a real payroll");

        summary.TierNumber.Should().Be(1, "the seeded world's lowest tier is the top one");
        summary.WeeklySponsorshipMinor.Should().Be(WorldRuleSet.WeeklySponsorshipMinorForTier(1), "FIN-4");
        summary.WeeklyOperatingCostMinor.Should().Be(WorldRuleSet.WeeklyOperatingCostMinorForTier(1), "FIN-9");

        // The payroll the summary reports is the one the contracts read reports, because they are one fact.
        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        summary.WeeklyWageMinor.Should().Be(contracts!.WeeklyWageTotalMinor);
        summary.ContractedPlayers.Should().Be(contracts.Contracts.Count);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task The_ledger_starts_with_the_opening_balance_and_walks_by_cursor()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var page = await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger");

        page.Should().NotBeNull();
        page!.Entries.Should().NotBeEmpty("a funded club has at least its opening entry (FIN-1)");

        // Newest first, so the walk never repeats a row.
        page.Entries.Select(entry => entry.Sequence).Should().BeInDescendingOrder();
        page.Entries.Should().OnlyContain(entry => entry.Description.Length > 0);
        page.Entries.Should().OnlyContain(entry => entry.Category.Length > 0);

        var opening = page.Entries.Single(entry => entry.Sequence == 1);

        opening.Category.Should().Be(LedgerCategories.ToCode(LedgerCategory.OpeningBalance));
        opening.CashDeltaMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));
        opening.ResultingCashMinor.Should().Be(WorldRuleSet.OpeningCashMinorForTier(1));

        // A cursor asks for what comes before it: below sequence one there is nothing left.
        var older = await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger?cursor=1");

        older!.Entries.Should().BeEmpty();
        older.NextCursor.Should().BeNull();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_cursor_that_does_not_decode_is_rejected()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await client.GetAsync("/api/v1/finances/ledger?cursor=not-a-number");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(FinanceErrorCodes.InvalidCursor);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
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

        var summary = await client.GetAsync("/api/v1/finances/summary");
        var ledger = await client.GetAsync("/api/v1/finances/ledger");

        summary.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(summary)).Should().Be(SquadErrorCodes.NoClub);
        ledger.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(ledger)).Should().Be(SquadErrorCodes.NoClub);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
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

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid clubId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return client.SendAsync(request);
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
