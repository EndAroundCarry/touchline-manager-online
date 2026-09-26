using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Contracts.Match;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The match center reads over HTTP: a played match's summary and its replay (master plan §9.5, §10.5,
/// §11.1).
/// </summary>
/// <remarks>
/// <para>
/// The world is arranged by playing a round through the real workflow — lock, resolve, publish — because
/// there is deliberately no HTTP command that simulates a match (`MAT-2`). Only then does the match center
/// have anything to read, which is the shape of the feature: results exist because the worker produced them,
/// and the viewer only ever reads them.
/// </para>
/// <para>
/// The reads are public game data, so the arrangement and the assertions use an authenticated manager who
/// holds no club: reading a result is not gated on managing one of its two sides.
/// </para>
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class MatchTests : IAsyncLifetime
{
    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public MatchTests(MatchApiFixture fixture) => _fixture = fixture;

    /// <summary>The world is seeded once by the collection's fixture, so there is nothing to arrange here.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Nothing to tear down; the collection's fixture owns the database.</summary>
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_published_match_reads_as_a_summary_with_its_score_and_statistics()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        var summary = (await client.GetFromJsonAsync<MatchResponse>($"/api/v1/matches/{matchId}"))!;

        summary.MatchId.Should().Be(matchId);
        summary.Status.Should().Be("published");
        summary.Home.Goals.Should().Be(summary.Home.Statistics.Goals, "MAT-5: the score equals the goal events");
        summary.Away.Goals.Should().Be(summary.Away.Statistics.Goals);
        summary.RoundNumber.Should().BeGreaterThan(0);
        summary.SeasonLabel.Should().NotBeEmpty();
        summary.EngineVersion.Should().NotBeEmpty();
        summary.PresentationVersion.Should().Be("highlights-v1");
        summary.ServerTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), "TIME-5");

        // Possession is a share, so the two sides' shares are complementary.
        (summary.Home.Statistics.PossessionBasisPoints + summary.Away.Statistics.PossessionBasisPoints)
            .Should().BeInRange(9_900, 10_100);
    }

    [Fact]
    public async Task A_replay_carries_commentary_for_every_narrated_event_and_a_highlight_for_every_goal()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        var presentation = (await client.GetFromJsonAsync<MatchPresentationResponse>(
            $"/api/v1/matches/{matchId}/presentation"))!;

        presentation.MatchId.Should().Be(matchId);
        presentation.PresentationVersion.Should().Be("highlights-v1");
        presentation.Commentary.Should().NotBeEmpty();
        presentation.Commentary.Select(line => line.TemplateKey).Should()
            .Contain(["match.kickoff", "match.full_time"], "a match is narrated from kick-off to full time");
        presentation.Commentary.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.Text));
        presentation.Commentary.Select(line => line.Sequence).Should().BeInAscendingOrder();

        // Every goal the engine recorded is shown, and the goals shown reconcile with the score (MAT-8).
        var goalSequences = await GoalSequencesAsync(matchId);

        goalSequences.Should().NotBeEmpty("a published match almost always has a goal");

        presentation.Highlights.Select(highlight => highlight.SourceEventSequence)
            .Should().Contain(goalSequences, "§9.2: every goal is always shown");

        presentation.Highlights
            .Count(highlight => highlight.OutcomeCode is "goal" or "penalty_goal")
            .Should().Be(presentation.HomeGoals + presentation.AwayGoals, "MAT-5: every goal links to a highlight");

        presentation.Highlights.Should().BeInAscendingOrder(highlight => highlight.SourceEventSequence);
    }

    [Fact]
    public async Task A_highlight_is_a_semantic_keyframe_payload_built_for_interpolation()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        var presentation = (await client.GetFromJsonAsync<MatchPresentationResponse>(
            $"/api/v1/matches/{matchId}/presentation"))!;

        foreach (var highlight in presentation.Highlights)
        {
            highlight.Narration.Should().NotBeEmpty("§9.4: the Canvas is not the only way to follow a highlight");
            highlight.DurationMilliseconds.Should().BeInRange(5_000, 8_000, "§9.3");
            highlight.HomeColour.Should().StartWith("#");
            highlight.AwayColour.Should().StartWith("#");

            // Twenty-two players and a ball, each with a track, so the renderer interpolates rather than
            // being sent frames (§9.1, §9.3).
            highlight.Entities.Should().HaveCount(23);
            highlight.Entities.Should().Contain(entity => entity.IsBall);
            highlight.Entities.Where(entity => !entity.IsBall).Should()
                .OnlyContain(entity =>
                    (entity.Side == "home" || entity.Side == "away") && entity.ParticipantId.HasValue);

            highlight.Tracks.Should().HaveCount(23);
            highlight.Tracks.Select(track => track.EntityId).Should()
                .BeEquivalentTo(highlight.Entities.Select(entity => entity.EntityId));

            foreach (var track in highlight.Tracks)
            {
                track.Keyframes.Should().NotBeEmpty();
                track.Keyframes.Select(keyframe => keyframe.TimeMilliseconds).Should().BeInAscendingOrder();

                track.Keyframes.Should().OnlyContain(
                    keyframe => keyframe.X >= 0 && keyframe.X <= 10_000
                        && keyframe.Y >= 0 && keyframe.Y <= 10_000,
                    "§9.3: coordinates are normalized to the pitch");
            }
        }

        presentation.EstimatedPayloadBytes.Should().BeLessThanOrEqualTo(750 * 1024, "§9.3: the payload budget");
    }

    [Fact]
    public async Task A_replay_is_immutable_and_answers_a_conditional_request_with_not_modified()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();
        var url = $"/api/v1/matches/{matchId}/presentation";

        var first = await client.GetAsync(url);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.ETag.Should().NotBeNull("§9.5: the replay is strongly cached");

        var entityTag = first.Headers.ETag!.Tag;

        first.Headers.CacheControl!.ToString().Should().Contain("immutable");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("If-None-Match", entityTag);

        var second = await client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);

        // A different tag is answered with the body again, so a stale client cannot read an empty 304.
        var stale = new HttpRequestMessage(HttpMethod.Get, url);
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"not-the-current-tag\"");

        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Neither_the_summary_nor_the_replay_carries_a_hidden_value()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        foreach (var url in new[]
        {
            $"/api/v1/matches/{matchId}",
            $"/api/v1/matches/{matchId}/presentation",
        })
        {
            var body = await client.GetStringAsync(url);

            body.Should().NotContain("seed", "MAT-11: the seed never reaches a manager")
                .And.NotContain("qualityBasisPoints", "MAT-11: engine detail stays behind the wire")
                .And.NotContain("inputHash")
                .And.NotContain("outputHash")
                .And.NotContain("snapshot");
        }
    }

    [Fact]
    public async Task An_unpublished_result_is_not_found_and_an_unknown_match_has_a_stable_code()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        // A round that was simulated but not yet published has a match id in the database, and the world must
        // not leak it (MAT-7).
        var staged = await StageNextRoundAsync();

        var hidden = await client.GetAsync($"/api/v1/matches/{staged}");
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var unknown = await client.GetAsync($"/api/v1/matches/{Guid.CreateVersion7()}");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(unknown)).Should().Be(MatchErrorCodes.MatchNotFound);
    }

    [Fact]
    public async Task An_unauthenticated_visitor_is_refused()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/v1/matches/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Plays the next untouched round through the real workflow and returns one of its matches.</summary>
    private async Task<Guid> PublishNextRoundAsync()
    {
        var matchdayId = await NextPendingMatchdayAsync();

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

        var dbContext = read.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchId = await dbContext.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId)
            .OrderBy(fixture => fixture.Id)
            .Select(fixture => fixture.MatchId)
            .FirstAsync();

        return matchId!.Value;
    }

    /// <summary>Simulates the next untouched round without publishing it, and returns one of its matches.</summary>
    private async Task<Guid> StageNextRoundAsync()
    {
        var matchdayId = await NextPendingMatchdayAsync();

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

        await using var read = _fixture.Factory.Services.CreateAsyncScope();

        var dbContext = read.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();
        var matchId = await dbContext.Fixtures
            .Where(fixture => fixture.MatchdayId == matchdayId)
            .OrderBy(fixture => fixture.Id)
            .Select(fixture => fixture.MatchId)
            .FirstAsync();

        return matchId!.Value;
    }

    /// <summary>Finds the first round no fixture of which has been played, so each test plays a fresh one.</summary>
    private async Task<Guid> NextPendingMatchdayAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await dbContext.Matchdays
            .Where(matchday => matchday.PublicationStatus == MatchdayPublicationStatus.Pending
                && !dbContext.Fixtures.Any(fixture =>
                    fixture.MatchdayId == matchday.Id && fixture.Status != FixtureStatus.Scheduled))
            .OrderBy(matchday => matchday.RoundNumber)
            .Select(matchday => matchday.Id)
            .FirstAsync();
    }

    /// <summary>Reads the sequence numbers of the goals the engine recorded, so every one can be traced.</summary>
    private async Task<IReadOnlyList<int>> GoalSequencesAsync(Guid matchId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

        return await dbContext.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId
                && (matchEvent.Type == MatchEventType.Goal || matchEvent.Type == MatchEventType.PenaltyGoal))
            .OrderBy(matchEvent => matchEvent.Sequence)
            .Select(matchEvent => matchEvent.Sequence)
            .ToListAsync();
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
