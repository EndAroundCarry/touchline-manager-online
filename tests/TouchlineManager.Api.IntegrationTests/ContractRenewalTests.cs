using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The renewal flow over HTTP, against the real composition root (master plan §10.3; `CON-3`, `CON-4`,
/// `CONC-1`).
/// </summary>
/// <remarks>
/// This is the interface-level half of F-22 and F-23: a manager asks for a quote, signs it, and the old
/// contract gives way to a new one — while a stale version, a missing precondition, an illegal term, and an
/// unknown contract are each refused by name.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class ContractRenewalTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ContractRenewalTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world these tests renew in. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_manager_quotes_and_signs_a_renewal()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");
        var contract = contracts!.Contracts.OrderBy(row => row.EndSeasonNumber).First();

        var quote = await QuoteAsync(client, contract.Id, seasons: 3);

        quote.WeeklyWageMinor.Should().BePositive("CON-3 offers a real wage");
        quote.Seasons.Should().Be(3);
        quote.StartSeasonNumber.Should().Be(contracts.SeasonNumber);
        quote.EndSeasonNumber.Should().Be(contracts.SeasonNumber + 2);

        // The quote is deterministic: asking twice, without changing anything, offers the same terms.
        var second = await QuoteAsync(client, contract.Id, seasons: 3);

        second.WeeklyWageMinor.Should().Be(quote.WeeklyWageMinor);
        second.ContractVersion.Should().Be(quote.ContractVersion);

        var renewed = await RenewAsync(client, contract.Id, seasons: 3, quote.ContractVersion);

        renewed.WeeklyWageMinor.Should().Be(quote.WeeklyWageMinor, "the accepted wage is the quoted one");
        renewed.StartSeasonNumber.Should().Be(contracts.SeasonNumber);
        renewed.EndSeasonNumber.Should().Be(contracts.SeasonNumber + 2);

        // The old contract is closed and the new one is the player's only active deal (SQ-6).
        var after = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        after!.Contracts.Should().Contain(row => row.Id == renewed.ContractId);
        after.Contracts.Should().NotContain(row => row.Id == contract.Id);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_stale_version_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");
        var contract = contracts!.Contracts[0];
        var quote = await QuoteAsync(client, contract.Id, seasons: 2);

        using var request = RenewRequest(contract.Id, seasons: 2, quote.ContractVersion + 999);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionFailed);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_renewal_without_a_version_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        using var request = RenewRequest(contracts!.Contracts[0].Id, seasons: 2, version: null);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_illegal_term_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/contracts/{contracts!.Contracts[0].Id}/renewal-quote",
            new { seasons = 4 });

        // The field-level validator catches it before the use case, so the code is the shared validation one
        // rather than the module's own (CON-1 is a field-level rule).
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(ApiErrorCodes.ValidationFailed);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_unknown_contract_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/contracts/{Guid.CreateVersion7()}/renewal-quote",
            new { seasons = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.ContractNotFound);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    private static async Task<RenewalQuoteResponse> QuoteAsync(HttpClient client, Guid contractId, int seasons)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/contracts/{contractId}/renewal-quote",
            new { seasons });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RenewalQuoteResponse>())!;
    }

    private static async Task<ContractRenewalResponse> RenewAsync(
        HttpClient client,
        Guid contractId,
        int seasons,
        long version)
    {
        using var request = RenewRequest(contractId, seasons, version);
        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ContractRenewalResponse>())!;
    }

    private static HttpRequestMessage RenewRequest(Guid contractId, int seasons, long? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/contracts/{contractId}/renew")
        {
            Content = JsonContent.Create(new { seasons }),
        };

        if (version is { } value)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{value}\"");
        }

        return request;
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

        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is null)
            {
                continue;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
            {
                Content = JsonContent.Create(new { clubId = available.Id }),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

            var claim = await client.SendAsync(request);

            claim.StatusCode.Should().Be(HttpStatusCode.Created);

            return available.Id;
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
