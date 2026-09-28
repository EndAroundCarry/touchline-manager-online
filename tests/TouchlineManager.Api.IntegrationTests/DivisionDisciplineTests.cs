using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Competition;
using TouchlineManager.Application.Match;
using TouchlineManager.Contracts.Competition;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Match;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.Api.IntegrationTests;

/// <summary>
/// The division discipline read over HTTP: a played season's cards and outstanding suspensions
/// (master plan §10.5, `DIS-2`…`DIS-5`).
/// </summary>
/// <remarks>
/// Arranged by playing a round through the real workflow and injecting a sending-off and a booking, for the
/// same reason the effects suite does: the engine draws cards from a fixture-derived seed, so a test that
/// waited for one would be testing the engine's luck. The read is public game data, so it is read by an
/// authenticated manager who holds no club.
/// </remarks>
[Collection(MatchApiCollection.Name)]
public sealed class DivisionDisciplineTests
{
    private readonly MatchApiFixture _fixture;

    /// <summary>Initializes the tests.</summary>
    public DivisionDisciplineTests(MatchApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_played_division_reads_the_cards_and_the_suspension_they_cost()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var (divisionId, sentOff, booked) = await PublishRoundWithCardsAsync();

        var discipline = (await client.GetFromJsonAsync<DivisionDisciplineResponse>(
            $"/api/v1/divisions/{divisionId}/discipline"))!;

        discipline.Rows.Should().NotBeEmpty("a played round has shown cards");

        // The injected events are the only ones this test knows the exact counts of, and the arrangement
        // picks players with no prior accumulation, so both rows are unambiguous. The rows are found by
        // player rather than by count, because the shared world has plenty of one-booking players.
        var redRow = discipline.Rows.Single(row => row.PlayerId == sentOff);

        redRow.RedCards.Should().Be(1, "DIS-4: the sending-off is on the record");
        redRow.YellowCards.Should().Be(0, "a straight red is not a booking");
        redRow.SuspensionFixturesRemaining.Should().BeGreaterThanOrEqualTo(
            1,
            "DIS-5: the page shows the fixtures the player still misses");

        var yellowRow = discipline.Rows.Single(row => row.PlayerId == booked);

        yellowRow.YellowCards.Should().Be(1, "DIS-2: the booking is on the record");
        yellowRow.RedCards.Should().Be(0);
        yellowRow.SuspensionFixturesRemaining.Should().Be(0, "one booking is not yet a ban");

        discipline.ServerTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), "TIME-5");

        // The rows arrive ranked by the server — most sendings-off, then most bookings, then name — so the
        // screen sorts nothing.
        for (var index = 1; index < discipline.Rows.Count; index++)
        {
            var previous = discipline.Rows[index - 1];
            var current = discipline.Rows[index];

            (previous.RedCards > current.RedCards
                || (previous.RedCards == current.RedCards && previous.YellowCards > current.YellowCards)
                || (previous.RedCards == current.RedCards
                    && previous.YellowCards == current.YellowCards
                    && string.CompareOrdinal(previous.PlayerName, current.PlayerName) <= 0))
                .Should().BeTrue($"row {index} is ranked after the one before it");
        }
    }

    [Fact]
    public async Task An_unknown_division_answers_with_a_stable_code()
    {
        using var client = _fixture.CreateClient();
        var manager = await AuthScenario.CreateVerifiedManagerAsync(_fixture.Email, client);

        client.WithBearer(manager.AccessToken);

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/discipline");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(response)).Should().Be(CompetitionErrorCodes.DivisionNotFound);
    }

    [Fact]
    public async Task An_unauthenticated_visitor_is_refused()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/v1/divisions/{Guid.CreateVersion7()}/discipline");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Plays the next untouched round with an injected sending-off and booking, and returns the division it
    /// belongs to together with the two players the cards were shown to.
    /// </summary>
    private async Task<(Guid DivisionId, Guid SentOff, Guid Booked)> PublishRoundWithCardsAsync()
    {
        Guid matchdayId;
        Guid divisionSeasonId;
        Guid sentOff;
        Guid booked;

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var matchday = await db.Matchdays
                .Where(candidate => candidate.PublicationStatus == MatchdayPublicationStatus.Pending
                    && !db.Fixtures.Any(fixture => fixture.MatchdayId == candidate.Id
                        && fixture.Status != FixtureStatus.Scheduled))
                .OrderBy(candidate => candidate.RoundNumber)
                .Select(candidate => new { candidate.Id, candidate.DivisionSeasonId })
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
            var db = scope.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>();

            var fixture = await db.Fixtures
                .Where(candidate => candidate.MatchdayId == matchdayId && candidate.Status == FixtureStatus.Staged)
                .OrderBy(candidate => candidate.Id)
                .FirstAsync();

            var matchId = fixture.MatchId!.Value;

            var (first, second) = await PickUnaffectedPlayersAsync(db, divisionSeasonId, fixture.Id, matchId);

            sentOff = first.ParticipantId;
            booked = second.ParticipantId;

            await InjectAsync(db, matchId, first.ClubId, first.ParticipantId, MatchEventType.RedCard);
            await InjectAsync(db, matchId, second.ClubId, second.ParticipantId, MatchEventType.YellowCard);
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var published = await scope.ServiceProvider.GetRequiredService<PublishMatchday>()
                .ExecuteAsync(matchdayId, CancellationToken.None);

            published.Outcome.Should().Be(PublishMatchdayOutcome.Published);
        }

        await using var read = _fixture.Factory.Services.CreateAsyncScope();

        var divisionId = await read.ServiceProvider.GetRequiredService<TouchlineManagerDbContext>()
            .DivisionSeasons
            .Where(candidate => candidate.Id == divisionSeasonId)
            .Select(candidate => candidate.DivisionId)
            .SingleAsync();

        return (divisionId, sentOff, booked);
    }

    /// <summary>Picks two outfield players who enter the round with no accumulation and no card in it.</summary>
    private static async Task<(MatchParticipantV1 First, MatchParticipantV1 Second)> PickUnaffectedPlayersAsync(
        TouchlineManagerDbContext db,
        Guid divisionSeasonId,
        Guid fixtureId,
        Guid matchId)
    {
        var snapshot = await db.InputSnapshots.SingleAsync(candidate => candidate.FixtureId == fixtureId);
        var input = MatchSnapshotDocument.Read(snapshot.SnapshotJson).Input;

        var named = input.Home.Squad.Concat(input.Away.Squad).ToList();

        var affected = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId
                && matchEvent.ParticipantId != null
                && (matchEvent.Type == MatchEventType.YellowCard
                    || matchEvent.Type == MatchEventType.SecondYellowCard
                    || matchEvent.Type == MatchEventType.RedCard
                    || matchEvent.Type == MatchEventType.Injury))
            .Select(matchEvent => matchEvent.ParticipantId!.Value)
            .Distinct()
            .ToListAsync();

        var recorded = await db.DisciplineRecords
            .Where(record => record.DivisionSeasonId == divisionSeasonId)
            .Select(record => record.PlayerId)
            .ToListAsync();

        var candidates = named
            .Where(participant => !participant.IsGoalkeeper
                && !affected.Contains(participant.ParticipantId)
                && !recorded.Contains(participant.ParticipantId))
            .OrderBy(participant => participant.ParticipantId)
            .ToList();

        candidates.Should().HaveCountGreaterThanOrEqualTo(2, "a match leaves most of a squad untouched");

        return (candidates[0], candidates[1]);
    }

    /// <summary>Injects one card event into a simulated match's stream.</summary>
    private static async Task InjectAsync(
        TouchlineManagerDbContext db,
        Guid matchId,
        Guid clubId,
        Guid playerId,
        MatchEventType type)
    {
        var sequence = await db.MatchEvents
            .Where(matchEvent => matchEvent.MatchId == matchId)
            .MaxAsync(matchEvent => matchEvent.Sequence) + 1;

        db.MatchEvents.Add(MatchEvent.Record(
            Guid.CreateVersion7(),
            matchId,
            sequence,
            minute: 90,
            stoppageMinute: 0,
            clubId,
            type,
            participantId: playerId,
            absenceFixtures: null));

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Reads the stable <c>code</c> out of a Problem Details response.</summary>
    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
