using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Http;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The tactics surface over HTTP, against the real composition root (master plan §10.4, §11.1, §15.4;
/// F-19).
/// </summary>
/// <remarks>
/// The ETag contract is the point of this suite: a plan is read with its version, saved with it back in
/// <c>If-Match</c>, and a stale or missing version is answered with <c>412</c> or <c>428</c> rather than a
/// silent overwrite (`CONC-1`, ADR-0009). The validation preview is asserted as the machine-readable
/// issues the screen draws on the pitch, not as prose.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class TacticsTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public TacticsTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task The_tactics_screen_reports_the_formations_and_the_squad()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var tactics = await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics");

        tactics.Should().NotBeNull();
        tactics!.ClubId.Should().Be(clubId);
        tactics.SelectablePlayers.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget);
        tactics.SelectablePlayers.Should().OnlyContain(player => !string.IsNullOrWhiteSpace(player.FullName));
        tactics.SelectablePlayers.Should().OnlyContain(player => player.PositionFamily.Length > 0);

        // Every saved plan is well formed, whichever manager saved it: a plan belongs to the club, so a
        // released club keeps the plans its previous manager left behind.
        tactics.Plans.Should().OnlyContain(plan => plan.Slots.Count == FormationLayouts.SlotCount);
        tactics.Plans.Should().OnlyContain(plan => plan.Version >= 1);

        tactics.Formations.Should().HaveCount(6, "TAC-1..TAC-6");
        tactics.Formations.Should().OnlyContain(formation => formation.Slots.Count == 11);
        tactics.Formations.Select(formation => formation.Code)
            .Should().BeEquivalentTo(["4-4-2", "4-3-3", "4-2-3-1", "4-1-4-1", "3-5-2", "5-3-2"]);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_creates_and_revises_a_plan_under_a_version()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        // A released club keeps its plans, so whether this is the club's first plan is read rather than
        // assumed; the first plan a club ever saves is the one that becomes its default (INS-11).
        var before = await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics");
        var becomesDefault = before!.Plans.All(existing => !existing.IsDefault);

        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Home shape"));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.ETag.Should().NotBeNull();

        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.IsDefault.Should().Be(becomesDefault);
        plan.FormationPreset.Should().Be("4-4-2");
        plan.Slots.Should().HaveCount(FormationLayouts.SlotCount);
        plan.Slots.Select(slot => slot.SlotNumber).Should().BeInAscendingOrder();
        plan.AssignedCount.Should().Be(0);
        plan.IsComplete.Should().BeFalse();
        plan.Slots[0].PositionFamily.Should().Be("goalkeeper");

        var version = created.Headers.ETag!.Tag;

        var revised = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Away shape", "4-3-3"),
            version);

        revised.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = (await revised.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        updated.Name.Should().Be("Away shape");
        updated.FormationPreset.Should().Be("4-3-3");
        updated.Version.Should().BeGreaterThan(plan.Version);

        // The version the first save carried is now stale, and must not overwrite the change that won.
        var stale = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Loser", "5-3-2"),
            version);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);
        (await CodeAsync(stale)).Should().Be(ApiErrorCodes.PreconditionFailed, "CONC-1");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_save_without_a_version_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Shape"));
        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        var unconditional = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("No header"));

        unconditional.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await CodeAsync(unconditional)).Should().Be(ApiErrorCodes.PreconditionRequired);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_complete_lineup_saves_and_is_reported_complete()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var players = await SquadIdsAsync(client, clubId, WorldRuleSet.TeamSheetStarters);

        var created = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Full eleven", lineup: LineupBody(players)));

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.AssignedCount.Should().Be(WorldRuleSet.TeamSheetStarters);
        plan.IsComplete.Should().BeTrue();
        plan.Slots.Should().OnlyContain(slot => slot.AssignedPlayer != null);
        plan.Slots.Select(slot => slot.AssignedPlayer!.Id).Should().OnlyHaveUniqueItems();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_repeated_player_is_refused_with_issues()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var players = await SquadIdsAsync(client, clubId, WorldRuleSet.TeamSheetStarters);

        // Slot 11 names the player already in slot 10.
        players[10] = players[9];

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Duplicate", lineup: LineupBody(players)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlanValidationFailed);
        (await IssueCodesAsync(response)).Should().Contain("DUPLICATE_PLAYER");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_half_filled_lineup_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var players = await SquadIdsAsync(client, clubId, 5);

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Half a team", lineup: LineupBody(players)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlanValidationFailed);
        (await IssueCodesAsync(response)).Should().Contain("SELECTION_INCOMPLETE", "SQ-4");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Making_a_plan_default_demotes_the_previous_one()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var first = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("First"));
        var firstPlan = (await first.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        var second = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Second", "4-2-3-1"));
        var secondPlan = (await second.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        secondPlan.IsDefault.Should().BeFalse("only one plan is the default (INS-11)");

        var promoted = await SendAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/tactics/{secondPlan.Id}/make-default",
            body: null,
            second.Headers.ETag!.Tag);

        promoted.StatusCode.Should().Be(HttpStatusCode.OK);

        var tactics = await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics");

        tactics!.Plans.Single(plan => plan.Id == secondPlan.Id).IsDefault.Should().BeTrue();
        tactics.Plans.Single(plan => plan.Id == firstPlan.Id).IsDefault.Should().BeFalse();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_unknown_plan_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var missing = Guid.CreateVersion7();
        var save = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{missing}",
            PlanBody("Ghost"),
            "\"1\"");

        save.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(save)).Should().Be(SquadErrorCodes.PlanNotFound, "§15.4: another club's plan is a 404");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_with_no_club_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await client.GetAsync("/api/v1/tactics");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Creates the manager's profile and claims the first club the world offers.</summary>
    private static async Task<Guid> OnboardAsync(HttpClient client)
    {
        var profile = await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        profile.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var countries = await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>("/api/v1/countries");

        foreach (var country in countries!)
        {
            var listing = await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs");

            var available = listing!.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is null)
            {
                continue;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
            {
                Content = JsonContent.Create(new { clubId = available.Id }),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

            var claim = await client.SendAsync(request);

            claim.StatusCode.Should().Be(HttpStatusCode.Created);

            return available.Id;
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    /// <summary>Reads the club's squad and returns the first players' identities.</summary>
    private static async Task<List<Guid>> SquadIdsAsync(HttpClient client, Guid clubId, int take)
    {
        var squad = await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad");

        return [.. squad!.Players.Take(take).Select(player => player.Id)];
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request);
    }

    private static object PlanBody(string name, string preset = "4-4-2", IEnumerable<object>? lineup = null) => new
    {
        name,
        formationPreset = preset,
        mentality = "balanced",
        tempo = "normal",
        passing = "mixed",
        width = "normal",
        pressing = "mid_block",
        defensiveLine = "normal",
        tackling = "normal",
        timeWasting = "off",
        lineup = lineup?.ToArray(),
    };

    private static object[] LineupBody(IEnumerable<Guid> playerIds) =>
        [.. playerIds.Select((playerId, index) => (object)new { slotNumber = index + 1, playerId })];

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Reads the issue codes out of a validation preview.</summary>
    private static async Task<IReadOnlyList<string>> IssueCodesAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (!problem.RootElement.TryGetProperty("validation", out var validation)
            || !validation.TryGetProperty("issues", out var issues))
        {
            return [];
        }

        return [.. issues.EnumerateArray().Select(issue => issue.GetProperty("code").GetString() ?? string.Empty)];
    }
}
