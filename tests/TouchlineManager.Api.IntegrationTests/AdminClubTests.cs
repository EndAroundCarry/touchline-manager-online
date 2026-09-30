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
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator's club repair over HTTP: handing a managed club back to the AI (master plan §10.8, §13,
/// `OCC-6`, `F-46`, `F-47`, ADR-0045).
/// </summary>
/// <remarks>
/// The gate is asserted from every side — anonymous, a plain manager, an operator without a fresh code, and
/// one without an idempotency key — and the command is exercised against the real tenure and audit rows.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AdminClubTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminClubTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the club repair acts on. Idempotent with the other admin tests.</summary>
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
    public async Task Assigning_a_club_to_the_AI_is_not_reachable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await ActionAsync(client, Guid.CreateVersion7(), "anonymous", code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_cannot_assign_a_club_to_the_AI()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await ActionAsync(client, Guid.CreateVersion7(), "not mine", "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Assigning_a_club_to_the_AI_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, Guid.CreateVersion7(), "no code", code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task Assigning_a_club_to_the_AI_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/clubs/{Guid.CreateVersion7()}/assign-ai")
        {
            Content = JsonContent.Create(new { reason = "no key" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_hands_a_managed_club_to_the_AI_and_the_reason_is_audited()
    {
        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);

        var clubId = await ClaimAClubAsync(managerClient);

        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        const string reason = "the manager asked support to step in";

        var response = await ActionAsync(operatorClient, clubId, reason, MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<AdminClubAssignAiResponse>())!;

        body.ClubId.Should().Be(clubId);
        body.ControlStatus.Should().Be("ai");
        body.ManagerId.Should().NotBeNull();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var managerId = await db.Managers
            .Where(candidate => candidate.UserId == manager.UserId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var tenure = await db.ClubTenures.SingleAsync(candidate => candidate.ManagerId == managerId);

        tenure.ControlStatus.Should().Be(ClubTenureControlStatus.Closed);
        tenure.EndReason.Should().Be(ClubTenureEndReasons.AdministratorClosed);

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == tenure.Id).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == WorldAuditActions.ClubAssignedToAi && entry.Reason == reason);
    }

    [Fact]
    public async Task Assigning_a_club_that_already_has_no_manager_is_a_conflict()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var clubId = await AFreeClubAsync(client);

        var response = await ActionAsync(client, clubId, "already AI", MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.ClubAlreadyAi);
    }

    [Fact]
    public async Task Assigning_an_unknown_club_is_not_found()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(
            client,
            Guid.CreateVersion7(),
            "no such club",
            MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.ClubNotFound);
    }

    private static Task<HttpResponseMessage> ActionAsync(
        HttpClient client,
        Guid clubId,
        string reason,
        string? code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/clubs/{clubId}/assign-ai")
        {
            Content = JsonContent.Create(new { reason }),
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

        var clubId = await AFreeClubAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
    }

    /// <summary>Finds the first club in the world that still has no manager.</summary>
    private static async Task<Guid> AFreeClubAsync(HttpClient client)
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
}
