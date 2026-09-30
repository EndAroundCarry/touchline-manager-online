using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Finance;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator's finance repair over HTTP: an append-only compensating entry that names the line it
/// corrects (master plan §10.8, §13, `FIN-12`, `F-46`, `F-47`, ADR-0045).
/// </summary>
/// <remarks>
/// The result is asserted against the real ledger and the real audit trail, and the club under repair is one
/// this test claimed itself so the shared seeded world is not disturbed for the other suites.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AdminFinanceRepairTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminFinanceRepairTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the finance repair acts on. Idempotent with the other admin tests.</summary>
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
    public async Task The_finance_repair_is_not_reachable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await ActionAsync(
            client,
            new { clubId = Guid.CreateVersion7(), cashDeltaMinor = -100, reason = "anonymous" },
            code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_cannot_post_a_compensating_entry()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await ActionAsync(
            client,
            new { clubId = Guid.CreateVersion7(), cashDeltaMinor = -100, reason = "not mine" },
            "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_finance_repair_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            client,
            new { clubId = Guid.CreateVersion7(), cashDeltaMinor = -100, reason = "no code" },
            code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task The_finance_repair_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/finance/compensating-entry")
        {
            Content = JsonContent.Create(new { clubId = Guid.CreateVersion7(), cashDeltaMinor = -100, reason = "no key" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_posts_a_compensating_entry_that_names_the_line_it_corrects()
    {
        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);
        var clubId = await ClaimAClubAsync(managerClient);

        var (opening, cashBefore) = await OpeningAsync(clubId);

        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        const string reason = "the gate was double counted";

        var response = await ActionAsync(
            operatorClient,
            new { clubId, cashDeltaMinor = -250, reversesEntryId = opening, reason },
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = (await response.Content.ReadFromJsonAsync<AdminCompensationResponse>())!;

        body.ClubId.Should().Be(clubId);
        body.CashDeltaMinor.Should().Be(-250);
        body.ResultingCashMinor.Should().Be(cashBefore - 250);
        body.ReversesEntryId.Should().Be(opening);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var entry = await db.LedgerEntries.AsNoTracking().SingleAsync(candidate => candidate.Id == body.EntryId);

        entry.Category.Should().Be(LedgerCategory.Compensation);
        entry.SourceType.Should().Be(LedgerSourceType.AdminRepair);
        entry.ReversesEntryId.Should().Be(opening);

        (await db.ClubAccounts.AsNoTracking().SingleAsync(account => account.ClubId == clubId))
            .CashMinor.Should().Be(cashBefore - 250, "the ledger is the balance (FIN-12, FIN-18)");

        (await db.AuditEntries.AsNoTracking().Where(row => row.TargetId == entry.Id).ToListAsync())
            .Should()
            .Contain(row => row.Action == FinanceAuditActions.CompensatingEntry && row.Reason == reason);
    }

    [Fact]
    public async Task A_correction_that_would_overdraw_is_a_conflict()
    {
        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);
        var clubId = await ClaimAClubAsync(managerClient);

        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            operatorClient,
            new { clubId, cashDeltaMinor = -long.MaxValue, reason = "too much" },
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.CompensationNotAffordable);
    }

    [Fact]
    public async Task A_correction_of_an_unknown_club_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            client,
            new { clubId = Guid.CreateVersion7(), cashDeltaMinor = -100, reason = "no such club" },
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.ClubNotFound);
    }

    private static Task<HttpResponseMessage> ActionAsync(HttpClient client, object body, string? code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/finance/compensating-entry")
        {
            Content = JsonContent.Create(body),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        if (code is not null)
        {
            request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);
        }

        return client.SendAsync(request);
    }

    /// <summary>Creates a profile, claims the first free club, and returns its identity.</summary>
    private static async Task<Guid> ClaimAClubAsync(HttpClient client)
    {
        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");
        Guid clubId = Guid.Empty;

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                clubId = available.Id;
                break;
            }
        }

        clubId.Should().NotBe(Guid.Empty, "the seeded world has a free club");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
    }

    private async Task<(Guid Opening, long CashMinor)> OpeningAsync(Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var opening = await db.LedgerEntries
            .Where(entry => entry.ClubId == clubId && entry.Category == LedgerCategory.OpeningBalance)
            .Select(entry => entry.Id)
            .SingleAsync();

        var cash = await db.ClubAccounts
            .Where(account => account.ClubId == clubId)
            .Select(account => account.CashMinor)
            .SingleAsync();

        return (opening, cash);
    }
}
