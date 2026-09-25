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
/// The squad, player, and contract reads over HTTP, against the real composition root
/// (master plan §10.3, §10.9, §15.4).
/// </summary>
/// <remarks>
/// <para>
/// This is the interface-level half of F-16 and F-17: a manager reads their inherited squad, opens a
/// player, and sees the contract list — while another club's squad, another club's player, and an account
/// with no club each get the specific refusal §15.4's test matrix asks for.
/// </para>
/// <para>
/// The C2 check reads the player response as raw JSON rather than as a deserialized DTO, because the
/// question is what crossed the wire, not what a DTO happens to declare.
/// </para>
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class SquadTests : IAsyncLifetime
{
    private readonly ApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public SquadTests(ApiFixture fixture) => _fixture = fixture;

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
    public async Task A_manager_reads_their_own_squad()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var squad = await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad");

        squad.Should().NotBeNull();
        squad!.ClubId.Should().Be(clubId);
        squad.Players.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget, "SQ-1");

        squad.Summary.PlayerCount.Should().Be(squad.Players.Count);
        squad.Summary.Goalkeepers.Should().BeGreaterThanOrEqualTo(WorldRuleSet.MinimumGoalkeepers, "SQ-2");
        squad.Summary.MeetsMinimum.Should().BeTrue("SQ-2");
        squad.Summary.HasMinimumGoalkeepers.Should().BeTrue("SQ-2");
        squad.Summary.WeeklyWageTotalMinor.Should().BePositive();

        squad.Players.Select(player => player.Id).Should().OnlyHaveUniqueItems();
        squad.Players.Should().OnlyContain(player => player.Age > 0);
        squad.Players.Should().OnlyContain(player => player.Contract != null, "SQ-6");
        squad.Players.Should().OnlyContain(
            player => player.State.Condition >= 0 && player.State.Condition <= 100);
        squad.Players.Should().OnlyContain(
            player => player.State.Fatigue >= 0 && player.State.Fatigue <= 100);

        // Goalkeepers first, which is the order the manager expects to read a squad in.
        squad.Players[0].PrimaryPosition.Should().Be(PlayerPositions.GoalkeeperCode);

        var release = await client.PostAsync("/api/v1/club-tenure/resign", content: null);

        release.StatusCode.Should().Be(HttpStatusCode.OK, "the test gives its club back, as the journeys do");
    }

    [Fact]
    public async Task Another_club_s_squad_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var (_, otherClubId) = await FirstAvailableClubAsync(client);

        otherClubId.Should().NotBe(clubId);

        var response = await client.GetAsync($"/api/v1/clubs/{otherClubId}/squad");

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

        var response = await client.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/squad");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.NoClub);

        var contracts = await client.GetAsync("/api/v1/contracts");

        contracts.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(contracts)).Should().Be(SquadErrorCodes.NoClub);
    }

    [Fact]
    public async Task An_unknown_club_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await client.GetAsync($"/api/v1/clubs/{Guid.CreateVersion7()}/squad");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(WorldErrorCodes.ClubNotFound);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task A_manager_reads_a_player_profile()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var squad = await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad");
        var playerId = squad!.Players[0].Id;

        var response = await client.GetAsync($"/api/v1/players/{playerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var player = (await response.Content.ReadFromJsonAsync<PlayerResponse>())!;

        player.Id.Should().Be(playerId);
        player.ClubId.Should().Be(clubId);
        player.Attributes.Technical.Should().NotBeNull();
        player.Attributes.Mental.Should().NotBeNull();
        player.Attributes.Physical.Should().NotBeNull();
        player.Attributes.Goalkeeping.Should().NotBeNull();
        player.Registration.Should().NotBeNull("SQ-6");

        // Every attribute is on the displayed scale, and the state values are the converted ones (TRN-4,
        // TRN-8).
        AttributeValues(player.Attributes).Should().OnlyContain(value => value >= 1 && value <= 20, "TRN-4");
        player.State.Condition.Should().BeInRange(0, 100);
        player.State.MatchSharpness.Should().BeInRange(0, 100);

        // The hidden values are class C2: absent from the payload that crossed the wire, not merely
        // unmapped by a DTO (data-classification.md §2.1).
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        PropertyNames(payload.RootElement)
            .Should().NotContain(["potential", "reputation", "Potential", "Reputation"]);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task Another_club_s_player_is_refused_by_name()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var (_, otherClubId) = await FirstAvailableClubAsync(client);
        var otherPlayerId = await FirstPlayerOfClubAsync(otherClubId);

        var response = await client.GetAsync($"/api/v1/players/{otherPlayerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.ClubNotManaged, "§15.4: the other-club case");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task An_unknown_player_is_not_found()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        await OnboardAsync(client);

        var response = await client.GetAsync($"/api/v1/players/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(SquadErrorCodes.PlayerNotFound);

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
    }

    [Fact]
    public async Task The_contract_list_carries_the_club_s_payroll()
    {
        using var client = CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);

        var contracts = await client.GetFromJsonAsync<ContractsResponse>("/api/v1/contracts");

        contracts.Should().NotBeNull();
        contracts!.ClubId.Should().Be(clubId);
        contracts.Contracts.Should().HaveCount(WorldRuleSet.GeneratorSquadTarget);
        contracts.WeeklyWageTotalMinor.Should().Be(contracts.Contracts.Sum(contract => contract.WeeklyWageMinor));
        contracts.Contracts.Should().OnlyContain(contract => contract.SeasonsRemaining >= 0);
        contracts.Contracts.Should().OnlyContain(contract => contract.PlayerName.Length > 0);

        // Closest to expiring first, which is the order a manager plans renewals in.
        contracts.Contracts.Select(contract => contract.EndSeasonNumber)
            .Should().BeInAscendingOrder();

        var squad = await client.GetFromJsonAsync<SquadResponse>($"/api/v1/clubs/{clubId}/squad");

        contracts.WeeklyWageTotalMinor.Should().Be(
            squad!.Summary.WeeklyWageTotalMinor,
            "the two reads report the same payroll");

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
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

    /// <summary>
    /// Reads one player identity from a club the caller does not manage, so the other-club case has a real
    /// subject rather than a made-up one.
    /// </summary>
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

    private static IEnumerable<int> AttributeValues(PlayerAttributesResponse attributes) =>
    [
        attributes.Technical.Finishing,
        attributes.Technical.Passing,
        attributes.Technical.Crossing,
        attributes.Technical.Dribbling,
        attributes.Technical.FirstTouch,
        attributes.Technical.Tackling,
        attributes.Technical.Marking,
        attributes.Technical.Heading,
        attributes.Technical.Technique,
        attributes.Technical.SetPieces,
        attributes.Mental.Decisions,
        attributes.Mental.Vision,
        attributes.Mental.Positioning,
        attributes.Mental.Composure,
        attributes.Mental.Anticipation,
        attributes.Mental.WorkRate,
        attributes.Mental.Aggression,
        attributes.Mental.Leadership,
        attributes.Physical.Pace,
        attributes.Physical.Acceleration,
        attributes.Physical.Stamina,
        attributes.Physical.Strength,
        attributes.Physical.Agility,
        attributes.Physical.JumpingReach,
        attributes.Goalkeeping.Handling,
        attributes.Goalkeeping.Reflexes,
        attributes.Goalkeeping.OneOnOnes,
        attributes.Goalkeeping.AerialAbility,
    ];

    /// <summary>Walks a JSON document and yields every property name it contains.</summary>
    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;

                    foreach (var nested in PropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in PropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
