using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Domain.World;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator surface: who may read it, who may act on it, and the audit trail it leaves
/// (master plan §10.8, §13, `F-46`, `F-47`, ADR-0042).
/// </summary>
/// <remarks>
/// The gate is asserted from every side: anonymous, a plain manager, an operator that never completed a
/// second factor, an operator that did but sent no fresh code, and finally an operator that did both.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AdminEndpointsTests : IAsyncLifetime
{
    private const string HealthPath = "/api/v1/admin/health/game";

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminEndpointsTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the health read reports on. Idempotent.</summary>
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
    public async Task The_admin_surface_is_not_readable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync(HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_is_forbidden()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync(HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_operator_without_a_second_factor_is_forbidden()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        await MfaScenario.GrantRoleAsync(_fixture, manager.UserId, UserRoles.Operator);

        // Sign in again so the role is stamped into the token. The account has no authenticator, so there
        // is no challenge: it gets a session, but one without the second factor.
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = manager.Email, password = manager.Password });

        login.EnsureSuccessStatusCode();

        var session = (await login.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
        client.WithBearer(session.AccessToken);

        MfaScenario.ReadClaim(session.AccessToken, "mfa").Should().Be("false");

        var response = await client.GetAsync(HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_operator_with_a_second_factor_reads_the_game_health()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await client.GetAsync(HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("it is live operator data");

        var health = (await response.Content.ReadFromJsonAsync<AdminGameHealthResponse>())!;

        health.WorldId.Should().NotBeEmpty();
        health.WorldStatus.Should().Be(GameWorldStatuses.ActiveCode);
        health.CurrentSeasonNumber.Should().BeGreaterThan(0);
        health.PendingJobs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task A_mutation_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        var target = await CreateTargetAsync();

        client.WithBearer(op.AccessToken);

        var response = await MutateAsync(client, target.UserId, "suspend", "testing the gate", code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task A_mutation_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        var target = await CreateTargetAsync();

        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{target.UserId}/suspend")
        {
            Content = JsonContent.Create(new { reason = "testing the gate" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_suspends_and_restores_an_account_and_the_reason_is_audited()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);

        using var targetClient = _fixture.Factory.CreateClient();
        var target = await AuthScenario.CreateVerifiedManagerAsync(_fixture, targetClient);
        targetClient.WithBearer(target.AccessToken);

        client.WithBearer(op.AccessToken);

        var suspension = await MutateAsync(
            client,
            target.UserId,
            "suspend",
            "abusive conduct",
            MfaScenario.Code(op.Secret));

        suspension.StatusCode.Should().Be(HttpStatusCode.OK);

        var suspensionBody = (await suspension.Content.ReadFromJsonAsync<AccountStatusResponse>())!;
        suspensionBody.Status.Should().Be(UserStatuses.SuspendedCode);

        // A suspended account loses its access immediately, despite its token not having expired.
        var revoked = await targetClient.GetAsync("/api/v1/me");
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var suspended = await db.Users.AsNoTracking().SingleAsync(user => user.Id == target.UserId);
            suspended.Status.Should().Be(UserStatus.Suspended);

            var audit = await db.AuditEntries
                .AsNoTracking()
                .Where(entry => entry.TargetId == target.UserId)
                .ToListAsync();

            audit.Should().Contain(entry =>
                entry.Action == AdminAuditActions.AccountSuspended && entry.Reason == "abusive conduct");
        }

        var restoration = await MutateAsync(
            client,
            target.UserId,
            "restore",
            "appeal upheld",
            MfaScenario.Code(op.Secret));

        restoration.StatusCode.Should().Be(HttpStatusCode.OK);
        (await restoration.Content.ReadFromJsonAsync<AccountStatusResponse>())!
            .Status.Should().Be(UserStatuses.ActiveCode);
    }

    private async Task<RegisteredManager> CreateTargetAsync()
    {
        using var targetClient = _fixture.Factory.CreateClient();

        return await AuthScenario.CreateVerifiedManagerAsync(_fixture, targetClient);
    }

    private static Task<HttpResponseMessage> MutateAsync(
        HttpClient client,
        Guid userId,
        string verb,
        string reason,
        string? code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/users/{userId}/{verb}")
        {
            Content = JsonContent.Create(new { reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());

        if (code is not null)
        {
            request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);
        }

        return client.SendAsync(request);
    }
}
