using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Contracts.Auth;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// Captures the emails the API would otherwise send over SMTP.
/// </summary>
/// <remarks>
/// Auth tests have to read the verification and reset links, so the provider is replaced with a
/// recorder. Nothing else about the flow changes: the use cases still commit before dispatching and
/// still tolerate a failure, they simply hand the message to a list instead of a socket.
/// </remarks>
public sealed partial class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];
    private readonly object _gate = new();

    /// <summary>Gets a snapshot of the captured messages.</summary>
    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    /// <summary>Forgets every captured message.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The cookie name the API issues. Mirrors the default in <c>AuthOptions</c>.</summary>
public static partial class AuthTestHelpers
{
    /// <summary>The refresh cookie's name.</summary>
    public const string RefreshCookieName = "touchline_refresh";

    /// <summary>Reads one cookie value out of a response's <c>Set-Cookie</c> headers.</summary>
    public static string? ReadCookie(HttpResponseMessage response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';')[0];
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0 && pair[..separator].Trim() == name)
            {
                return pair[(separator + 1)..];
            }
        }

        return null;
    }

    /// <summary>Builds a request carrying a refresh cookie.</summary>
    public static HttpRequestMessage WithRefreshCookie(this HttpRequestMessage request, string token)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Headers.Add("Cookie", $"{RefreshCookieName}={token}");

        return request;
    }

    /// <summary>Extracts the token from a link in an email body.</summary>
    public static string ExtractToken(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return TokenPattern().Match(body) is { Success: true } match
            ? Uri.UnescapeDataString(match.Groups[1].Value)
            : throw new InvalidOperationException("No token found in the email body.");
    }

    [GeneratedRegex(@"[?&]token=([A-Za-z0-9_\-%]+)")]
    private static partial Regex TokenPattern();
}

/// <summary>An account that has completed registration, verification, and sign-in.</summary>
/// <param name="UserId">The account identity.</param>
/// <param name="Email">The email address.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Password">The password.</param>
/// <param name="AccessToken">A valid access token.</param>
/// <param name="RefreshToken">The refresh token from the login cookie.</param>
public sealed record RegisteredManager(
    Guid UserId,
    string Email,
    string DisplayName,
    string Password,
    string AccessToken,
    string RefreshToken);

/// <summary>Drives the auth flow the way a client does, so tests share one honest path.</summary>
public static class AuthScenario
{
    /// <summary>The password used by scenario accounts.</summary>
    public const string Password = "correct-horse-battery";

    /// <summary>Registers, verifies, and signs in a manager.</summary>
    public static async Task<RegisteredManager> CreateVerifiedManagerAsync(
        ApiFixture fixture,
        HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(client);

        fixture.Email.Clear();

        var email = $"manager-{Guid.NewGuid():N}@example.com";
        var displayName = $"Mgr{Guid.NewGuid():N}"[..13];

        var registration = await RegisterAsync(client, email, displayName);

        var verificationEmail = fixture.Email.Messages.Single(message => message.To == email);
        var token = AuthTestHelpers.ExtractToken(verificationEmail.TextBody);

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { userId = registration.UserId, token });

        verifyResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = Password });

        loginResponse.EnsureSuccessStatusCode();

        var session = await loginResponse.Content.ReadFromJsonAsync<AuthSessionResponse>();
        var refreshToken = AuthTestHelpers.ReadCookie(loginResponse, AuthTestHelpers.RefreshCookieName);

        return new RegisteredManager(
            registration.UserId,
            email,
            displayName,
            Password,
            session!.AccessToken,
            refreshToken!);
    }

    /// <summary>Registers an account without verifying it.</summary>
    public static async Task<RegistrationAcceptedResponse> RegisterAsync(
        HttpClient client,
        string email,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            displayName,
            password = Password,
            acceptTerms = true,
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>())!;
    }

    /// <summary>Attaches a bearer token to a client's default headers.</summary>
    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
