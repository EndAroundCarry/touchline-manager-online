using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>An operator with a confirmed second factor and a session that carries it.</summary>
/// <param name="UserId">The operator's account identity.</param>
/// <param name="Email">The operator's email address.</param>
/// <param name="AccessToken">A session that completed the second factor.</param>
/// <param name="Secret">The raw TOTP secret, so a code can be computed.</param>
/// <param name="RecoveryCode">An unused recovery code.</param>
public sealed record OperatorSession(
    Guid UserId,
    string Email,
    string AccessToken,
    byte[] Secret,
    string RecoveryCode);

/// <summary>Drives the multi-factor flow the way a client does, so the tests share one honest path.</summary>
public static class MfaScenario
{
    /// <summary>The header a mutation carries its fresh code in.</summary>
    public const string CodeHeader = "X-MFA-Code";

    /// <summary>Computes the current code for a secret.</summary>
    public static string Code(byte[] secret) => Totp.Compute(secret, Totp.CurrentStep(DateTimeOffset.UtcNow));

    /// <summary>
    /// Grants a role directly. Role administration is an operator tool, not an endpoint (ADR-0042), so the
    /// tests take the same path the tool takes.
    /// </summary>
    public static async Task GrantRoleAsync(ApiFixture fixture, Guid userId, string role)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var user = await db.Users.Include(candidate => candidate.Roles).SingleAsync(candidate => candidate.Id == userId);

        user.GrantRole(role, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
    }

    /// <summary>Enrols and confirms an authenticator for the signed-in account.</summary>
    public static async Task<(byte[] Secret, IReadOnlyList<string> RecoveryCodes)> EnrolAsync(HttpClient client)
    {
        var enrolment = await client.PostAsync("/api/v1/auth/mfa/enrol", content: null);
        enrolment.EnsureSuccessStatusCode();

        var material = (await enrolment.Content.ReadFromJsonAsync<MfaEnrolmentResponse>())!;
        var secret = Base32.Decode(material.Secret);

        var confirm = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/enrol/confirm",
            new { code = Code(secret) });

        confirm.EnsureSuccessStatusCode();

        return (secret, material.RecoveryCodes);
    }

    /// <summary>Signs in and completes the challenge, returning the session that carries the second factor.</summary>
    public static async Task<AuthSessionResponse> CompleteLoginAsync(
        HttpClient client,
        RegisteredManager manager,
        byte[] secret)
    {
        var challengeResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = manager.Email, password = manager.Password });

        challengeResponse.StatusCode.Should().Be(
            HttpStatusCode.Accepted,
            "the account has a confirmed second factor, so the password alone is not enough");

        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<MfaChallengeResponse>())!;

        var completion = await client.PostAsJsonAsync(
            "/api/v1/auth/mfa/login",
            new { challengeToken = challenge.ChallengeToken, code = Code(secret) });

        completion.EnsureSuccessStatusCode();

        return (await completion.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
    }

    /// <summary>Creates an operator: a verified manager, granted the role, with a confirmed second factor.</summary>
    public static Task<OperatorSession> CreateOperatorAsync(ApiFixture fixture, HttpClient client) =>
        CreateWithRoleAsync(fixture, client, UserRoles.Operator);

    /// <summary>Creates a role holder with a confirmed second factor, e.g. a read-only support operator.</summary>
    public static async Task<OperatorSession> CreateWithRoleAsync(
        ApiFixture fixture,
        HttpClient client,
        string role)
    {
        var manager = await AuthScenario.CreateVerifiedManagerAsync(fixture, client);

        await GrantRoleAsync(fixture, manager.UserId, role);

        // Sign in again so the freshly granted role is stamped into the token, then enrol.
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = manager.Email, password = manager.Password });

        login.EnsureSuccessStatusCode();

        var session = (await login.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
        client.WithBearer(session.AccessToken);

        var (secret, recoveryCodes) = await EnrolAsync(client);
        var mfaSession = await CompleteLoginAsync(client, manager, secret);

        return new OperatorSession(manager.UserId, manager.Email, mfaSession.AccessToken, secret, recoveryCodes[0]);
    }

    /// <summary>Reads a claim out of an access token's payload, without validating its signature.</summary>
    public static string? ReadClaim(string accessToken, string claim)
    {
        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        return document.RootElement.TryGetProperty(claim, out var value) ? value.GetString() : null;
    }

    /// <summary>Reads the stable error code out of a Problem Details body.</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);

        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
