using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The onboarding journey over HTTP, against the real composition root (master plan §10.2, §7.6).
/// </summary>
/// <remarks>
/// <para>
/// This is the interface-level half of the Stage 3 exit criteria: a manager creates a profile, reads the
/// countries, sees which clubs are free, takes one over, and inherits it — with each refusal arriving as
/// the specific code master plan §7.6 requires rather than as a generic failure.
/// </para>
/// <para>
/// The world is seeded once for the whole collection, because "there is exactly one world" is a product
/// rule and the seeded pyramid is what every test here reads.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class OnboardingTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public OnboardingTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world this collection onboards into. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_manager_creates_a_profile_claims_a_club_and_inherits_it()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        // Before a profile exists, the account is told exactly where it stands.
        var before = await client.GetFromJsonAsync<OnboardingStateResponse>("/api/v1/club-tenure");

        before!.Manager.Should().BeNull();
        before.Tenure.Should().BeNull();

        var world = await client.GetFromJsonAsync<WorldResponse>("/api/v1/world");

        world.Should().NotBeNull();
        world!.CurrentSeason.Should().NotBeNull();
        world.AcceptsClaims.Should().BeTrue();

        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        countries.Should().HaveCount(6);
        countries!.Select(country => country.Code).Should().BeEquivalentTo("ENG", "ESP", "GER", "ITA", "FRA", "ROU");

        // The profile is created once. A repeat answers 200 with the same profile rather than a second one.
        var created = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var profile = (await created.Content.ReadFromJsonAsync<ManagerProfileResponse>())!;

        var repeated = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        repeated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repeated.Content.ReadFromJsonAsync<ManagerProfileResponse>())!.Id.Should().Be(profile.Id);

        var (countryId, clubId) = await FirstAvailableClubAsync(client);

        var capacity = await client.GetFromJsonAsync<CountryCapacityResponse>(
            $"/api/v1/countries/{countryId}/capacity");

        capacity!.LowestActiveTier.Should().Be(1);
        capacity.ClubsInLowestTier.Should().Be(18);
        capacity.AvailableClubs.Should().BeGreaterThan(0);
        capacity.Provisioning.Should().BeNull("nothing is being generated while the tier still has room");

        var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
            $"/api/v1/countries/{countryId}/available-clubs");

        listing!.Clubs.Should().HaveCount(18);
        listing.Clubs.Single(club => club.Id == clubId).IsAvailable.Should().BeTrue();

        // A claim without an idempotency key is refused, because a retried claim would otherwise be a
        // second tenure.
        var keyless = await client.PostAsJsonAsync("/api/v1/club-claims", new { clubId });

        keyless.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(keyless)).Should().Be(WorldErrorCodes.IdempotencyKeyRequired);

        var key = Guid.CreateVersion7().ToString();
        var claimed = await ClaimAsync(client, clubId, key);

        claimed.StatusCode.Should().Be(HttpStatusCode.Created);
        claimed.Headers.Location!.ToString().Should().Contain($"/clubs/{clubId}/dashboard");

        var dashboard = (await claimed.Content.ReadFromJsonAsync<ClubDashboardResponse>())!;

        dashboard.Club.Id.Should().Be(clubId);
        dashboard.Country.Id.Should().Be(countryId);
        dashboard.Division.TierNumber.Should().Be(1);
        dashboard.Control.Status.Should().Be("active");
        dashboard.Finances.CashMinor.Should().BeGreaterThan(0);

        // Repeating the same key replays the first outcome instead of creating a second tenure.
        var replay = await ClaimAsync(client, clubId, key);

        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await replay.Content.ReadFromJsonAsync<ClubDashboardResponse>())!
            .Control.TenureId.Should().Be(dashboard.Control.TenureId);

        // The tenure is now readable, and the manager cannot hold a second club (OCC-9).
        var tenure = await client.GetFromJsonAsync<OnboardingStateResponse>("/api/v1/club-tenure");

        tenure!.Tenure!.ClubId.Should().Be(clubId);

        var (_, secondClubId) = await FirstAvailableClubAsync(client);
        var second = await ClaimAsync(client, secondClubId, Guid.CreateVersion7().ToString());

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(second)).Should().Be(WorldErrorCodes.ManagerHasActiveClub);

        // The dashboard is reachable on its own, which is what the client links to after a claim.
        var read = await client.GetFromJsonAsync<ClubDashboardResponse>($"/api/v1/clubs/{clubId}/dashboard");

        read!.Club.Name.Should().Be(dashboard.Club.Name);

        // Resigning closes the tenure and starts the cooldown.
        var resign = await client.PostAsync("/api/v1/club-tenure/resign", content: null);

        resign.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterResigning = (await resign.Content.ReadFromJsonAsync<OnboardingStateResponse>())!;

        afterResigning.Tenure.Should().BeNull();
        afterResigning.Manager!.TakeoverCooldownUntil.Should().NotBeNull();

        // The cooldown is a refusal the client can act on, so it carries the moment it lapses.
        var duringCooldown = await ClaimAsync(client, secondClubId, Guid.CreateVersion7().ToString());

        duringCooldown.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(duringCooldown)).Should().Be(WorldErrorCodes.ManagerInCooldown);

        var problem = JsonDocument.Parse(await duringCooldown.Content.ReadAsStringAsync());

        problem.RootElement.TryGetProperty("cooldownUntil", out var cooldownUntil).Should().BeTrue();
        cooldownUntil.ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task An_unconfirmed_account_may_not_claim_a_club()
    {
        // ADR-0002: verification is required before a club claim.
        _fixture.Email.Clear();

        using var client = CreateClient();
        var email = $"unverified-{Guid.NewGuid():N}@example.com";

        await AuthScenario.RegisterAsync(client, email, $"Mgr{Guid.NewGuid():N}"[..13]);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = AuthScenario.Password });

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = (await login.Content.ReadFromJsonAsync<TouchlineManager.Contracts.Auth.AuthSessionResponse>())!;

        client.WithBearer(session.AccessToken);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var response = await ClaimAsync(client, clubId, Guid.CreateVersion7().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_claim_without_a_manager_profile_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var response = await ClaimAsync(client, clubId, Guid.CreateVersion7().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be(WorldErrorCodes.ManagerProfileRequired);
    }

    [Fact]
    public async Task An_unknown_club_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await ClaimAsync(client, Guid.CreateVersion7(), Guid.CreateVersion7().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(WorldErrorCodes.ClubNotFound);
    }

    [Fact]
    public async Task Two_managers_claiming_one_club_at_once_produce_one_winner()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();

        var first = await AuthScenario.CreateVerifiedManagerAsync(_fixture, firstClient);
        var second = await AuthScenario.CreateVerifiedManagerAsync(_fixture, secondClient);

        firstClient.WithBearer(first.AccessToken);
        secondClient.WithBearer(second.AccessToken);

        foreach (var client in new[] { firstClient, secondClient })
        {
            await client.PostAsJsonAsync(
                "/api/v1/manager-profile",
                new { locale = "en-GB", timeZone = "Europe/London" });
        }

        var (_, clubId) = await FirstAvailableClubAsync(firstClient);

        var responses = await Task.WhenAll(
            ClaimAsync(firstClient, clubId, Guid.CreateVersion7().ToString()),
            ClaimAsync(secondClient, clubId, Guid.CreateVersion7().ToString()));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var refused = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);

        // The country still has room, so the useful answer is "pick another club".
        (await CodeAsync(refused)).Should().Be(WorldErrorCodes.ClubAlreadyClaimed);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>
    /// Finds the first club in the world that still has no manager.
    /// </summary>
    /// <remarks>
    /// Tests share one seeded world, so asking which club is free keeps them independent of each other's
    /// order — and it exercises the same endpoint the client uses to offer a choice.
    /// </remarks>
    private static async Task<(Guid CountryId, Guid ClubId)> FirstAvailableClubAsync(HttpClient client)
    {
        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is not null)
            {
                return (country.Id, available.Id);
            }
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    private static Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid clubId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
        {
            Content = JsonContent.Create(new { clubId }),
        };

        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return client.SendAsync(request);
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
