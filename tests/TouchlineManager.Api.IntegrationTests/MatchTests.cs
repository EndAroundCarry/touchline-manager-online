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
        summary.PresentationVersion.Should().Be("replay-v13");
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
        presentation.PresentationVersion.Should().Be("replay-v13");
        presentation.Commentary.Should().NotBeEmpty();
        presentation.Commentary.Select(line => line.TemplateKey).Should()
            .Contain(["match.kickoff", "match.full_time"], "a match is narrated from kick-off to full time");
        presentation.Commentary.Should().OnlyContain(line => !string.IsNullOrWhiteSpace(line.Text));
        presentation.Commentary.Select(line => line.Sequence).Should().BeInAscendingOrder();

        // Every goal the engine recorded is shown, and the goals shown reconcile with the score (MAT-8).
        var goalSequences = await GoalSequencesAsync(matchId);
        var goals = presentation.HomeGoals + presentation.AwayGoals;

        // A round can end goalless, so a goal event is only required when the score has one. What must always
        // hold is the reconciliation: every goal the score claims is in the film and on the reel, and no
        // passage claims a goal the score does not (MAT-5, §9.2).
        if (goals > 0)
        {
            goalSequences.Should().NotBeEmpty("a match with goals has a goal event");
        }

        var filmed = presentation.Passages.SelectMany(passage => passage.EventSequences).ToHashSet();

        filmed.Should().Contain(goalSequences, "§9.2: every goal is in the film");

        if (goals > 0)
        {
            presentation.Reel.Should().NotBeEmpty("§9.2: every goal is on the reel");
        }

        foreach (var goalSequence in goalSequences)
        {
            var index = Enumerable.Range(0, presentation.Passages.Count)
                .Single(position => presentation.Passages[position].EventSequences.Contains(goalSequence));

            var start = presentation.Playback![index].StartMilliseconds;
            var end = start + presentation.Playback![index].DurationMilliseconds;

            presentation.Reel
                .Any(clip => clip.StartMilliseconds <= start && clip.EndMilliseconds >= end)
                .Should().BeTrue("§9.2: every goal is on the reel");
            presentation.Passages[index].OutcomeCode
                .Should().BeOneOf("goal", "penalty_goal", "MAT-5: a goal is narrated as one");
        }

        // Each half runs on its own clock (replay-v4): the match second only ever goes forward within a half.
        presentation.Passages.Select(passage => passage.Period).Should().BeInAscendingOrder().And.OnlyContain(period => period == 1 || period == 2);

        foreach (var half in presentation.Passages.GroupBy(passage => passage.Period))
        {
            half.Select(passage => passage.StartMatchSecond).Should().BeInAscendingOrder();
        }

        // The film is one contiguous schedule; the client plays a single list, and the total is the plan's
        // viewing window rather than ninety minutes (replay-v4).
        presentation.Playback.Should().NotBeNull().And.NotBeEmpty();
        presentation.Playback![0].StartMilliseconds.Should().Be(0);
        presentation.Playback.Should().HaveCount(presentation.Passages.Count);

        var cursor = 0;

        foreach (var segment in presentation.Playback)
        {
            segment.Kind.Should().Be("passage");
            segment.StartMilliseconds.Should().Be(cursor);
            segment.DurationMilliseconds.Should().BeGreaterThan(0);
            cursor += segment.DurationMilliseconds;
        }

        cursor.Should().Be(presentation.TotalPlaybackMilliseconds);
        presentation.TotalPlaybackMilliseconds.Should().BeLessThanOrEqualTo(11 * 60 * 1000);
    }

    [Fact]
    public async Task A_passage_is_a_semantic_keyframe_payload_built_for_interpolation()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        var presentation = (await client.GetFromJsonAsync<MatchPresentationResponse>(
            $"/api/v1/matches/{matchId}/presentation"))!;

        presentation.Passages.Should().NotBeEmpty("replay-v4: every match is watchable");

        foreach (var passage in presentation.Passages)
        {
            passage.Narration.Should().NotBeEmpty("§9.4: the Canvas is not the only way to follow a passage");
            passage.DurationMilliseconds.Should().BeGreaterThan(0);
            passage.EndMatchSecond.Should().BeGreaterThanOrEqualTo(passage.StartMatchSecond);
            passage.Period.Should().BeOneOf(1, 2);

            // The match clock keyframes run from the passage's first frame to its last (replay-v4).
            passage.Clock.Should().NotBeNullOrEmpty();
            passage.Clock![0].TimeMilliseconds.Should().Be(0);
            passage.Clock[^1].TimeMilliseconds.Should().Be(passage.DurationMilliseconds);
            passage.Clock.Select(point => point.MatchSecond).Should().BeInAscendingOrder();
            passage.Clock[0].MatchSecond.Should().Be(passage.StartMatchSecond);
            passage.Clock[^1].MatchSecond.Should().Be(passage.EndMatchSecond);
            passage.Cuts.Should().NotBeNull();
            passage.Cuts!.All(cut => cut.TimeMilliseconds == 0 && cut.DurationMilliseconds > 0).Should().BeTrue("a cut is at a passage's first frame");
            passage.HomeColour.Should().StartWith("#");
            passage.AwayColour.Should().StartWith("#");

            // The feed's lines are pinned to the passage's own film clock (§9.3, replay-v4).
            passage.Commentary.Should().NotBeNull();
            passage.Commentary!.Select(line => line.TimeMilliseconds).Should().BeInAscendingOrder();
            passage.Commentary.All(line =>
                line.TimeMilliseconds >= 0
                && line.TimeMilliseconds <= passage.DurationMilliseconds
                && !string.IsNullOrWhiteSpace(line.Text)).Should().BeTrue("a passage may be quiet, but its lines are inside it");

            // The eleven, plus the ball, each with a track, so the renderer interpolates rather than being
            // sent frames (§9.1, §9.3). A sent-off player is not carried, so the count can be below 23.
            passage.Entities.Should().Contain(entity => entity.IsBall);
            passage.Entities.Count(entity => !entity.IsBall).Should().BeInRange(18, 22);
            passage.Entities.Where(entity => !entity.IsBall).Should()
                .OnlyContain(entity =>
                    (entity.Side == "home" || entity.Side == "away") && entity.ParticipantId.HasValue);

            passage.Tracks.Should().HaveCount(passage.Entities.Count);
            passage.Tracks.Select(track => track.EntityId).Should()
                .BeEquivalentTo(passage.Entities.Select(entity => entity.EntityId));

            foreach (var track in passage.Tracks)
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
    public async Task A_replay_carries_both_lineups_and_the_live_condition_and_rating_curve()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var matchId = await PublishNextRoundAsync();

        var presentation = (await client.GetFromJsonAsync<MatchPresentationResponse>(
            $"/api/v1/matches/{matchId}/presentation"))!;

        presentation.HomeLineup.Should().NotBeNull("the match center draws a panel per side (Stage 4)");
        presentation.AwayLineup.Should().NotBeNull();
        presentation.LiveMetrics.Should().NotBeNull().And.NotBeEmpty();

        string[] formations =
        [
            "4-4-2", "4-3-3", "4-2-3-1", "4-1-4-1", "3-5-2", "5-3-2",
            "4-4-1-1", "4-5-1", "4-3-2-1", "4-2-2-2", "3-4-3", "3-4-2-1", "5-4-1",
        ];

        foreach (var lineup in new[] { presentation.HomeLineup!, presentation.AwayLineup! })
        {
            lineup.ClubName.Should().NotBeEmpty();
            lineup.ShortName.Should().NotBeEmpty();
            lineup.PrimaryColour.Should().StartWith("#");
            lineup.SecondaryColour.Should().StartWith("#");
            lineup.Formation.Should().BeOneOf(formations, "a seeded side takes the field in a standard shape");

            lineup.Starters.Should().HaveCount(11);
            lineup.Starters.Select(player => player.SlotNumber).Should().Equal(Enumerable.Range(1, 11));
            lineup.Starters.Should().OnlyContain(player =>
                player.IsStarter
                && !string.IsNullOrWhiteSpace(player.Name)
                && player.Position.Length >= 2
                && player.ShirtNumber > 0);
            lineup.Bench.Should().OnlyContain(player => !player.IsStarter && player.SlotNumber == 0);

            lineup.Starters.Concat(lineup.Bench).Should().OnlyContain(player =>
                player.KickoffCondition > 0
                && player.KickoffCondition <= 10_000
                && player.FinalCondition >= 0
                && player.FinalCondition <= 10_000
                && player.FinalRating >= 0
                && player.FinalRating <= 10_000);
        }

        var players = presentation.HomeLineup!.Starters
            .Concat(presentation.HomeLineup.Bench)
            .Concat(presentation.AwayLineup!.Starters)
            .Concat(presentation.AwayLineup.Bench)
            .ToDictionary(player => player.ParticipantId);

        presentation.LiveMetrics!.Should().OnlyContain(metric =>
            metric.Minute >= 1
            && metric.ConditionBasisPoints > 0
            && metric.ConditionBasisPoints <= 10_000
            && metric.RatingBasisPoints > 0
            && metric.RatingBasisPoints <= 10_000);

        presentation.LiveMetrics!.Select(metric => metric.ParticipantId)
            .Should().BeSubsetOf(players.Keys, "the curve belongs to the players the lineups name");

        // One bar and one badge per player per minute, so a panel never has to choose between two values.
        presentation.LiveMetrics!
            .GroupBy(metric => metric.Minute)
            .Should().OnlyContain(minute =>
                minute.Select(metric => metric.ParticipantId).Distinct().Count() == minute.Count());

        // The curve ends where the panel's bar and badge end: a replay that showed a different final
        // figure from the result would be describing a second match (MAT-8).
        foreach (var player in players.Values.Where(player =>
            player.IsStarter && player.SubbedOutMinute is null && !player.SentOff))
        {
            var last = presentation.LiveMetrics!
                .Where(metric => metric.ParticipantId == player.ParticipantId)
                .OrderBy(metric => metric.Minute)
                .LastOrDefault();

            last.Should().NotBeNull($"{player.Name} played the whole match");
            last!.ConditionBasisPoints.Should().Be(player.FinalCondition);
            last.RatingBasisPoints.Should().Be(player.FinalRating);
        }
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
