using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The end-to-end authentication lifecycle against the real composition root (master plan §15.4):
/// register, verify, sign in, refresh, edit, sign out, and reset.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthLifecycleTests
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AuthLifecycleTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_manager_can_register_verify_sign_in_edit_refresh_and_sign_out()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        // The profile is readable, and carries the version that guards the next write.
        var me = await client.GetAsync("/api/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        me.Headers.ETag.Should().NotBeNull();

        var profile = await me.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile!.Email.Should().Be(manager.Email);
        profile.EmailVerified.Should().BeTrue();
        profile.Status.Should().Be("active");
        profile.Roles.Should().Contain("player");

        // A conditional write succeeds and returns the next version.
        var renamed = $"Mgr{Guid.NewGuid():N}"[..13];

        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/me")
        {
            Content = JsonContent.Create(new { displayName = renamed }),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", me.Headers.ETag!.Tag);

        var patched = await client.SendAsync(patch);
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await patched.Content.ReadFromJsonAsync<ProfileUpdatedResponse>();
        updated!.User.DisplayName.Should().Be(renamed);
        updated.Version.Should().BeGreaterThan(1);

        // Refreshing rotates the cookie rather than reusing the token.
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(manager.RefreshToken);

        var refreshed = await client.SendAsync(refresh);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = AuthTestHelpers.ReadCookie(refreshed, AuthTestHelpers.RefreshCookieName);
        rotated.Should().NotBeNullOrWhiteSpace().And.NotBe(manager.RefreshToken);

        // Signing out makes the rotated token unusable.
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            .WithRefreshCookie(rotated!);

        (await client.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var afterLogout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(rotated!);

        (await client.SendAsync(afterLogout)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Registration_emails_a_link_and_leaves_the_account_unverified_until_it_is_used()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"new-{Guid.NewGuid():N}@example.com";
        var registration = await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var message = _fixture.Email.Messages.Single(captured => captured.To == email);
        message.Subject.Should().Contain("Confirm");
        AuthTestHelpers.ExtractToken(message.TextBody).Should().NotBeNullOrWhiteSpace();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthScenario.Password,
        });

        // An unverified account may hold a session; it simply may not write yet.
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await login.Content.ReadFromJsonAsync<AuthSessionResponse>();
        session!.User.EmailVerified.Should().BeFalse();
        session.User.Status.Should().Be("pending");
    }

    [Fact]
    public async Task A_verification_link_works_exactly_once()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"once-{Guid.NewGuid():N}@example.com";
        var registration = await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var token = AuthTestHelpers.ExtractToken(_fixture.Email.Messages.Single(m => m.To == email).TextBody);

        var first = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { userId = registration.UserId, token });

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { userId = registration.UserId, token });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync()).Should().Contain(AuthErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task Requesting_a_fresh_link_invalidates_the_previous_one()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"resend-{Guid.NewGuid():N}@example.com";
        var registration = await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var firstToken = AuthTestHelpers.ExtractToken(_fixture.Email.Messages.Single(m => m.To == email).TextBody);

        var resend = await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email });
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var allMessages = _fixture.Email.Messages.Where(m => m.To == email).ToList();
        allMessages.Should().HaveCount(2);

        var secondToken = AuthTestHelpers.ExtractToken(allMessages[1].TextBody);
        secondToken.Should().NotBe(firstToken);

        // Only the newest link is valid (ADR-0002).
        var stale = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { userId = registration.UserId, token = firstToken });

        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var fresh = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { userId = registration.UserId, token = secondToken });

        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resend_and_forgot_password_answer_identically_for_unknown_addresses()
    {
        // Neither endpoint may reveal whether an address is registered (ADR-0002).
        using var client = CreateClient();
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";

        var resend = await client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email = unknown });
        var forgot = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = unknown });

        resend.StatusCode.Should().Be(HttpStatusCode.Accepted);
        forgot.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_password_reset_changes_the_password_and_signs_every_session_out()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        var forgot = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = manager.Email });
        forgot.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var resetEmail = _fixture.Email.Messages.Last(m => m.To == manager.Email);
        var resetToken = AuthTestHelpers.ExtractToken(resetEmail.TextBody);
        const string NewPassword = "brand-new-horse-battery";

        var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new
        {
            userId = manager.UserId,
            token = resetToken,
            newPassword = NewPassword,
        });

        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        // The session that existed before the reset is dead.
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(manager.RefreshToken);

        (await client.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The old password no longer works and the new one does.
        var oldLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = manager.Email,
            password = AuthScenario.Password,
        });

        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = manager.Email,
            password = NewPassword,
        });

        newLogin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_everywhere_invalidates_an_access_token_that_has_not_expired()
    {
        // Access tokens are stateless, so revocation has to come from the security stamp (ADR-0002).
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logoutAll = await client.PostAsync("/api/v1/auth/logout-all", content: null);
        logoutAll.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}
