using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Contracts.Squad;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The player profile's season line over HTTP: the same projection the division leaderboard reads,
/// narrowed to the player the profile is about (master plan §10.3, §11.1, `STA-2`).
/// </summary>
/// <remarks>
/// Arranged by a manager taking a club and playing its next round through the real workflow, because the
/// profile read is the owning manager's read (the public, unattached profile is a later stage's scouting
/// surface). What it asserts is that the two reads of one projection agree: the leaderboard's row for a
/// player and the profile's line for the same player are the same numbers.
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class PlayerSeasonStatsTests
{
    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public PlayerSeasonStatsTests(MatchApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_manager_s_player_carries_their_own_season_line_after_a_round()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var clubId = await OnboardAsync(client);
        var divisionId = await PlayNextRoundOfClubAsync(clubId);

        var statistics = (await client.GetFromJsonAsync<DivisionStatisticsResponse>(
            $"/api/v1/divisions/{divisionId}/statistics"))!;

        var line = statistics.Rows.First(row => row.ClubId == clubId);

        var player = (await client.GetFromJsonAsync<PlayerResponse>($"/api/v1/players/{line.PlayerId}"))!;

        player.ClubId.Should().Be(clubId);
        player.SeasonStats.Should().NotBeNull("STA-2: a player who appeared has a season line");

        // The profile and the leaderboard read the same stored row, so they cannot disagree.
        player.SeasonStats!.Appearances.Should().Be(line.Appearances);
        player.SeasonStats.Starts.Should().Be(line.Starts);
        player.SeasonStats.MinutesPlayed.Should().Be(line.MinutesPlayed);
        player.SeasonStats.Goals.Should().Be(line.Goals);
        player.SeasonStats.Assists.Should().Be(line.Assists);
        player.SeasonStats.AverageRating.Should().Be(line.AverageRating);
        player.SeasonStats.Appearances.Should().BeGreaterThanOrEqualTo(1);

        if (player.SeasonStats.AverageRating is decimal rating)
        {
            rating.Should().BeInRange(0.0m, 10.0m, "TRN-8: the rating arrives on its display scale");
        }

        await client.PostAsync("/api/v1/club-tenure/resign", content: null);
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

    /// <summary>Plays the managed club's next untouched round and returns the division it belongs to.</summary>
    private async Task<Guid> PlayNextRoundOfClubAsync(Guid clubId)
    {
        Guid matchdayId;
        Guid divisionSeasonId;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var matchday = await (
                from fixture in db.Fixtures
                join candidate in db.Matchdays on fixture.MatchdayId equals candidate.Id
                where (fixture.HomeClubId == clubId || fixture.AwayClubId == clubId)
                    && candidate.PublicationStatus == MatchdayPublicationStatus.Pending
                    && !db.Fixtures.Any(other => other.MatchdayId == candidate.Id
                        && other.Status != FixtureStatus.Scheduled)
                orderby candidate.RoundNumber
                select new { candidate.Id, candidate.DivisionSeasonId })
                .FirstAsync();

            matchdayId = matchday.Id;
            divisionSeasonId = matchday.DivisionSeasonId;
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LockMatchday>()
                .ExecuteAsync(matchdayId, CancellationToken.None);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ResolveMatchday>()
                .ExecuteAsync(matchdayId, jobId: null, CancellationToken.None);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
                .ExecuteAsync(matchdayId, CancellationToken.None);

            published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        }

        await using var read = _fixture.Factory.Services.CreateAsyncScope();

        return await read.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>()
            .DivisionSeasons
            .Where(candidate => candidate.Id == divisionSeasonId)
            .Select(candidate => candidate.DivisionId)
            .SingleAsync();
    }
}
