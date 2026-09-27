using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Comms;
using TouchlineManager.Contracts.World;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The inbox and synchronization surface over HTTP (master plan §10.7, F-41).
/// </summary>
/// <remarks>
/// <para>
/// A round is played through the real workflow — lock, resolve, publish — because there is deliberately no
/// HTTP command that simulates a match (`MAT-2`). That is what puts messages in the inbox: the game tells a
/// manager about what happened, so something has to have happened.
/// </para>
/// <para>
/// The fixture owns a playable world of its own, so claiming a club and playing its round cannot disturb the
/// other API tests' freshly seeded calendar. The manager who reads the inbox holds the club, because the
/// messages are addressed to a person, not a club.
/// </para>
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class InboxTests
{
    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public InboxTests(MatchApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_manager_reads_a_result_and_the_unread_count_falls_as_they_read()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var clubId = await ClaimFirstAvailableClubAsync(client);
        var round = await NextUntouchedRoundForClubAsync(clubId);
        var opponent = await OpponentOfAsync(round, clubId);

        await PublishRoundAsync(round);

        var inbox = (await client.GetFromJsonAsync<InboxResponse>("/api/v1/inbox"))!;

        inbox.UnreadCount.Should().BeGreaterThan(0, "the round told the manager what happened");
        inbox.Messages.Should().NotBeEmpty();

        var result = inbox.Messages.Where(message => message.Category == "result").ToList();

        result.Should().NotBeEmpty("a club that played is told its result");
        result.Should().Contain(
            message => message.Body.Contains(opponent, StringComparison.Ordinal),
            "the result names the opponent");
        result.Should().OnlyContain(message => message.RelatedEntityId != null, "a result links to its match");
        result.Should().OnlyContain(message => !string.IsNullOrWhiteSpace(message.Title));

        var before = (await client.GetFromJsonAsync<SyncResponse>("/api/v1/sync"))!;

        before.UnreadInboxCount.Should().Be(inbox.UnreadCount);

        // Reading one message drops the count by exactly one.
        var unread = inbox.Messages.First(message => !message.IsRead);
        var mark = await client.PostAsJsonAsync($"/api/v1/inbox/{unread.Id}/read", new { });

        mark.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterOne = (await client.GetFromJsonAsync<SyncResponse>("/api/v1/sync"))!;

        afterOne.UnreadInboxCount.Should().Be(before.UnreadInboxCount - 1);

        // Marking everything else read empties it.
        (await client.PostAsJsonAsync("/api/v1/inbox/read-all", new { })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var afterAll = (await client.GetFromJsonAsync<SyncResponse>("/api/v1/sync"))!;

        afterAll.UnreadInboxCount.Should().Be(0);

        var unreadOnly = (await client.GetFromJsonAsync<InboxResponse>("/api/v1/inbox?unread=true"))!;

        unreadOnly.Messages.Should().BeEmpty("the unread filter excludes what was just read");
    }

    [Fact]
    public async Task An_account_with_no_manager_profile_is_refused_the_inbox_but_not_the_summary()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var inbox = await client.GetAsync("/api/v1/inbox");

        inbox.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(inbox)).Should().Be(WorldErrorCodes.ManagerProfileRequired);

        // The poll runs in the background of every screen, so it answers with nothing rather than refusing.
        var sync = await client.GetFromJsonAsync<SyncResponse>("/api/v1/sync");

        sync.Should().NotBeNull();
        sync!.UnreadInboxCount.Should().Be(0);
    }

    [Fact]
    public async Task A_cursor_this_server_did_not_produce_is_refused_by_name()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        await client.PostAsJsonAsync(
            "/api/v1/manager-profile",
            new { locale = "en-GB", timeZone = "Europe/London" });

        var response = await client.GetAsync("/api/v1/inbox?cursor=not-a-cursor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be(CommsErrorCodes.InvalidCursor);
    }

    [Fact]
    public async Task An_unauthenticated_visitor_is_refused()
    {
        using var client = _fixture.CreateClient();

        (await client.GetAsync("/api/v1/inbox")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/sync")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Claims the first club the onboarding reads offer, and returns it.</summary>
    private static async Task<Guid> ClaimFirstAvailableClubAsync(HttpClient client)
    {
        var countries = (await client.GetFromJsonAsync<IReadOnlyList<CountrySummaryResponse>>(
            "/api/v1/countries"))!;

        foreach (var country in countries)
        {
            var listing = (await client.GetFromJsonAsync<AvailableClubsResponse>(
                $"/api/v1/countries/{country.Id}/available-clubs"))!;

            var available = listing.Clubs.FirstOrDefault(club => club.IsAvailable);

            if (available is null)
            {
                continue;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/club-claims")
            {
                Content = JsonContent.Create(new { clubId = available.Id }),
            };

            request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.CreateVersion7().ToString());

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Created);

            return available.Id;
        }

        throw new InvalidOperationException("No club in the world is free; the test arrangement is wrong.");
    }

    /// <summary>Finds the club's next untouched round, so each run claims one and plays it.</summary>
    private async Task<Guid> NextUntouchedRoundForClubAsync(Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await db.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && (fixture.HomeClubId == clubId || fixture.AwayClubId == clubId))
                && !db.Fixtures.Any(fixture => fixture.MatchdayId == matchday.Id
                    && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    /// <summary>Reads the name of the club the given club faced in a round.</summary>
    private async Task<string> OpponentOfAsync(Guid matchdayId, Guid clubId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        var otherClubId = await db.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId
                && (fixture.HomeClubId == clubId || fixture.AwayClubId == clubId))
            .Select(fixture => fixture.HomeClubId == clubId ? fixture.AwayClubId : fixture.HomeClubId)
            .FirstAsync();

        return await db.Clubs
            .Where(club => club.Id == otherClubId)
            .Select(club => club.Name)
            .SingleAsync();
    }

    /// <summary>Plays one round through the real workflow, so its publication writes the messages.</summary>
    private async Task PublishRoundAsync(Guid matchdayId)
    {
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
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
