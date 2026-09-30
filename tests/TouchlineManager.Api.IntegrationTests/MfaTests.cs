using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TouchlineManager.Application.Auth;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The two-step multi-factor login over the real stack (ADR-0042).
/// </summary>
/// <remarks>
/// The whole flow is driven the way a client drives it: enrol with a live session, confirm with a computed
/// code, then sign in and be asked for the code. The tokens are inspected, not trusted, so the <c>mfa</c>
/// claim is asserted as the thing that gates the admin surface.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class MfaTests
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MfaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_confirmed_authenticator_turns_login_into_a_challenge_and_completing_it_issues_an_mfa_session()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var (secret, recoveryCodes) = await MfaScenario.EnrolAsync(client);

        recoveryCodes.Should().HaveCount(MfaRecoveryCodeIssuer.CodeCount);

        var challenge = await LastChallengeAsync(client, manager);

        challenge.ChallengeToken.Should().NotBeNullOrWhiteSpace();

        var completion = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = challenge.ChallengeToken, code = MfaScenario.Code(secret) });

        completion.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = (await completion.Content.ReadFromJsonAsync<AuthSessionResponse>())!;

        MfaScenario.ReadClaim(session.AccessToken, "mfa").Should().Be("true");

        // A refresh must keep the assurance the session was granted.
        var cookie = AuthTestHelpers.ReadCookie(completion, AuthTestHelpers.RefreshCookieName);

        cookie.Should().NotBeNull("completing the challenge is what issues the refresh cookie");

        var refreshed = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh").WithRefreshCookie(cookie!));

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedSession = (await refreshed.Content.ReadFromJsonAsync<AuthSessionResponse>())!;

        MfaScenario.ReadClaim(refreshedSession.AccessToken, "mfa").Should().Be("true");
    }

    [Fact]
    public async Task A_wrong_code_does_not_complete_the_login()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        await MfaScenario.EnrolAsync(client);

        var challenge = await LastChallengeAsync(client, manager);

        var completion = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = challenge.ChallengeToken, code = "000000" });

        completion.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MfaScenario.ErrorCodeAsync(completion)).Should().Be(AuthErrorCodes.MfaCodeInvalid);
    }

    [Fact]
    public async Task A_recovery_code_completes_a_login_once()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        var (secret, recoveryCodes) = await MfaScenario.EnrolAsync(client);
        var recoveryCode = recoveryCodes[0];

        // The enrolment session is still valid and still carries no second factor; a fresh login gives a
        // challenge which the recovery code completes.
        var challenge = await LastChallengeAsync(client, manager);

        var first = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = challenge.ChallengeToken, code = recoveryCode });

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = (await LastChallengeAsync(client, manager)).ChallengeToken, code = recoveryCode });

        second.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a recovery code is single use");

        // The time-based code still works after a recovery code is spent.
        var challenge3 = await LastChallengeAsync(client, manager);

        var third = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = challenge3.ChallengeToken, code = MfaScenario.Code(secret) });

        third.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_manager_without_a_second_factor_signs_in_normally()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        manager.AccessToken.Should().NotBeNullOrWhiteSpace();
        MfaScenario.ReadClaim(manager.AccessToken, "mfa").Should().Be("false");
    }

    [Fact]
    public async Task A_challenge_token_is_not_accepted_as_an_access_token()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);
        client.WithBearer(manager.AccessToken);

        await MfaScenario.EnrolAsync(client);

        var challenge = await LastChallengeAsync(client, manager);

        using var attacker = _fixture.Factory.CreateClient();
        attacker.WithBearer(challenge.ChallengeToken);

        var response = await attacker.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "a token that carries a purpose must never be accepted as an access token");
    }

    private static async Task<MfaChallengeResponse> LastChallengeAsync(
        HttpClient client,
        RegisteredManager manager)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = manager.Email, password = manager.Password });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        return (await response.Content.ReadFromJsonAsync<MfaChallengeResponse>())!;
    }
}
