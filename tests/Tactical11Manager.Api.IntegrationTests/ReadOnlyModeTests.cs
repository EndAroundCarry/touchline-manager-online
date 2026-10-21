using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Contracts.Comms;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Infrastructure.Persistence;

namespace Tactical11Manager.Api.IntegrationTests;

/// <summary>
/// The read-only incident gate over HTTP: while the flag is on, manager commands are refused and reads,
/// sign-in, and the operator console keep working (master plan §13, `F-51`, ADR-0047).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReadOnlyModeTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public ReadOnlyModeTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Clears the flag, so the shared host never starts a test read-only.</summary>
    public Task InitializeAsync() => ClearAsync();

    /// <summary>Clears the flag, so the read-only mode cannot leak into the other suites.</summary>
    public Task DisposeAsync() => ClearAsync();

    [Fact]
    public async Task A_manager_command_is_refused_while_read_only_but_reads_and_the_console_work()
    {
        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        var enabled = await SetFlagAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: true, Reason);
        enabled.StatusCode.Should().Be(HttpStatusCode.Created);

        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);

        // A read still works, and it carries the state so the shell can disable its commands.
        var read = await managerClient.GetAsync("/api/v1/sync");
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await read.Content.ReadFromJsonAsync<SyncResponse>())!;
        summary.ReadOnly.Should().BeTrue();
        summary.ReadOnlyMessage.Should().Be(Reason);

        // A manager command is refused with the stable code.
        var command = await managerClient.PostAsync("/api/v1/inbox/read-all", content: null);

        command.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await MfaScenario.ErrorCodeAsync(command)).Should().Be(ApiErrorCodes.ReadOnlyMode);

        // The operator console is exempt: another admin mutation still succeeds while the game is read-only.
        var second = await SetFlagAsync(
            operatorClient,
            MfaScenario.Code(op.Secret),
            enabled: true,
            "still allowed",
            key: $"test.flag.{Guid.NewGuid():N}");

        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_command_is_allowed_again_once_the_flag_is_cleared()
    {
        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        await SetFlagAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: true, Reason);

        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);

        (await managerClient.PostAsync("/api/v1/inbox/read-all", content: null))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var cleared = await SetFlagAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: false, "resumed");
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);

        // The command reaches its handler instead of the gate.
        (await managerClient.PostAsync("/api/v1/inbox/read-all", content: null))
            .StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Direct_message_commands_are_refused_while_read_only_but_reading_them_still_works()
    {
        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        (await SetFlagAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: true, Reason))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);

        var id = Guid.CreateVersion7();

        var start = await managerClient.PostAsJsonAsync(
            "/api/v1/conversations",
            new StartConversationRequest(id, "Hello", "Hello"));
        var reply = await managerClient.PostAsJsonAsync(
            $"/api/v1/conversations/{id}/messages",
            new SendDirectMessageRequest("Hello"));
        var read = await managerClient.PostAsync($"/api/v1/conversations/{id}/read", content: null);
        var block = await managerClient.PutAsync($"/api/v1/blocked-managers/{id}", content: null);

        foreach (var response in new[] { start, reply, read, block })
        {
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "COM-10 commands join the read-only gate (F-51)");
            (await MfaScenario.ErrorCodeAsync(response)).Should().Be(ApiErrorCodes.ReadOnlyMode);
        }

        // Reading the list is not a command; this account has no profile, so it is refused by name, not gated.
        (await managerClient.GetAsync("/api/v1/conversations")).StatusCode
            .Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_structure_upgrade_is_refused_while_read_only_but_the_buildings_can_still_be_read()
    {
        using var operatorClient = _fixture.Factory.CreateClient();
        var op = await MfaScenario.CreateOperatorAsync(_fixture, operatorClient);
        operatorClient.WithBearer(op.AccessToken);

        (await SetFlagAsync(operatorClient, MfaScenario.Code(op.Secret), enabled: true, Reason))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        using var managerClient = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, managerClient);
        managerClient.WithBearer(manager.AccessToken);

        var upgrade = await managerClient.PostAsync("/api/v1/structures/snack_bar/upgrade", content: null);

        upgrade.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "an upgrade spends the club's cash (STRC-3, F-51)");
        (await MfaScenario.ErrorCodeAsync(upgrade)).Should().Be(ApiErrorCodes.ReadOnlyMode);

        // Reading is not a command; this account has no profile, so it is refused by name, not gated.
        (await managerClient.GetAsync("/api/v1/structures")).StatusCode
            .Should().NotBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task An_anonymous_command_is_refused_by_authentication_first()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsync("/api/v1/inbox/read-all", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_manager_cannot_flip_the_incident_flag()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await SetFlagAsync(client, code: "000000", enabled: true, "not permitted");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private const string Reason = "Read-only while we repair the ledger.";

    private static Task<HttpResponseMessage> SetFlagAsync(
        HttpClient client,
        string code,
        bool enabled,
        string reason,
        string? key = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/feature-flags/{key ?? IncidentFlags.ReadOnly}")
        {
            Content = JsonContent.Create(new { value = new { enabled, message = reason }, reason }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());
        request.Headers.TryAddWithoutValidation(MfaScenario.CodeHeader, code);

        return client.SendAsync(request);
    }

    private async Task ClearAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tactical11ManagerDbContext>();
        var readOnly = scope.ServiceProvider.GetRequiredService<IReadOnlyMode>();

        var existing = await db.FeatureFlags.SingleOrDefaultAsync(
            flag => flag.Scope == IncidentFlags.WorldScope && flag.Key == IncidentFlags.ReadOnly);

        if (existing is not null)
        {
            db.FeatureFlags.Remove(existing);
            await db.SaveChangesAsync();
        }

        // The host caches the flag for a few seconds; drop it so the next test starts from the database.
        readOnly.Invalidate();
    }
}
