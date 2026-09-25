using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.World;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The fixture reads and the prepare-match team sheet over HTTP, against the real composition root
/// (master plan §10.4, §10.5, §11.1; `SQ-4`, `CAL-3`, `CONC-1`).
/// </summary>
/// <remarks>
/// <para>
/// The journey a manager drives: read the season's calendar, read their own club's fixtures with the next
/// one named, open a fixture, save a side, and then replace it under the sheet's version — with the refusals
/// the contract asks for when the version is missing or stale, and when the selection is not a legal side.
/// </para>
/// <para>
/// The tests are tolerant of a re-used club, like the tactics and training tests: the world is seeded once
/// for the whole collection, so a club may already carry a tactic from an earlier run. That is why the
/// arrangement saves a tactic rather than assuming the club has none.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class CompetitionTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public CompetitionTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_manager_reads_the_seasons_calendar_and_their_own_fixtures()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var my = (await client.GetFromJsonAsync<MyFixturesResponse>("/api/v1/fixtures/mine"))!;

        my.Fixtures.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason, "CAL-1: a club plays 34 fixtures");
        my.NextFixtureId.Should().NotBeNull("a fresh season has a fixture still to play");
        my.Fixtures.Should().OnlyContain(fixture =>
            fixture.HomeScore == null && fixture.AwayScore == null,
            "MAT-7: a score is public only once the whole matchday publishes");

        var next = my.Fixtures.Single(fixture => fixture.Id == my.NextFixtureId);
        next.OpponentName.Should().NotBeEmpty();
        next.Venue.Should().BeOneOf("home", "away");

        var calendar = (await client.GetFromJsonAsync<DivisionFixturesResponse>(
            $"/api/v1/divisions/{my.DivisionId}/fixtures"))!;

        calendar.Clubs.Should().HaveCount(WorldRuleSet.ClubsPerDivision, "WORLD-4");
        calendar.Matchdays.Should().HaveCount(WorldRuleSet.MatchdaysPerSeason, "CAL-1");
        calendar.Matchdays.SelectMany(matchday => matchday.Fixtures).Should()
            .HaveCount(WorldRuleSet.ClubsPerDivision / 2 * WorldRuleSet.MatchdaysPerSeason);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_reads_a_fixture_from_their_own_side()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var my = (await client.GetFromJsonAsync<MyFixturesResponse>("/api/v1/fixtures/mine"))!;

        var detail = (await client.GetFromJsonAsync<FixtureDetailResponse>($"/api/v1/fixtures/{my.NextFixtureId}"))!;

        detail.ManagedClubId.Should().Be(clubId);
        detail.ManagedSide.Should().BeOneOf("home", "away");
        detail.IsLocked.Should().BeFalse("a fixture in the future has not locked yet");
        detail.Home.Name.Should().NotBeEmpty();

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_saves_and_replaces_a_side_under_the_sheets_version()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        // A side is prepared from the club's default plan, so the arrangement provides one.
        var plan = await client.PostAsJsonAsync("/api/v1/tactics", new
        {
            name = "Shape",
            formationPreset = "4-4-2",
            mentality = "balanced",
            tempo = "normal",
            passing = "mixed",
            width = "normal",
            pressing = "mid_block",
            defensiveLine = "normal",
            tackling = "normal",
            timeWasting = "off",
        });

        plan.StatusCode.Should().Be(HttpStatusCode.Created);

        var fixtureId = (await client.GetFromJsonAsync<MyFixturesResponse>("/api/v1/fixtures/mine"))!.NextFixtureId!;

        var sheet = (await client.GetFromJsonAsync<FixtureTeamSheetResponse>(
            $"/api/v1/fixtures/{fixtureId}/team-sheet"))!;

        sheet.PlanId.Should().NotBeNull("INS-11: the side is prepared from the club's default plan");
        sheet.Slots.Should().HaveCount(WorldRuleSet.TeamSheetStarters + WorldRuleSet.TeamSheetSubstitutes);

        var available = sheet.SelectablePlayers.Where(player => !player.IsUnavailable).ToList();
        available.Should().HaveCountGreaterThanOrEqualTo(12, "SQ-2: a legal squad fields a side and a bench");

        // A fresh club has no side yet, so the first save carries no version; a club a previous run left with
        // one carries its current version. Either way the save succeeds, and what follows is the contract.
        var first = Side(available, offset: 0);
        var create = await PutAsync(
            client,
            $"/api/v1/fixtures/{fixtureId}/team-sheet",
            new { selection = first },
            sheet.SheetVersion);

        create.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        create.Headers.ETag.Should().NotBeNull("a saved side exposes its version as an entity tag");

        var saved = (await create.Content.ReadFromJsonAsync<FixtureTeamSheetResponse>())!;

        saved.SheetVersion.Should().NotBeNull();
        saved.Slots.Count(slot => slot.Designation == "starter" && slot.Player is not null)
            .Should().Be(11, "SQ-4");

        // Replacing needs the version: without it the server cannot know it is not overwriting a change.
        var noVersion = await PutAsync(
            client,
            $"/api/v1/fixtures/{fixtureId}/team-sheet",
            new { selection = Side(available, offset: available.Count - 12) },
            null);

        noVersion.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, "CONC-1");
        (await CodeAsync(noVersion)).Should().Be("PRECONDITION_REQUIRED");

        // A stale version is refused rather than overwriting the winner of the race.
        var stale = await PutAsync(
            client,
            $"/api/v1/fixtures/{fixtureId}/team-sheet",
            new { selection = Side(available, offset: 0) },
            (saved.SheetVersion ?? 0) + 100);
        stale.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed, "CONC-1");
        (await CodeAsync(stale)).Should().Be("PRECONDITION_FAILED");

        // The current version lands, and replacing a whole selection is what the delete-then-insert is for.
        var replace = await PutAsync(
            client,
            $"/api/v1/fixtures/{fixtureId}/team-sheet",
            new { selection = Side(available, offset: available.Count - 12) },
            saved.SheetVersion);

        replace.StatusCode.Should().Be(HttpStatusCode.OK);

        var replaced = (await replace.Content.ReadFromJsonAsync<FixtureTeamSheetResponse>())!;

        replaced.SheetVersion.Should().BeGreaterThan(saved.SheetVersion!.Value);
        replaced.Slots.Count(slot => slot.Designation == "starter" && slot.Player is not null)
            .Should().Be(11);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_incomplete_side_is_refused_with_the_validator_issues()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        await client.PostAsJsonAsync("/api/v1/tactics", new
        {
            name = "Shape",
            formationPreset = "4-4-2",
            mentality = "balanced",
            tempo = "normal",
            passing = "mixed",
            width = "normal",
            pressing = "mid_block",
            defensiveLine = "normal",
            tackling = "normal",
            timeWasting = "off",
        });

        var fixtureId = (await client.GetFromJsonAsync<MyFixturesResponse>("/api/v1/fixtures/mine"))!.NextFixtureId!;

        var sheet = (await client.GetFromJsonAsync<FixtureTeamSheetResponse>(
            $"/api/v1/fixtures/{fixtureId}/team-sheet"))!;

        var partial = sheet.SelectablePlayers
            .Where(player => !player.IsUnavailable)
            .Take(3)
            .Select((player, index) => new { slotNumber = index + 1, playerId = player.Id })
            .ToList();

        var response = await PutAsync(
            client,
            $"/api/v1/fixtures/{fixtureId}/team-sheet",
            new { selection = partial },
            sheet.SheetVersion);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(
            CompetitionErrorCodes.TeamSheetValidationFailed,
            "SQ-4: a side short of eleven is refused with the issues, not a bare message");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Another_clubs_fixture_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var (_, otherClubId) = await FirstAvailableClubAsync(client);

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var otherFixtureId = await OtherClubFixtureAsync(otherClubId);

        var response = await client.GetAsync($"/api/v1/fixtures/{otherFixtureId}/team-sheet");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(
            CompetitionErrorCodes.FixtureNotYours,
            "§15.4: the other-club case");

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

        var response = await client.GetAsync("/api/v1/fixtures/mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);
    }

    private HttpClient CreateClient() => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    /// <summary>Builds an eleven-strong starting eleven and a one-man bench from a squad.</summary>
    private static List<object> Side(IReadOnlyList<SelectablePlayerResponse> players, int offset)
    {
        var picked = players.Skip(offset).Take(12).ToList();

        var side = Enumerable.Range(1, 11)
            .Select(slot => (object)new { slotNumber = slot, playerId = picked[slot - 1].Id })
            .ToList();

        side.Add(new { slotNumber = 12, playerId = picked[11].Id });

        return side;
    }

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

    /// <summary>Finds a fixture the given club plays in, so a manager can be refused for it.</summary>
    private async Task<Guid> OtherClubFixtureAsync(Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await dbContext.Fixtures
            .Where(fixture => fixture.HomeClubId == clubId || fixture.AwayClubId == clubId)
            .OrderBy(fixture => fixture.Id)
            .Select(fixture => fixture.Id)
            .FirstAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
