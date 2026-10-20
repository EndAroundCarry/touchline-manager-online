using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Contracts.Finance;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Market;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// Releasing a player over HTTP, against the real composition root (`CON-12`, `FA-3`, `CONC-1`).
/// </summary>
/// <remarks>
/// A release is permanent, so these tests claim the last club the world offers rather than the first: the
/// other suites read and renew the first club's squad and would otherwise find players missing.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ContractReleaseTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ContractReleaseTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these tests release in. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_quote_prices_half_the_wages_left_and_says_when_the_player_joins_the_pool()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var quote = await QuoteAsync(client, contract.Id);

        quote.PlayerId.Should().Be(contract.PlayerId);
        quote.WeeklyWageMinor.Should().Be(contract.WeeklyWageMinor);
        quote.RemainingWeeks.Should().BePositive();
        quote.PayoutMinor.Should().Be(
            quote.WeeklyWageMinor * quote.RemainingWeeks / 2,
            "the payout is half the wages left (CON-12)");
        quote.BlockedReason.Should().BeNull();
        quote.AvailableMinor.Should().BePositive();

        quote.JoinsPoolAt.Should().BeAfter(quote.ServerTime);
        quote.JoinsPoolAt.UtcDateTime.DayOfWeek.Should().Be(DayOfWeek.Monday, "the pool opens on a Monday (FA-3)");
        quote.JoinsPoolAt.UtcDateTime.TimeOfDay.Should().Be(TimeSpan.FromHours(12));

        var second = await QuoteAsync(client, contract.Id);

        second.PayoutMinor.Should().Be(quote.PayoutMinor, "asking twice offers the same price");
        second.ContractVersion.Should().Be(quote.ContractVersion);

        await ResignAsync(client);
    }

    [Fact]
    public async Task Releasing_a_player_pays_closes_the_contract_and_sends_him_to_the_pool()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var quote = await QuoteAsync(client, contract.Id);
        var summaryBefore = (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!;

        var response = await ReleaseAsync(client, contract.Id, quote.ContractVersion);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var released = (await response.Content.ReadFromJsonAsync<ReleasePlayerResponse>())!;

        released.ContractId.Should().Be(contract.Id);
        released.PlayerId.Should().Be(contract.PlayerId);
        released.PayoutMinor.Should().Be(quote.PayoutMinor, "the amount confirmed is the amount paid");
        released.RemainingWeeks.Should().Be(quote.RemainingWeeks);
        released.WeeklyWageSavedMinor.Should().Be(contract.WeeklyWageMinor);
        released.JoinsPoolAt.Should().Be(quote.JoinsPoolAt);

        // The club is poorer by the payout, owes the player's wage no more, and has one player fewer.
        var summary = (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!;

        summary.CashMinor.Should().Be(summaryBefore.CashMinor - quote.PayoutMinor);
        summary.WeeklyWageMinor.Should().Be(summaryBefore.WeeklyWageMinor - contract.WeeklyWageMinor);
        summary.ContractedPlayers.Should().Be(summaryBefore.ContractedPlayers - 1);

        var ledger = (await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger"))!;
        var entry = ledger.Entries[0];

        entry.Category.Should().Be("release_payout");
        entry.CashDeltaMinor.Should().Be(-quote.PayoutMinor);
        entry.ReservedDeltaMinor.Should().Be(0);
        entry.Description.Should().Be("Player release payout");

        var contracts = (await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts"))!;

        contracts.Contracts.Should().NotContain(row => row.Id == contract.Id);

        // The records: the contract closed as released, the registration ended, the player is a free agent.
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();

            var closed = await db.PlayerContracts.SingleAsync(row => row.Id == contract.Id);

            closed.Status.Should().Be(ContractStatus.Closed);
            closed.ClosedReason.Should().Be(PlayerContractCloseReasons.Released);

            (await db.PlayerRegistrations.CountAsync(row =>
                row.PlayerId == contract.PlayerId && row.Status == RegistrationStatus.Active)).Should().Be(0);
            (await db.Players.SingleAsync(row => row.Id == contract.PlayerId)).Status
                .Should().Be(PlayerStatus.FreeAgent);
        }

        // The manager is told.
        var inbox = (await client.GetFromJsonAsync<InboxResponse>("/api/v1/inbox"))!;
        var note = inbox.Messages.Single(message => message.TemplateKey == "inbox.squad.player_released");

        note.RelatedEntityId.Should().Be(contract.PlayerId);
        note.Title.Should().EndWith("was released");

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_retried_release_pays_nothing_more()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var quote = await QuoteAsync(client, contract.Id);

        (await ReleaseAsync(client, contract.Id, quote.ContractVersion)).StatusCode.Should().Be(HttpStatusCode.OK);

        var cashAfterFirst = (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!
            .CashMinor;
        var entriesAfterFirst = (await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger"))!
            .Entries.Count;

        // The same request again, still holding the version it read: a double-click or a retry.
        var second = await ReleaseAsync(client, contract.Id, quote.ContractVersion);

        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(second)).Should().Be(SquadErrorCodes.ContractNotFound);

        (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!.CashMinor
            .Should().Be(cashAfterFirst, "the retry must not charge again");
        (await client.GetFromJsonAsync<FinanceLedgerResponse>("/api/v1/finances/ledger"))!.Entries
            .Should().HaveCount(entriesAfterFirst);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_release_without_a_version_is_refused()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var response = await ReleaseAsync(client, contract.Id, version: null);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionRequired);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_nothing_is_paid()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var quote = await QuoteAsync(client, contract.Id);
        var cash = (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!.CashMinor;

        var response = await ReleaseAsync(client, contract.Id, quote.ContractVersion + 999);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionFailed);
        (await client.GetFromJsonAsync<FinanceSummaryResponse>("/api/v1/finances/summary"))!.CashMinor
            .Should().Be(cash);

        await ResignAsync(client);
    }

    [Fact]
    public async Task A_listed_player_cannot_be_released_until_the_listing_is_withdrawn()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        var contract = await ReleasableAsync(client);
        var key = Guid.CreateVersion7().ToString();
        var listing = await PostWithKeyAsync(
            client,
            "/api/v1/transfers/listings",
            new CreateListingRequest(contract.PlayerId, MinimumFeeMinor: 5_000_000, Seasons: 2),
            key);

        listing.StatusCode.Should().Be(HttpStatusCode.Created);

        var listingId = (await listing.Content.ReadFromJsonAsync<TransferListingResponse>())!.ListingId;
        var quote = await QuoteAsync(client, contract.Id);

        quote.BlockedReason.Should().Be(SquadErrorCodes.ReleasePlayerListed);

        var refused = await ReleaseAsync(client, contract.Id, quote.ContractVersion);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).Should().Be(SquadErrorCodes.ReleasePlayerListed);

        var withdrawn = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/transfers/listings/{listingId}");

        withdrawn.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());
        (await client.SendAsync(withdrawn)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await QuoteAsync(client, contract.Id)).BlockedReason.Should().BeNull();

        await ResignAsync(client);
    }

    [Fact]
    public async Task Another_clubs_contract_and_an_unknown_one_are_the_same_answer()
    {
        using var client = CreateClient();
        await SignInAndOnboardAsync(client);

        Guid otherClubsContract;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
            var mine = (await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts"))!.ClubId;

            otherClubsContract = await db.PlayerContracts
                .Where(row => row.Status == ContractStatus.Active && row.ClubId != mine)
                .Select(row => row.Id)
                .FirstAsync();
        }

        foreach (var contractId in new[] { otherClubsContract, Guid.CreateVersion7() })
        {
            var quote = await client.GetAsync($"/api/v1/contracts/{contractId}/release-quote");
            var release = await ReleaseAsync(client, contractId, version: 1);

            quote.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CodeAsync(quote)).Should().Be(SquadErrorCodes.ContractNotFound);
            release.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CodeAsync(release)).Should().Be(SquadErrorCodes.ContractNotFound);
        }

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

        var quote = await client.GetAsync($"/api/v1/contracts/{Guid.CreateVersion7()}/release-quote");
        var release = await ReleaseAsync(client, Guid.CreateVersion7(), version: 1);

        quote.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(quote)).Should().Be(SquadErrorCodes.NoClub);
        release.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(release)).Should().Be(SquadErrorCodes.NoClub);
    }

    /// <summary>An outfield player on a long contract, so the club stays legal and the payout is real.</summary>
    private static async Task<PlayerContractResponse> ReleasableAsync(HttpClient client)
    {
        var contracts = (await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts"))!;

        return contracts.Contracts
            .Where(row => row.PrimaryPosition != PlayerPositions.GoalkeeperCode)
            .OrderByDescending(row => row.EndSeasonNumber)
            .ThenBy(row => row.Id)
            .First();
    }

    private static async Task<ReleaseQuoteResponse> QuoteAsync(HttpClient client, Guid contractId)
    {
        var response = await client.GetAsync($"/api/v1/contracts/{contractId}/release-quote");

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ReleaseQuoteResponse>())!;
    }

    private static Task<HttpResponseMessage> ReleaseAsync(HttpClient client, Guid contractId, long? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/contracts/{contractId}/release");

        if (version is { } value)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{value}\"");
        }

        return client.SendAsync(request);
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

        var clubId = await LastAvailableClubAsync(client);
        var claim = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        claim.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        (await client.SendAsync(claim)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<Guid> LastAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!.Reverse())
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.LastOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return available.Id;
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
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
