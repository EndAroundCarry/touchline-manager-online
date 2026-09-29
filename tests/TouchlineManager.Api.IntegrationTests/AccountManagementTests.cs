using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The account's own management surface (master plan §12.4, `F-07`): the session list and its revoke, the
/// account-data export, and the manager's formatting preferences.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountManagementTests
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AccountManagementTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_session_list_marks_the_session_that_asked()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        // A second sign-in is a second device: a new family with its own session.
        var secondDevice = await SignInAgainAsync(client, manager);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/sessions")
            .WithRefreshCookie(manager.RefreshToken);

        var response = await client.SendAsync(list);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessions = await response.Content.ReadFromJsonAsync<SessionsResponse>();
        sessions!.Sessions.Should().HaveCount(2);
        sessions.Sessions.Count(session => session.IsCurrent).Should().Be(1);
        sessions.Sessions.Should().Contain(session => session.Id == secondDevice.SessionId);

        // The listed sessions carry no token or client-fingerprint material.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(manager.RefreshToken);
        body.Should().NotContain("tokenHash");
        body.Should().NotContain("ipPrefixHash");
    }

    [Fact]
    public async Task A_manager_can_revoke_another_session_and_it_stops_refreshing()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        var secondDevice = await SignInAgainAsync(client, manager);

        using var revoke = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/auth/sessions/{secondDevice.SessionId}")
            .WithRefreshCookie(manager.RefreshToken);

        (await client.SendAsync(revoke)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(secondDevice.RefreshToken);

        (await client.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_session_making_the_request_cannot_be_revoked_from_the_list()
    {
        // Ending the current session is what sign-out is for; revoking it here would leave the manager
        // holding a live access token while the screen said the session was gone.
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/sessions")
            .WithRefreshCookie(manager.RefreshToken);

        var sessions = await (await client.SendAsync(list)).Content.ReadFromJsonAsync<SessionsResponse>();
        var current = sessions!.Sessions.Single(session => session.IsCurrent);

        using var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/auth/sessions/{current.Id}")
            .WithRefreshCookie(manager.RefreshToken);

        var response = await client.SendAsync(revoke);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AuthErrorCodes.CurrentSessionCannotBeRevoked);
    }

    [Fact]
    public async Task The_list_needs_a_session_and_revoking_an_unknown_one_is_refused()
    {
        using var client = CreateClient();

        (await client.GetAsync("/api/v1/auth/sessions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.DeleteAsync($"/api/v1/auth/sessions/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(AuthErrorCodes.SessionNotFound);
    }

    [Fact]
    public async Task The_export_returns_the_accounts_own_data_without_any_secret()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var created = await client.PostAsJsonAsync("/api/v1/manager-profile", new
        {
            locale = "en-GB",
            timeZone = "Europe/London",
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.GetAsync("/api/v1/me/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain(manager.Email);
        body.Should().Contain("\"account\"");
        body.Should().Contain("\"consents\"");
        body.Should().Contain("\"manager\"");
        body.Should().Contain("\"sessions\"");

        // Nothing class C3/C4 may appear: no password, no tokens, no client-fingerprint hashes.
        body.Should().NotContain(AuthScenario.Password);
        body.Should().NotContain(manager.RefreshToken);
        body.Should().NotContain("passwordHash");
        body.Should().NotContain("tokenHash");
        body.Should().NotContain("securityStamp");
    }

    [Fact]
    public async Task The_export_needs_a_session()
    {
        using var client = CreateClient();

        (await client.GetAsync("/api/v1/me/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_manager_can_change_the_time_zone_conditionally()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var created = await client.PostAsJsonAsync("/api/v1/manager-profile", new
        {
            locale = "en-GB",
            timeZone = "Europe/London",
        });

        var profile = await created.Content.ReadFromJsonAsync<ManagerProfileResponse>();

        // Without a version the change cannot be conditional, so it is refused rather than applied.
        var unconditional = await client.PatchAsJsonAsync("/api/v1/manager-profile", new
        {
            locale = "en-GB",
            timeZone = "Europe/Madrid",
        });

        unconditional.StatusCode.Should().Be((HttpStatusCode)428);

        // A stale version is refused too.
        using var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/manager-profile")
        {
            Content = JsonContent.Create(new { locale = "en-GB", timeZone = "Europe/Madrid" }),
        };
        stale.Headers.TryAddWithoutValidation("If-Match", "\"999\"");

        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        // An unknown zone is a validation failure, not a silent fallback to UTC.
        using var invalid = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/manager-profile")
        {
            Content = JsonContent.Create(new { locale = "en-GB", timeZone = "Mars/Phobos" }),
        };
        invalid.Headers.TryAddWithoutValidation("If-Match", $"\"{profile!.Version}\"");

        (await client.SendAsync(invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The real change succeeds, and the new zone is what the position now reports.
        using var change = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/manager-profile")
        {
            Content = JsonContent.Create(new { locale = "en-GB", timeZone = "Asia/Tokyo" }),
        };
        change.Headers.TryAddWithoutValidation("If-Match", $"\"{profile.Version}\"");

        var changed = await client.SendAsync(change);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await changed.Content.ReadFromJsonAsync<ManagerProfileResponse>();
        updated!.TimeZone.Should().Be("Asia/Tokyo");
        updated.Version.Should().BeGreaterThan(profile.Version);

        var state = await client.GetFromJsonAsync<OnboardingStateResponse>("/api/v1/club-tenure");
        state!.Manager!.TimeZone.Should().Be("Asia/Tokyo");
    }

    [Fact]
    public async Task Changing_preferences_needs_a_manager_profile()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        using var change = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/manager-profile")
        {
            Content = JsonContent.Create(new { locale = "en-GB", timeZone = "Asia/Tokyo" }),
        };
        change.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        var response = await client.SendAsync(change);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(WorldErrorCodes.ManagerProfileRequired);
    }

    /// <summary>
    /// Signs in a second time, which is a second device with its own session, and resolves the session
    /// identity the list assigned it. The access token is left on the client.
    /// </summary>
    private static async Task<(Guid SessionId, string RefreshToken)> SignInAgainAsync(
        HttpClient client,
        RegisteredManager manager)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = manager.Email,
            password = manager.Password,
        });

        login.EnsureSuccessStatusCode();

        var refreshToken = AuthTestHelpers.ReadCookie(login, AuthTestHelpers.RefreshCookieName)!;

        client.WithBearer(manager.AccessToken);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/sessions")
            .WithRefreshCookie(refreshToken);

        using var authorized = await client.SendAsync(list);

        authorized.EnsureSuccessStatusCode();

        var sessions = await authorized.Content.ReadFromJsonAsync<SessionsResponse>();
        var current = sessions!.Sessions.Single(session => session.IsCurrent);

        return (current.Id, refreshToken);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}
