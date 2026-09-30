using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Ops;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator's feature-flag switch over HTTP: a world-scoped key with a JSON value (master plan §10.8, §13,
/// `F-46`, `F-47`, ADR-0045).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AdminFeatureFlagTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AdminFeatureFlagTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the flag store belongs to. Idempotent with the other admin tests.</summary>
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
    public async Task Setting_a_feature_flag_is_not_reachable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await ActionAsync(client, Key(), value: true, code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_cannot_set_a_feature_flag()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await ActionAsync(client, Key(), value: true, code: "000000");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Setting_a_feature_flag_without_a_fresh_code_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, Key(), value: true, code: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task Setting_a_feature_flag_without_an_idempotency_key_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/feature-flags/{Key()}")
        {
            Content = JsonContent.Create(new { value = true, reason = "no key" }),
        };
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, MfaScenario.Code(op.Secret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.IdempotencyKeyRequired);
    }

    [Fact]
    public async Task An_operator_creates_then_updates_a_feature_flag_and_the_reason_is_audited()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var key = Key();
        const string reason = "pausing auctions for a data repair";

        var created = await ActionAsync(client, key, value: false, MfaScenario.Code(op.Secret), reason);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdBody = (await created.Content.ReadFromJsonAsync<AdminFeatureFlagResponse>())!;

        createdBody.Key.Should().Be(key);
        createdBody.Scope.Should().Be(SetFeatureFlag.WorldScope);
        createdBody.Value.ValueKind.Should().Be(System.Text.Json.JsonValueKind.False);
        createdBody.Version.Should().Be(1);
        createdBody.Created.Should().BeTrue();

        var updated = await ActionAsync(client, key, value: true, MfaScenario.Code(op.Secret), "resuming auctions");

        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedBody = (await updated.Content.ReadFromJsonAsync<AdminFeatureFlagResponse>())!;

        updatedBody.Version.Should().Be(2);
        updatedBody.Created.Should().BeFalse();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var row = await db.FeatureFlags.AsNoTracking().SingleAsync(flag => flag.Key == key);

        row.ValueJson.Should().Be("true");
        row.Version.Should().Be(2);

        (await db.AuditEntries.AsNoTracking().Where(entry => entry.TargetId == row.Id).ToListAsync())
            .Should()
            .Contain(entry => entry.Action == AdminAuditActions.FeatureFlagSet && entry.Reason == reason);
    }

    [Fact]
    public async Task A_flag_key_this_surface_does_not_accept_is_refused()
    {
        using var client = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, client);
        client.WithBearer(op.AccessToken);

        var response = await ActionAsync(client, "Not-A-Valid-Key", value: true, MfaScenario.Code(op.Secret));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MfaScenario.ErrorCodeAsync(response)).Should().Be(AdminErrorCodes.FeatureFlagInvalid);
    }

    private static string Key() => $"test.flag.{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> ActionAsync(
        HttpClient client,
        string key,
        bool value,
        string? code,
        string reason = "an operator switch")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/feature-flags/{key}")
        {
            Content = JsonContent.Create(new { value, reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

        if (code is not null)
        {
            request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);
        }

        return client.SendAsync(request);
    }
}
