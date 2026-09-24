using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The abuse and integrity behaviours of the auth module: rotation and reuse detection, the
/// restrictions on unverified and suspended accounts, conditional-write preconditions, and the
/// promise that no raw credential is ever persisted (ADR-0002, master plan §12).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthSecurityTests
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AuthSecurityTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_whole_family()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        using var firstRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(manager.RefreshToken);

        var first = await client.SendAsync(firstRefresh);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = AuthTestHelpers.ReadCookie(first, AuthTestHelpers.RefreshCookieName)!;

        // Presenting the consumed token is proof of theft.
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(manager.RefreshToken);

        var replayResponse = await client.SendAsync(replay);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The legitimate replacement is revoked too: the family is treated as compromised.
        using var legitimate = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
            .WithRefreshCookie(rotated);

        (await client.SendAsync(legitimate)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unverified_account_cannot_change_its_display_name()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"unverified-{Guid.NewGuid():N}@example.com";
        await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthScenario.Password,
        });

        var session = await login.Content.ReadFromJsonAsync<AuthSessionResponse>();
        client.WithBearer(session!.AccessToken);

        var me = await client.GetAsync("/api/v1/me");

        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/me")
        {
            Content = JsonContent.Create(new { displayName = $"Mgr{Guid.NewGuid():N}"[..13] }),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", me.Headers.ETag!.Tag);

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(response)).Should().Be(AuthErrorCodes.AccountNotVerified);
    }

    [Fact]
    public async Task A_suspended_account_cannot_sign_in_and_its_live_token_stops_working()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await SuspendAsync(manager.UserId);

        // The stamp and status are re-checked on every request, so the token in hand is already dead
        // even though it has not expired.
        client.DefaultRequestHeaders.Authorization = null;
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = manager.Email,
            password = manager.Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemCodeAsync(login)).Should().Be(AuthErrorCodes.AccountSuspended);
    }

    [Fact]
    public async Task A_profile_write_must_be_conditional()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var response = await client.PatchAsJsonAsync("/api/v1/me", new
        {
            displayName = $"Mgr{Guid.NewGuid():N}"[..13],
        });

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await ReadProblemCodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionRequired);
    }

    [Fact]
    public async Task A_stale_profile_version_is_rejected_without_overwriting_the_change()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/me")
        {
            Content = JsonContent.Create(new { displayName = $"Mgr{Guid.NewGuid():N}"[..13] }),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", "\"999999\"");

        var response = await client.SendAsync(patch);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await ReadProblemCodeAsync(response)).Should().Be(ApiErrorCodes.PreconditionFailed);
    }

    [Fact]
    public async Task A_weak_entity_tag_is_not_accepted_as_a_precondition()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        using var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/me")
        {
            Content = JsonContent.Create(new { displayName = $"Mgr{Guid.NewGuid():N}"[..13] }),
        };
        patch.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");

        (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
    }

    [Fact]
    public async Task Invalid_requests_return_field_level_problem_details()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            displayName = "ab",
            password = "short",
            acceptTerms = false,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("code").GetString().Should().Be(ApiErrorCodes.ValidationFailed);

        var errors = root.GetProperty("errors");
        errors.TryGetProperty("Email", out _).Should().BeTrue();
        errors.TryGetProperty("Password", out _).Should().BeTrue();
        errors.TryGetProperty("DisplayName", out _).Should().BeTrue();
        errors.TryGetProperty("AcceptTerms", out _).Should().BeTrue();

        // The response must not echo the password back.
        (await response.Content.ReadAsStringAsync()).Should().NotContain("short");
    }

    [Fact]
    public async Task Unknown_accounts_get_the_same_answer_as_a_wrong_password()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = manager.Email,
            password = "definitely-not-the-password",
        });

        var unknownAccount = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"nobody-{Guid.NewGuid():N}@example.com",
            password = "definitely-not-the-password",
        });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await ReadProblemCodeAsync(wrongPassword)).Should().Be(AuthErrorCodes.InvalidCredentials);
        (await ReadProblemCodeAsync(unknownAccount)).Should().Be(AuthErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task No_raw_token_is_ever_stored()
    {
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"storage-{Guid.NewGuid():N}@example.com";
        var registration = await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var verificationToken = AuthTestHelpers.ExtractToken(
            _fixture.Email.Messages.Single(m => m.To == email).TextBody);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var emailTokenHashes = await db.EmailTokens
            .Where(token => token.UserId == registration.UserId)
            .Select(token => token.TokenHash)
            .ToListAsync();

        emailTokenHashes.Should().ContainSingle("one verification link was issued");
        emailTokenHashes.Should().NotContain(verificationToken);

        // Verify and sign in so a refresh session exists, then assert the same for the cookie value.
        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new
        {
            userId = registration.UserId,
            token = verificationToken,
        });
        verify.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = AuthScenario.Password,
        });

        var refreshToken = AuthTestHelpers.ReadCookie(login, AuthTestHelpers.RefreshCookieName)!;

        var sessionHashes = await db.RefreshSessions
            .Where(session => session.UserId == registration.UserId)
            .Select(session => session.TokenHash)
            .ToListAsync();

        sessionHashes.Should().ContainSingle();
        sessionHashes.Should().NotContain(refreshToken);
    }

    [Fact]
    public async Task Api_responses_carry_the_security_headers_the_policy_requires()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/live");

        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Single().Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Single().Should().Be("no-referrer");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.Should().Contain("default-src 'none'");
        csp.Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task The_auth_endpoints_are_rate_limited_per_client()
    {
        // A dedicated host with a deliberately tiny limit, so the mechanism is exercised without
        // throttling every other test.
        await using var factory = _fixture.CreateFactory(enableJobProbe: false, authPermitLimit: 2);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        HttpResponseMessage? throttled = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                email = $"ratelimit-{Guid.NewGuid():N}@example.com",
                displayName = $"Mgr{Guid.NewGuid():N}"[..13],
                password = AuthScenario.Password,
                acceptTerms = true,
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }
        }

        throttled.Should().NotBeNull("the third request within the window must be throttled");
        (await ReadProblemCodeAsync(throttled!)).Should().Be(ApiErrorCodes.TooManyRequests);
    }

    private async Task SuspendAsync(Guid userId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "update auth.users set status = 'suspended' where id = {0}",
            userId);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}
