using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The training endpoints over HTTP, against the real composition root (master plan §10.4; `TRN-1`,
/// `TRN-2`, `CONC-1`).
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle a manager drives: read the club's training state, save a plan, revise it under its version,
/// set and clear a player's individual focus — each with the refusal the contract asks for when the version
/// is missing or stale, and when the player belongs to somebody else.
/// </para>
/// <para>
/// The tests are tolerant of a re-used club, as the tactics tests are: the world is seeded once for the
/// whole collection and a released club keeps the plan its previous manager saved, so a test reads the
/// current state rather than assuming a fresh club.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class TrainingTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TrainingTests(ApiFixture fixture) => _fixture = fixture;

    /// <summary>Seeds the world this collection reads. Idempotent, so a second run is a no-op.</summary>
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
    public async Task A_manager_reads_their_training_state()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await client.GetAsync("/api/v1/training");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var training = (await response.Content.ReadFromJsonAsync<TrainingResponse>())!;

        training.Players.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget, "SQ-1");
        training.TeamFocusOptions.Should().Contain(TrainingFocus.Balanced.ToCode(), "TRN-1");
        training.IntensityOptions.Should().NotBeEmpty();
        training.FocusFamilyOptions.Should().Contain(AttributeFamilies.TechnicalCode, "TRN-2");

        training.Players.Should().OnlyContain(player => player.State.Condition >= 0 && player.State.Condition <= 100);
        training.Players.Should().OnlyContain(player => player.Age > 0);

        if (training.IsConfigured)
        {
            response.Headers.ETag.Should().NotBeNull("a set plan exposes its version as an entity tag");
        }

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_saves_and_revises_a_training_plan_under_its_version()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var current = (await client.GetFromJsonAsync<TrainingResponse>("/api/v1/training"))!;
        var expectedVersion = current.IsConfigured ? current.Version : (long?)null;

        var save = await PutAsync(
            client,
            "/api/v1/training",
            new { teamFocus = "fitness", intensity = "intense" },
            expectedVersion);

        save.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        save.Headers.ETag.Should().NotBeNull();

        var saved = (await save.Content.ReadFromJsonAsync<TrainingResponse>())!;

        saved.TeamFocus.Should().Be("fitness", "TRN-1");
        saved.Intensity.Should().Be("intense");
        saved.IsConfigured.Should().BeTrue();

        // A revise now needs the version: without it the server cannot know it is not overwriting a change.
        var noVersion = await PutAsync(
            client,
            "/api/v1/training",
            new { teamFocus = "recovery", intensity = "light" },
            expectedVersion: null);

        noVersion.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, "CONC-1");
        (await CodeAsync(noVersion)).Should().Be("PRECONDITION_REQUIRED");

        // A stale version is refused rather than overwriting the winner of the race.
        var stale = await PutAsync(
            client,
            "/api/v1/training",
            new { teamFocus = "balanced", intensity = "normal" },
            saved.Version + 100);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "CONC-1");
        (await CodeAsync(stale)).Should().Be("PRECONDITION_FAILED");

        // The current version lands and advances the plan.
        var revise = await PutAsync(
            client,
            "/api/v1/training",
            new { teamFocus = "recovery", intensity = "light" },
            saved.Version);

        revise.StatusCode.Should().Be(HttpStatusCode.OK);

        var revised = (await revise.Content.ReadFromJsonAsync<TrainingResponse>())!;

        revised.TeamFocus.Should().Be("recovery");
        revised.Version.Should().BeGreaterThan(saved.Version);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_sets_and_clears_one_players_individual_focus()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var training = (await client.GetFromJsonAsync<TrainingResponse>("/api/v1/training"))!;
        var player = training.Players[0];

        // A player who already holds a focus (a re-used club) needs its version; a fresh player does not.
        var set = await PutAsync(
            client,
            $"/api/v1/players/{player.Id}/training-focus",
            new { focusFamily = "technical" },
            player.FocusVersion);

        set.StatusCode.Should().Be(HttpStatusCode.OK);

        var focus = (await set.Content.ReadFromJsonAsync<PlayerTrainingFocusResponse>())!;

        focus.PlayerId.Should().Be(player.Id);
        focus.FocusFamily.Should().Be("technical", "TRN-2");
        focus.Version.Should().BeGreaterThan(0);

        // A stale version is refused; the current one clears the focus.
        var stale = await PutAsync(
            client,
            $"/api/v1/players/{player.Id}/training-focus",
            new { focusFamily = "physical" },
            focus.Version + 100);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "CONC-1");

        var clear = await PutAsync(
            client,
            $"/api/v1/players/{player.Id}/training-focus",
            new { focusFamily = (string?)null },
            focus.Version);

        clear.StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = (await clear.Content.ReadFromJsonAsync<PlayerTrainingFocusResponse>())!;

        cleared.FocusFamily.Should().BeNull("a null family returns the player to the team plan");
        cleared.Version.Should().Be(0);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Another_club_s_player_focus_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var (_, otherClubId) = await FirstAvailableClubAsync(client);
        var otherPlayerId = await FirstPlayerOfClubAsync(otherClubId);

        var response = await PutAsync(
            client,
            $"/api/v1/players/{otherPlayerId}/training-focus",
            new { focusFamily = "technical" },
            expectedVersion: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.ClubNotManaged, "§15.4: the other-club case");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await client.GetAsync("/api/v1/training");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Issues a JSON PUT with an optional strong <c>If-Match</c> version.</summary>
    private static Task<HttpResponseMessage> PutAsync<T>(
        HttpClient client,
        string url,
        T body,
        long? expectedVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body),
        };

        if (expectedVersion is { } version)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        }

        return client.SendAsync(request);
    }

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
    private static async Task<Guid> OnboardAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var (_, clubId) = await FirstAvailableClubAsync(client);
        var claim = await ClaimAsync(client, clubId, Guid.CreateVersion7().ToString());

        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        return clubId;
    }

    /// <summary>Finds the first club in the world that still has no manager.</summary>
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

    /// <summary>Reads one player from a club the caller does not manage.</summary>
    private async Task<Guid> FirstPlayerOfClubAsync(Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await dbContext.PlayerContracts
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .OrderBy(contract => contract.PlayerId)
            .Select(contract => contract.PlayerId)
            .FirstAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
