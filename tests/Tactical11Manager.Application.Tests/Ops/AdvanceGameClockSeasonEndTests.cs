using FluentAssertions;
using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.Ops;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Ops;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Tests.Ops;

/// <summary>
/// The stepped clock's last step (ADR-0049): with no round left, the matchday step is the end of the season, so the toolbar can close the
/// season and open the next instead of going dark.
/// </summary>
public sealed class AdvanceGameClockSeasonEndTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorldId = Guid.CreateVersion7();
    private static readonly Guid SeasonId = Guid.CreateVersion7();

    [Fact]
    public async Task With_no_round_left_the_matchday_step_lands_on_the_season_end_and_queues_the_rollover()
    {
        var season = Season();
        var jobs = new RecordingJobQueue();

        var result = await Advance(Now, [], jobs, season).ExecuteAsync("matchday", CancellationToken.None);

        result.Target.Should().Be(ClockJobPayload.SeasonEnd);
        result.TargetInstantUtc.Should().Be(season.EndsAt);
        result.BusinessKey.Should().Be(ClockJobTypes.AdvanceKey(ClockJobPayload.SeasonEnd, season.EndsAt));

        jobs.Enqueued.Select(job => job.JobType).Should().BeEquivalentTo([ClockJobTypes.Advance, SeasonRolloverJobTypes.Rollover]);

        var rollover = jobs.Enqueued.Single(job => job.JobType == SeasonRolloverJobTypes.Rollover);

        rollover.BusinessKey.Should().Be(SeasonRolloverJobTypes.RolloverKey(SeasonId));
        rollover.DueAt.Should().Be(season.EndsAt, "the rollover runs when the clock reaches the end of the season");
    }

    [Fact]
    public async Task While_a_round_or_a_play_off_tie_is_still_to_be_played_the_matchday_step_is_a_day_and_no_rollover_is_queued()
    {
        var jobs = new RecordingJobQueue { RequeueResult = true };

        var result = await Advance(Now, [], jobs, Season(), settled: false).ExecuteAsync("matchday", CancellationToken.None);

        result.Target.Should().Be(ClockJobPayload.Matchday);
        result.TargetInstantUtc.Should().Be(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
        jobs.Enqueued.Should().ContainSingle().Which.JobType.Should().Be(ClockJobTypes.Advance);
        jobs.Requeued.Should().BeEmpty("the season is not at its end, so there is no rollover to revive");
    }

    [Fact]
    public async Task A_rollover_that_was_dead_lettered_goes_back_to_the_queue_instead_of_a_second_job()
    {
        var season = Season();
        var jobs = new RecordingJobQueue { RequeueResult = true };

        await Advance(season.EndsAt, [], jobs, season).ExecuteAsync("matchday", CancellationToken.None);

        jobs.Requeued.Should().ContainSingle()
            .Which.Should().Be((SeasonRolloverJobTypes.Rollover, SeasonRolloverJobTypes.RolloverKey(SeasonId)));
        jobs.Enqueued.Should().ContainSingle(job => job.JobType == ClockJobTypes.Advance);
    }

    [Fact]
    public async Task The_step_never_goes_back_when_the_clock_is_already_past_the_season_end()
    {
        var season = Season();
        var late = season.EndsAt.AddDays(3);

        var result = await Advance(late, [], new RecordingJobQueue(), season).ExecuteAsync("matchday", CancellationToken.None);

        result.TargetInstantUtc.Should().Be(late);
    }

    [Fact]
    public async Task While_a_round_is_left_the_matchday_step_goes_to_it_and_the_season_is_not_touched()
    {
        var jobs = new RecordingJobQueue();
        var kickoff = Now.AddDays(7);

        var result = await Advance(Now, [kickoff], jobs, Season()).ExecuteAsync("matchday", CancellationToken.None);

        result.Target.Should().Be(ClockJobPayload.Matchday);
        result.TargetInstantUtc.Should().Be(kickoff);
        jobs.Enqueued.Should().ContainSingle().Which.JobType.Should().Be(ClockJobTypes.Advance);
        jobs.Requeued.Should().BeEmpty();
    }

    [Fact]
    public async Task A_day_step_is_a_day_whatever_is_left_of_the_season()
    {
        var jobs = new RecordingJobQueue();

        var result = await Advance(Now, [], jobs, Season()).ExecuteAsync("day", CancellationToken.None);

        result.Target.Should().Be(ClockJobPayload.Day);
        result.TargetInstantUtc.Should().Be(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
        jobs.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task With_no_active_season_the_matchday_step_falls_back_to_a_day()
    {
        var jobs = new RecordingJobQueue();

        var result = await Advance(Now, [], jobs, season: null).ExecuteAsync("matchday", CancellationToken.None);

        result.Target.Should().Be(ClockJobPayload.Matchday);
        result.TargetInstantUtc.Should().Be(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero));
        jobs.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task The_status_names_the_season_end_only_when_no_round_is_left()
    {
        var season = Season();

        var none = await Status(Now, [], season).ExecuteAsync(CancellationToken.None);
        var some = await Status(Now, [Now.AddDays(7)], season).ExecuteAsync(CancellationToken.None);
        var closed = await Status(Now, [], season: null).ExecuteAsync(CancellationToken.None);

        none.NextMatchdayAt.Should().BeNull();
        none.SeasonEndsAt.Should().Be(season.EndsAt);
        some.NextMatchdayAt.Should().Be(Now.AddDays(7));
        some.SeasonEndsAt.Should().BeNull();
        closed.SeasonEndsAt.Should().BeNull("a closed season has nothing left to end");
    }

    private static Season Season()
    {
        var season = Domain.Competition.Season.Create(
            SeasonId,
            WorldId,
            sequenceNumber: 1,
            gameYear: 2026,
            WorldRuleSet.Version,
            new DateOnly(2026, 10, 6),
            Now);

        season.Activate(Now);

        return season;
    }

    private static AdvanceGameClock Advance(
        DateTimeOffset now,
        DateTimeOffset[] kickoffs,
        RecordingJobQueue jobs,
        Season? season,
        bool settled = true) =>
        new(new FixedClockStore(now), new StubMatchdayRepository(kickoffs), jobs, new StubWorldRepository(season), new StubPlayoffRepository(settled));

    private sealed class StubPlayoffRepository(bool settled) : IPlayoffRepository
    {
        public void Add(PlayoffTie tie) => throw new NotSupportedException();

        public Task<IReadOnlyList<PlayoffTie>> ListBySeasonAsync(Guid seasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlayoffTie?> FindByFixtureAsync(Guid fixtureId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CountUndecidedAsync(Guid seasonId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsSeasonSettledAsync(Guid seasonId, CancellationToken cancellationToken) => Task.FromResult(settled);
    }

    private static GetGameClockStatus Status(DateTimeOffset now, DateTimeOffset[] kickoffs, Season? season) =>
        new(new FixedClockStore(now), new StubMatchdayRepository(kickoffs), new StubWorldRepository(season));

    private sealed class FixedClockStore(DateTimeOffset now) : IGameClockStore
    {
        public DateTimeOffset Current => now;

        public Task<DateTimeOffset> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(now);

        public Task WriteAsync(DateTimeOffset gameNow, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingJobQueue : IJobQueue
    {
        public List<JobEnqueueRequest> Enqueued { get; } = [];

        public List<(string JobType, string BusinessKey)> Requeued { get; } = [];

        public bool RequeueResult { get; init; }

        public Task<bool> EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
        {
            Enqueued.Add(request);

            return Task.FromResult(true);
        }

        public Task<bool> RequeueAsync(string jobType, string businessKey, CancellationToken cancellationToken)
        {
            Requeued.Add((jobType, businessKey));

            return Task.FromResult(RequeueResult);
        }

        public Task<JobSnapshot?> FindByIdAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LeasedJob>> ClaimAsync(
            string leaseOwner,
            int maxJobs,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task FailAsync(Guid jobId, string errorMessage, JobFailureKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubMatchdayRepository(DateTimeOffset[] kickoffs) : IMatchdayRepository
    {
        public Task<IReadOnlyList<Matchday>> ListPendingLockingBetweenAsync(
            DateTimeOffset after,
            DateTimeOffset before,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Matchday>>(
            [
                .. kickoffs
                    .Where(kickoff => kickoff > after && kickoff <= before)
                    .Order()
                    .Select((kickoff, index) => Matchday.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), index + 1, kickoff, after)),
            ]);

        public Task<MatchdayWorkload?> LoadMatchdayAsync(Guid matchdayId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Matchday>> LoadDivisionMatchdaysAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FixtureSidesSnapshot?> LoadFixtureSidesAsync(Guid fixtureId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClubSeasonPlacement>> LoadClubPlacementsAsync(
            Guid seasonId,
            IReadOnlyCollection<Guid> clubIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DivisionTableSource?> LoadTableSourceAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Standing>> LoadStandingsAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddStanding(Standing standing) => throw new NotSupportedException();

        public Task<IReadOnlyList<MatchEffectEvent>> LoadMatchEffectsAsync(Guid matchdayId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DisciplineRecord>> LoadDisciplineAsync(
            Guid divisionSeasonId,
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddDisciplineRecord(DisciplineRecord record) => throw new NotSupportedException();

        public Task<IReadOnlyList<MatchStatEvent>> LoadMatchStatEventsAsync(Guid matchdayId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlayerSeasonStat>> LoadPlayerSeasonStatsAsync(
            Guid divisionSeasonId,
            IReadOnlyCollection<Guid> playerIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddPlayerSeasonStat(PlayerSeasonStat stat) => throw new NotSupportedException();

        public Task<IReadOnlyList<FixtureMatchLoadRow>> LoadMatchLoadsAsync(Guid matchdayId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FixtureMatchLoadRow>> LoadDivisionMatchLoadsAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MatchStatEvent>> LoadDivisionStatEventsAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlayerSeasonStat>> LoadDivisionPlayerSeasonStatsAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void RemovePlayerSeasonStat(PlayerSeasonStat stat) => throw new NotSupportedException();
    }

    private sealed class StubWorldRepository(Season? season) : IWorldRepository
    {
        public Task<GameWorld?> FindWorldAsync(CancellationToken cancellationToken) =>
            Task.FromResult<GameWorld?>(GameWorld.Create(WorldId, "Season end", Now));

        public Task<Season?> FindSeasonAsync(Guid worldId, int sequenceNumber, CancellationToken cancellationToken) =>
            Task.FromResult(season is not null && season.WorldId == worldId && season.SequenceNumber == sequenceNumber ? season : null);

        public Task<Season?> FindSeasonByIdAsync(Guid seasonId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddWorld(GameWorld gameWorld) => throw new NotSupportedException();

        public void AddSeason(Season newSeason) => throw new NotSupportedException();

        public Task<IReadOnlyList<Country>> ListCountriesAsync(Guid worldId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Country?> FindCountryAsync(Guid countryId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddCountry(Country country) => throw new NotSupportedException();

        public Task<Division?> FindLowestActiveDivisionAsync(Guid countryId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Division?> FindDivisionAsync(Guid divisionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Division?> FindDivisionByLeagueAsync(
            Guid countryId,
            int tierNumber,
            int leagueNumber,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<Division>> ListDivisionsAsync(Guid countryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddDivision(Division division) => throw new NotSupportedException();

        public Task<DivisionSeason?> FindDivisionSeasonAsync(Guid divisionId, Guid seasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddDivisionSeason(DivisionSeason divisionSeason) => throw new NotSupportedException();

        public void AddClubSeasonEntry(ClubSeasonEntry entry) => throw new NotSupportedException();

        public Task<IReadOnlyList<Division>> ListActiveDivisionsAsync(Guid countryId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DivisionSeason>> ListDivisionSeasonsAsync(Guid seasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClubSeasonEntry>> ListClubSeasonEntriesAsync(Guid divisionSeasonId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
