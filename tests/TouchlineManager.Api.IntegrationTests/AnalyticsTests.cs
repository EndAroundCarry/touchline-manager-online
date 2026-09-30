using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Auth;
using TouchlineManager.Contracts.Ops;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The operator-only operational funnels over the real database (master plan §16 Stage 13, `F-54`,
/// ADR-0041).
/// </summary>
/// <remarks>
/// <para>
/// This is the first role-gated endpoint in the product, so the gate is asserted from both sides: anonymous
/// is refused, a plain manager is forbidden, and an operator reads it.
/// </para>
/// <para>
/// The last test also enforces the disclosure boundary as executable policy: the raw body carries counts
/// and no personal data, which is the shape <c>docs/security/data-classification.md</c> §4 requires and
/// `MAT-11` forbids leaking.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsTests : IAsyncLifetime
{
    private const string Path = "/api/v1/ops/analytics/funnels";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public AnalyticsTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world the funnels are read against. Idempotent, so a second run is a no-op.</summary>
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
    public async Task The_funnels_are_not_readable_anonymously()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync(Path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_manager_without_the_role_is_forbidden()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync(Path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_operator_reads_the_funnel_counts_and_nothing_else()
    {
        using var client = _fixture.Factory.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        // No administrative surface exists until Stage 14, so the role is granted directly and the session
        // is renewed: a role is stamped into the access token when it is issued.
        await GrantOperatorAsync(manager.UserId);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = manager.Email, password = manager.Password });

        login.EnsureSuccessStatusCode();

        var session = (await login.Content.ReadFromJsonAsync<AuthSessionResponse>())!;
        client.WithBearer(session.AccessToken);

        var response = await client.GetAsync(Path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("it is live operator data, so it is never cached");

        var payload = await response.Content.ReadAsStringAsync();

        // The disclosure boundary: the body is counts and instants, so no account address can appear in it.
        payload.Should().NotContain("@", "no email address may reach an operator through this surface");

        var funnels = JsonSerializer.Deserialize<OperationalAnalyticsResponse>(payload, JsonOptions)!;

        funnels.WorldId.Should().NotBeEmpty();
        funnels.SeasonNumber.Should().BeGreaterThan(0);
        funnels.Onboarding.Registered.Should().BeGreaterThan(0, "the journey that produced this operator registered accounts");

        funnels.Onboarding.Registered.Should().BeGreaterThanOrEqualTo(funnels.Onboarding.Verified);
        funnels.Onboarding.Verified.Should().BeGreaterThanOrEqualTo(funnels.Onboarding.ProfilesCreated);
        funnels.Onboarding.ProfilesCreated.Should().BeGreaterThanOrEqualTo(funnels.Onboarding.ClubsClaimed);
    }

    private async Task GrantOperatorAsync(Guid userId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == userId);

        user.GrantRole(UserRoles.Operator, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();
    }
}
