using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tactical11Manager.Application.World;
using Tactical11Manager.Contracts.Http;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Api.IntegrationTests;

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

        tactics.Formations.Should().HaveCount(13, "TAC-1..TAC-6, TAC-11..TAC-17");
        tactics.Formations.Should().OnlyContain(formation => formation.Slots.Count == 11);
        tactics.Formations.Select(formation => formation.Code)
            .Should().BeEquivalentTo(
                [
                    "4-4-2", "4-3-3", "4-2-3-1", "4-1-4-1", "3-5-2", "5-3-2",
                    "4-4-1-1", "4-5-1", "4-3-2-1", "4-2-2-2", "3-4-3", "3-4-2-1", "5-4-1",
                ]);

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
    public async Task A_plan_keeps_its_pass_focus_and_a_client_that_names_none_gets_balanced()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var silent = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Silent"));

        silent.StatusCode.Should().Be(HttpStatusCode.Created);
        (await silent.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.Instructions.PassFocus
            .Should().Be("balanced", "a plan that names no pass focus plays without a preference");

        var wings = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Wide", passFocus: "wings"));

        wings.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = (await wings.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.Instructions.PassFocus.Should().Be("wings");

        var read = await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics");

        read!.Plans.Single(candidate => candidate.Id == plan.Id).Instructions.PassFocus
            .Should().Be("wings", "the setting is stored, not just echoed");

        var revised = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Wide", passFocus: "centre_left"),
            wings.Headers.ETag!.Tag);

        revised.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revised.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.Instructions.PassFocus
            .Should().Be("centre_left");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_plan_keeps_its_counter_attack_and_a_client_that_names_none_plays_without_it()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var silent = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Silent"));

        silent.StatusCode.Should().Be(HttpStatusCode.Created);
        (await silent.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.Instructions.CounterAttack
            .Should().BeFalse("a plan that names no counter-attack does not play on the counter");

        var counter = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Counter", counterAttack: true));

        counter.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = (await counter.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.Instructions.CounterAttack.Should().BeTrue();

        var read = await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics");

        read!.Plans.Single(candidate => candidate.Id == plan.Id).Instructions.CounterAttack
            .Should().BeTrue("the setting is stored, not just echoed");

        var revised = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Counter", counterAttack: false),
            counter.Headers.ETag!.Tag);

        revised.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revised.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.Instructions.CounterAttack.Should().BeFalse();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_unknown_pass_focus_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Odd", passFocus: "sideways"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

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
    public async Task A_bench_of_seven_with_a_goalkeeper_saves_revises_and_reads_back()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);

        var created = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("With a bench", lineup: LineupBody(starters), bench: BenchBody(bench)));

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.Bench.Select(place => place.SlotNumber)
            .Should().Equal(Enumerable.Range(TacticalBenchSlot.FirstSlotNumber, WorldRuleSet.TeamSheetSubstitutes));
        plan.Bench.Select(place => place.AssignedPlayer.Id).Should().Equal(bench);

        // Swapping two places and saving again keeps all seven and moves the version on.
        var swapped = new List<Guid>(bench);

        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);

        var revised = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("With a bench", lineup: LineupBody(starters), bench: BenchBody(swapped)),
            ifMatch: created.Headers.ETag!.Tag);

        revised.StatusCode.Should().Be(HttpStatusCode.OK);

        var revisedPlan = (await revised.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        revisedPlan.Bench.Select(place => place.AssignedPlayer.Id).Should().Equal(swapped);

        // The read side agrees with what the save answered.
        var screen = (await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics"))!;

        screen.Plans.Single(saved => saved.Id == plan.Id).Bench
            .Select(place => place.AssignedPlayer.Id).Should().Equal(swapped);

        // Leaving the bench out empties it.
        var cleared = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("With a bench", lineup: LineupBody(starters)),
            ifMatch: revised.Headers.ETag!.Tag);

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cleared.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.Bench.Should().BeEmpty();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_bench_without_a_goalkeeper_is_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);
        var outfield = await OutfieldIdsAsync(client, clubId);

        // Seven outfield players on the bench, none of them in the lineup.
        var noKeeper = outfield.Where(id => !starters.Contains(id)).Take(WorldRuleSet.TeamSheetSubstitutes).ToList();

        noKeeper.Should().HaveCount(WorldRuleSet.TeamSheetSubstitutes);

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("No keeper", lineup: LineupBody(starters), bench: BenchBody(noKeeper)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlanValidationFailed);
        (await IssueCodesAsync(response)).Should().Contain("BENCH_NEEDS_GOALKEEPER");

        bench.Should().HaveCount(WorldRuleSet.TeamSheetSubstitutes);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_half_filled_bench_and_a_starter_on_the_bench_are_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);

        var half = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Half a bench", lineup: LineupBody(starters), bench: BenchBody(bench.Take(3))));

        half.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IssueCodesAsync(half)).Should().Contain("BENCH_INCOMPLETE");

        var doubled = new List<Guid>(bench) { [0] = starters[3] };

        var both = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Starts and sits", lineup: LineupBody(starters), bench: BenchBody(doubled)));

        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await IssueCodesAsync(both)).Should().Contain("DUPLICATE_PLAYER");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_plan_keeps_its_match_instructions_in_order_through_a_save_a_revision_and_a_read()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);

        var chase = new
        {
            name = "Chase the game",
            fromMinute = 60,
            score = "trailing",
            scoreMargin = 1,
            formation = "4-3-3",
            overrides = new[] { new { playerId = starters[5], slotNumber = 9 } },
            changes = new { mentality = "attacking", pressing = "high_press", counterAttack = false },
            substitutions = new object[]
            {
                new { off = "player", offPlayerId = starters[6], on = "player", onPlayerId = bench[1] },
                new { off = "most_tired_in_family", offFamily = "midfield", on = "best_fit_in_family", onFamily = "attack" },
            },
        };
        var protect = new
        {
            name = "Protect a two-goal lead",
            fromMinute = 70,
            score = "leading",
            scoreMargin = 2,
            changes = new { mentalitySteps = -1, timeWasting = "on" },
        };

        var created = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Plan B", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: [chase, protect]));

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        plan.AssistantFatigueSubstitutions.Should().BeTrue("a plan that says nothing keeps the assistant on");
        plan.MatchInstructions.Select(rule => rule.Name).Should().Equal("Chase the game", "Protect a two-goal lead");

        var first = plan.MatchInstructions[0];

        first.FromMinute.Should().Be(60);
        first.Score.Should().Be("trailing");
        first.Formation.Should().Be("4-3-3");
        first.Overrides.Should().ContainSingle().Which.PlayerId.Should().Be(starters[5]);
        first.Changes.Mentality.Should().Be("attacking");
        first.Changes.Pressing.Should().Be("high_press");
        first.Changes.CounterAttack.Should().BeFalse("turning the counter-attack off is a change");
        first.Changes.Tempo.Should().BeNull("an instruction not named is not changed");
        first.Substitutions.Select(sub => sub.Off).Should().Equal("player", "most_tired_in_family");
        first.Substitutions[1].OnFamily.Should().Be("attack");
        plan.MatchInstructions[1].Changes.MentalitySteps.Should().Be(-1);
        plan.MatchInstructionWarnings.Should().OnlyContain(
            warning => warning.Code == "INSTRUCTION_OUT_OF_POSITION",
            "the override puts the sixth player on the sheet into a slot he may not fit, which depends on the generated squad; nothing else may warn");

        // The read side agrees with what the save answered.
        var screen = (await client.GetFromJsonAsync<TacticsResponse>("/api/v1/tactics"))!;

        screen.Plans.Single(saved => saved.Id == plan.Id).MatchInstructions.Should().BeEquivalentTo(
            plan.MatchInstructions,
            options => options.WithStrictOrdering());

        // Swapping the two moves the order; a request that sends no list keeps it, with the assistant turned off.
        var swapped = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan B", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: [protect, chase]),
            ifMatch: created.Headers.ETag!.Tag);

        swapped.StatusCode.Should().Be(HttpStatusCode.OK);

        var swappedPlan = (await swapped.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        swappedPlan.MatchInstructions.Select(rule => rule.Name).Should().Equal("Protect a two-goal lead", "Chase the game");
        swappedPlan.Version.Should().BeGreaterThan(plan.Version);

        var kept = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan B", lineup: LineupBody(starters), bench: BenchBody(bench), assistantFatigueSubstitutions: false),
            ifMatch: swapped.Headers.ETag!.Tag);

        kept.StatusCode.Should().Be(HttpStatusCode.OK);

        var keptPlan = (await kept.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        keptPlan.MatchInstructions.Select(rule => rule.Name).Should().Equal("Protect a two-goal lead", "Chase the game");
        keptPlan.AssistantFatigueSubstitutions.Should().BeFalse();

        // One instruction deleted, then an empty list clears them all; leaving the assistant out keeps it off.
        var oneLeft = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan B", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: [chase]),
            ifMatch: kept.Headers.ETag!.Tag);

        (await oneLeft.Content.ReadFromJsonAsync<TacticalPlanResponse>())!.MatchInstructions.Select(rule => rule.Name)
            .Should().Equal("Chase the game");

        var cleared = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan B", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: []),
            ifMatch: oneLeft.Headers.ETag!.Tag);

        var clearedPlan = (await cleared.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        clearedPlan.MatchInstructions.Should().BeEmpty();
        clearedPlan.AssistantFatigueSubstitutions.Should().BeFalse();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_stale_version_refuses_a_save_that_edits_the_match_instructions()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var rule = new { name = "Late push", fromMinute = 80, changes = new { mentalitySteps = 1 } };

        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Plan"));
        var plan = (await created.Content.ReadFromJsonAsync<TacticalPlanResponse>())!;

        var first = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan", matchInstructions: [rule]),
            ifMatch: created.Headers.ETag!.Tag);

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await SendAsync(
            client,
            HttpMethod.Put,
            $"/api/v1/tactics/{plan.Id}",
            PlanBody("Plan", matchInstructions: []),
            ifMatch: created.Headers.ETag!.Tag);

        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "the first save moved the version, and the instructions travel with it");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Match_instructions_that_break_the_rules_are_refused_with_the_instruction_they_concern()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);
        var outsider = (await OutfieldIdsAsync(client, clubId)).Except(starters).Except(bench).First();

        var fine = new { name = "Fine", fromMinute = 60, changes = new { mentalitySteps = 1 } };
        var badMinute = new { name = "Too late", fromMinute = 95, changes = new { mentalitySteps = 1 } };
        var strangerOff = new
        {
            name = "Swap a stranger",
            fromMinute = 60,
            substitutions = new[] { new { off = "player", offPlayerId = (Guid?)outsider, on = "player", onPlayerId = (Guid?)bench[1] } },
        };

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Refused", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: [fine, badMinute, strangerOff]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlanValidationFailed);

        var issues = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("validation").GetProperty("issues").EnumerateArray()
            .Select(issue => (Code: issue.GetProperty("code").GetString(), Ordinal: issue.GetProperty("instructionOrdinal").GetInt32()))
            .ToList();

        issues.Should().BeEquivalentTo(
            new[] { ("INSTRUCTION_MINUTE_RANGE", 2), ("INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP", 3) });

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_unknown_code_or_too_many_match_instructions_are_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var odd = new { name = "Odd", score = "winning-ish" };
        var rules = Enumerable.Range(1, 11).Select(index => (object)new { name = $"Rule {index}", changes = new { mentalitySteps = 1 } });

        var unknown = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Odd", matchInstructions: [odd]));
        var tooMany = await SendAsync(client, HttpMethod.Post, "/api/v1/tactics", PlanBody("Many", matchInstructions: rules));

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task More_substitutions_than_a_match_allows_are_refused()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (starters, bench) = await StartersAndBenchAsync(client, clubId);

        object Rule(string name, int from) => new
        {
            name,
            fromMinute = from,
            substitutions = new[]
            {
                new { off = "most_tired_in_family", on = "best_fit_in_family", offFamily = "midfield", onFamily = "midfield" },
                new { off = "most_tired_in_family", on = "best_fit_in_family", offFamily = "defence", onFamily = "defence" },
                new { off = "most_tired_in_family", on = "best_fit_in_family", offFamily = "attack", onFamily = "attack" },
            },
        };

        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/tactics",
            PlanBody("Many changes", lineup: LineupBody(starters), bench: BenchBody(bench), matchInstructions: [Rule("First", 60), Rule("Second", 75)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlanValidationFailed);
        (await IssueCodesAsync(response)).Should().Contain("INSTRUCTIONS_SUBSTITUTION_LIMIT", "the second rule takes the plan from 3 to 6");

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

    /// <summary>
    /// Picks eleven starters led by a goalkeeper and seven substitutes that include another, from the squad.
    /// </summary>
    private static async Task<(List<Guid> Starters, List<Guid> Bench)> StartersAndBenchAsync(
        HttpClient client,
        Guid clubId)
    {
        var squad = (await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad"))!;

        var keepers = squad.Players.Where(player => player.PrimaryPosition == PlayerPositions.GoalkeeperCode)
            .Select(player => player.Id).ToList();
        var outfield = squad.Players.Where(player => player.PrimaryPosition != PlayerPositions.GoalkeeperCode)
            .Select(player => player.Id).ToList();

        keepers.Should().HaveCountGreaterThanOrEqualTo(2, "SQ-2 gives every club two goalkeepers");

        List<Guid> starters = [keepers[0], .. outfield.Take(WorldRuleSet.TeamSheetStarters - 1)];
        List<Guid> bench = [keepers[1], .. outfield.Skip(WorldRuleSet.TeamSheetStarters - 1).Take(WorldRuleSet.TeamSheetSubstitutes - 1)];

        return (starters, bench);
    }

    private static async Task<List<Guid>> OutfieldIdsAsync(HttpClient client, Guid clubId)
    {
        var squad = (await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad"))!;

        return [.. squad.Players.Where(player => player.PrimaryPosition != PlayerPositions.GoalkeeperCode).Select(player => player.Id)];
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

    private static Dictionary<string, object?> PlanBody(
        string name,
        string preset = "4-4-2",
        IEnumerable<object>? lineup = null,
        string? passFocus = null,
        bool? counterAttack = null,
        IEnumerable<object>? bench = null,
        IEnumerable<object>? matchInstructions = null,
        bool? assistantFatigueSubstitutions = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["formationPreset"] = preset,
            ["mentality"] = "balanced",
            ["tempo"] = "normal",
            ["passing"] = "mixed",
            ["width"] = "normal",
            ["pressing"] = "mid_block",
            ["defensiveLine"] = "normal",
            ["tackling"] = "normal",
            ["timeWasting"] = "off",
            ["lineup"] = lineup?.ToArray(),
            ["bench"] = bench?.ToArray(),
        };

        // Left out entirely when not chosen, as a client that predates the setting would send it.
        if (passFocus is not null)
        {
            body["passFocus"] = passFocus;
        }

        if (counterAttack is not null)
        {
            body["counterAttack"] = counterAttack;
        }

        // Left out entirely when not sent: a request without the key keeps the stored instructions and the stored setting.
        if (matchInstructions is not null)
        {
            body["matchInstructions"] = matchInstructions.ToArray();
        }

        if (assistantFatigueSubstitutions is not null)
        {
            body["assistantFatigueSubstitutions"] = assistantFatigueSubstitutions;
        }

        return body;
    }

    private static object[] LineupBody(IEnumerable<Guid> playerIds) =>
        [.. playerIds.Select((playerId, index) => (object)new { slotNumber = index + 1, playerId })];

    /// <summary>The bench's places are numbered after the eleven starters.</summary>
    private static object[] BenchBody(IEnumerable<Guid> playerIds) =>
        [.. playerIds.Select((playerId, index) =>
            (object)new { slotNumber = TacticalBenchSlot.FirstSlotNumber + index, playerId })];

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
