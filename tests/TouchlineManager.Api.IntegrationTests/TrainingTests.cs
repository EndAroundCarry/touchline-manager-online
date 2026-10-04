using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Squad;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The training endpoints over HTTP, against the real composition root (master plan §10.4; `TRN-1`,
/// `TRN-2`, `TRN-17`, `CONC-1`).
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle a manager drives: read the club's training state, save a plan, revise it under its version,
/// set and clear a player's programme, and read a player's training history — each with the refusal the
/// contract asks for when the version is missing or stale, and when the player belongs to somebody else.
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
        training.IntensityOptions.Should().NotBeEmpty();

        training.Programmes.Select(programme => programme.Code)
            .Should().BeEquivalentTo(TrainingProgrammes.All.Select(definition => definition.Code), "TRN-1");
        training.Programmes.Single(programme => programme.Code == "forward").Attributes
            .Should().Contain(attribute => attribute.Name == "finishing" && attribute.Weight == 3);
        training.Programmes.Single(programme => programme.Code == "recovery").Attributes.Should().BeEmpty();

        training.Players.Should().OnlyContain(player => player.State.Condition >= 0 && player.State.Condition <= 100);
        training.Players.Should().OnlyContain(player => player.Age > 0);
        training.Players.Should().OnlyContain(
            player => player.Attributes.Technical.Finishing >= 1 && player.Attributes.Goalkeeping.Handling >= 1,
            "every player carries the full attribute set (TRN-4)");
        training.Players.Where(player => player.FocusVersion is null).Should().OnlyContain(
            player => player.IsDefaultProgramme && player.Programme == player.DefaultProgramme,
            "a player with no override trains the position default (TRN-1)");
        training.Players.First(player => player.PrimaryPosition == "gk" && player.FocusVersion is null)
            .DefaultProgramme.Should().Be("goalkeeper");

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContainEquivalentOf("potential", "hidden potential is never exposed (TRN-9)");
        raw.Should().NotContainEquivalentOf("aptitude", "per-player aptitude is hidden (TRN-9)");

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
            new { intensity = "intense" },
            expectedVersion);

        save.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        save.Headers.ETag.Should().NotBeNull();

        var saved = (await save.Content.ReadFromJsonAsync<TrainingResponse>())!;

        saved.Intensity.Should().Be("intense", "TRN-1");
        saved.IsConfigured.Should().BeTrue();

        // A revise now needs the version: without it the server cannot know it is not overwriting a change.
        var noVersion = await PutAsync(
            client,
            "/api/v1/training",
            new { intensity = "light" },
            expectedVersion: null);

        noVersion.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, "CONC-1");
        (await CodeAsync(noVersion)).Should().Be("PRECONDITION_REQUIRED");

        // A stale version is refused rather than overwriting the winner of the race.
        var stale = await PutAsync(
            client,
            "/api/v1/training",
            new { intensity = "normal" },
            saved.Version + 100);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "CONC-1");
        (await CodeAsync(stale)).Should().Be("PRECONDITION_FAILED");

        // The current version lands and advances the plan.
        var revise = await PutAsync(
            client,
            "/api/v1/training",
            new { intensity = "light" },
            saved.Version);

        revise.StatusCode.Should().Be(HttpStatusCode.OK);

        var revised = (await revise.Content.ReadFromJsonAsync<TrainingResponse>())!;

        revised.Intensity.Should().Be("light");
        revised.Version.Should().BeGreaterThan(saved.Version);

        var invalid = await PutAsync(
            client,
            "/api/v1/training",
            new { intensity = "ludicrous" },
            revised.Version);

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unknown intensity is a field error");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_sets_and_clears_one_players_programme()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var training = (await client.GetFromJsonAsync<TrainingResponse>("/api/v1/training"))!;
        var player = training.Players.First(candidate => candidate.PrimaryPosition != "gk");
        var url = $"/api/v1/players/{player.Id}/training-programme";

        // A player who already holds an override (a re-used club) needs its version; a fresh player does not.
        var set = await PutAsync(client, url, new { programme = "physical" }, player.FocusVersion);

        set.StatusCode.Should().Be(HttpStatusCode.OK);
        set.Headers.ETag.Should().NotBeNull("an override exposes its version as an entity tag");

        var programme = (await set.Content.ReadFromJsonAsync<PlayerTrainingProgrammeResponse>())!;

        programme.PlayerId.Should().Be(player.Id);
        programme.Programme.Should().Be("physical", "TRN-1");
        programme.IsDefaultProgramme.Should().BeFalse();
        programme.DefaultProgramme.Should().Be(player.DefaultProgramme);
        programme.Version.Should().BeGreaterThan(0);

        // The training read now reports the override for that player.
        var reread = (await client.GetFromJsonAsync<TrainingResponse>("/api/v1/training"))!;
        var reloaded = reread.Players.Single(candidate => candidate.Id == player.Id);

        reloaded.Programme.Should().Be("physical");
        reloaded.IsDefaultProgramme.Should().BeFalse();
        reloaded.FocusVersion.Should().Be(programme.Version);

        // Changing a set override needs its version, and a stale one is refused.
        var noVersion = await PutAsync(client, url, new { programme = "mental" }, expectedVersion: null);

        noVersion.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, "CONC-1");

        var stale = await PutAsync(client, url, new { programme = "mental" }, programme.Version + 100);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "CONC-1");

        var unknown = await PutAsync(client, url, new { programme = "balanced" }, programme.Version);

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a programme outside the catalogue is a field error");

        var clear = await PutAsync(client, url, new { programme = (string?)null }, programme.Version);

        clear.StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = (await clear.Content.ReadFromJsonAsync<PlayerTrainingProgrammeResponse>())!;

        cleared.Programme.Should().Be(player.DefaultProgramme, "a null programme returns the player to the position default");
        cleared.IsDefaultProgramme.Should().BeTrue();
        cleared.Version.Should().Be(0);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Another_club_s_player_programme_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var (_, otherClubId) = await FirstAvailableClubAsync(client);
        var otherPlayerId = await FirstPlayerOfClubAsync(otherClubId);

        var response = await PutAsync(
            client,
            $"/api/v1/players/{otherPlayerId}/training-programme",
            new { programme = "forward" },
            expectedVersion: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.ClubNotManaged, "§15.4: the other-club case");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_reads_a_players_training_regime_and_history_after_a_progression_run()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var training = (await client.GetFromJsonAsync<TrainingResponse>("/api/v1/training"))!;
        var player = training.Players.First(candidate => candidate.PrimaryPosition != "gk");

        var set = await PutAsync(
            client,
            $"/api/v1/players/{player.Id}/training-programme",
            new { programme = "winger" },
            player.FocusVersion);

        set.StatusCode.Should().Be(HttpStatusCode.OK);

        // A day far enough ahead that no other test's run has already covered it.
        var day = new DateOnly(2031, 1, 1);

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<RunDailyProgression>()
                .ExecuteAsync(day, CancellationToken.None);
        }

        var response = await client.GetAsync($"/api/v1/players/{player.Id}/training");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var history = (await response.Content.ReadFromJsonAsync<PlayerTrainingResponse>())!;

        history.PlayerId.Should().Be(player.Id);
        history.Regime.Programme.Should().Be("winger");
        history.Regime.IsDefaultProgramme.Should().BeFalse();
        history.Regime.Attributes.Should().Contain(attribute => attribute.Name == "dribbling" && attribute.Weight == 3);
        history.Regime.Attributes.Should().OnlyContain(attribute => attribute.Weight >= 1 && attribute.Weight <= 3);

        history.Days.Should().Contain(entry => entry.Day == day && entry.Programme == "winger");

        var recorded = history.Days.Single(entry => entry.Day == day);

        recorded.Intensity.Should().Be(history.Regime.Intensity);
        recorded.PointsGained.Should().Be(recorded.AttributeChanges.Where(change => change.Delta > 0).Sum(change => change.Delta));
        recorded.PointsLost.Should().Be(recorded.AttributeChanges.Where(change => change.Delta < 0).Sum(change => -change.Delta));

        var summary = history.Summary.Single(line => line.Programme == "winger");

        summary.Label.Should().Be("Winger");
        summary.Net.Should().Be(summary.PointsGained - summary.PointsLost);
        summary.Days.Should().Be(history.Days.Count(entry => entry.Programme == "winger"));

        var raw = await response.Content.ReadAsStringAsync();

        raw.Should().NotContainEquivalentOf("potential", "hidden potential is never exposed (TRN-9)");
        raw.Should().NotContainEquivalentOf("aptitude", "per-player aptitude is hidden (TRN-9)");

        // The window is the most recent days, and a nonsense size is clamped rather than refused.
        var windowed = (await client.GetFromJsonAsync<PlayerTrainingResponse>(
            $"/api/v1/players/{player.Id}/training?days=1"))!;

        windowed.Days.Should().ContainSingle().Which.Day.Should().Be(day);

        var clamped = await client.GetAsync($"/api/v1/players/{player.Id}/training?days=-5");

        clamped.StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Another_club_s_player_training_is_refused_and_an_unknown_player_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var (_, otherClubId) = await FirstAvailableClubAsync(client);
        var otherPlayerId = await FirstPlayerOfClubAsync(otherClubId);

        var other = await client.GetAsync($"/api/v1/players/{otherPlayerId}/training");

        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(other)).Should().Be(SquadErrorCodes.ClubNotManaged, "a manager sees only their own players");

        var unknown = await client.GetAsync($"/api/v1/players/{Guid.CreateVersion7()}/training");

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(unknown)).Should().Be(SquadErrorCodes.PlayerNotFound);

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
