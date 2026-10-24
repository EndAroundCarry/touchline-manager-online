using Tactical11Manager.Application.Abstractions.Competition;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Match;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.MatchEngine.Configuration;
using Tactical11Manager.MatchEngine.Model;
using Tactical11Manager.MatchEngine.Shootout;

namespace Tactical11Manager.Application.Competition.Playoffs;

/// <summary>
/// Decides a promotion play-off tie from its published match, and sets up what follows from it (`PR-4`, `PR-6`, ADR-0087).
/// </summary>
/// <remarks>
/// <para>
/// A single match settles a tie. If the 90 minutes end level, a penalty shoot-out outside the match engine settles it
/// (<see cref="PenaltyShootout"/>), run from the match's frozen snapshot and stored lines so the same published match always gives the same
/// shoot-out. A semi-final's winner is then named as the final's away side and the final is scheduled in the higher league's
/// division-season; a final's outcome is read by the rollover (`PR-5`).
/// </para>
/// <para>
/// It stages and never saves: the publication that calls it commits the decision with the result it follows from. Every step is
/// idempotent, because a publication that is repeated returns before it gets here, and a tie that is already decided keeps its winner.
/// </para>
/// </remarks>
public sealed class PlayoffProgression
{
    private readonly IPlayoffRepository _playoffs;
    private readonly IMatchdayRepository _matchdays;
    private readonly ICompetitionRepository _competition;
    private readonly IWorldRepository _world;
    private readonly PlayoffNotifications _notifications;

    /// <summary>Initializes the use case.</summary>
    public PlayoffProgression(
        IPlayoffRepository playoffs,
        IMatchdayRepository matchdays,
        ICompetitionRepository competition,
        IWorldRepository world,
        PlayoffNotifications notifications)
    {
        _playoffs = playoffs;
        _matchdays = matchdays;
        _competition = competition;
        _world = world;
        _notifications = notifications;
    }

    /// <summary>Decides the tie a published play-off match belongs to.</summary>
    /// <param name="workload">The published matchday.</param>
    /// <param name="fixture">The match, published.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How the tie was decided, for the result messages.</returns>
    /// <exception cref="InvalidOperationException">When the match belongs to no tie, which only a defect can cause.</exception>
    public async Task<PlayoffResultFact> DecideAsync(
        MatchdayWorkload workload,
        Fixture fixture,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(fixture);

        var tie = await _playoffs.FindByFixtureAsync(fixture.Id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Fixture {fixture.Id:D} is on a play-off matchday but belongs to no play-off tie (PR-4).");

        var homeGoals = fixture.HomeScore!.Value;
        var awayGoals = fixture.AwayScore!.Value;

        ShootoutResult? shootout = null;

        if (!tie.IsDecided)
        {
            if (homeGoals != awayGoals)
            {
                tie.Decide(
                    homeGoals > awayGoals ? tie.HomeClubId : tie.AwayClubId!.Value,
                    PlayoffDecision.Score,
                    shootoutHomeScore: null,
                    shootoutAwayScore: null,
                    shootoutKicks: null,
                    now);
            }
            else
            {
                shootout = await RunShootoutAsync(workload.Matchday.Id, fixture.Id, cancellationToken);

                tie.Decide(
                    shootout.Winner == MatchSide.Home ? tie.HomeClubId : tie.AwayClubId!.Value,
                    PlayoffDecision.Penalties,
                    shootout.HomeScore,
                    shootout.AwayScore,
                    PlayoffShootoutDocument.Write(shootout),
                    now);
            }
        }

        if (tie.Stage == PlayoffStage.SemiFinal)
        {
            await SetUpFinalAsync(tie, workload.SeasonId, now, cancellationToken);
        }

        return new PlayoffResultFact(
            tie.Stage,
            tie.WinnerClubId!.Value,
            tie.Stage == PlayoffStage.Final ? tie.HomeClubId : Guid.Empty,
            tie.ShootoutHomeScore,
            tie.ShootoutAwayScore);
    }

    private async Task<ShootoutResult> RunShootoutAsync(Guid matchdayId, Guid fixtureId, CancellationToken cancellationToken)
    {
        var row = (await _matchdays.LoadMatchLoadsAsync(matchdayId, cancellationToken))
            .SingleOrDefault(candidate => candidate.FixtureId == fixtureId)
            ?? throw new InvalidOperationException(
                $"Fixture {fixtureId:D} drew but has no stored match to take its shoot-out from (PR-6).");

        var input = MatchSnapshotDocument.Read(row.SnapshotJson).Input;
        var lines = MatchStatisticsDocument.Read(row.StatisticsJson).PlayerLines;

        return PenaltyShootout.Run(
            input.Seed,
            ShootoutSide.FromFullTime(input.Home, lines),
            ShootoutSide.FromFullTime(input.Away, lines),
            WorldRuleSet.ShootoutRegulationKicks,
            EngineRulesV2.Default);
    }

    /// <summary>
    /// Names the semi-final's winner as the final's away side and schedules the final in the higher league (`PR-4`).
    /// </summary>
    /// <remarks>
    /// The final is planned for slot 2. If its team sheets have already locked (the semi-final was published late), it takes the next slot
    /// that has not. When none is left the tie keeps its clubs and no match, so the season stays unsettled and an operator acts.
    /// </remarks>
    private async Task SetUpFinalAsync(PlayoffTie semi, Guid seasonId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var final = (await _playoffs.ListBySeasonAsync(seasonId, cancellationToken))
            .FirstOrDefault(candidate => candidate.Stage == PlayoffStage.Final
                && candidate.ParentDivisionId == semi.ParentDivisionId
                && candidate.FeederDivisionId == semi.FeederDivisionId);

        if (final is null)
        {
            return;
        }

        var winnerIsHome = semi.WinnerClubId == semi.HomeClubId;

        final.FillAway(
            semi.WinnerClubId!.Value,
            winnerIsHome ? semi.HomeDivisionId : semi.AwayDivisionId!.Value,
            winnerIsHome ? semi.HomePlace : semi.AwayPlace!.Value,
            now);

        if (final.FixtureId is not null)
        {
            return;
        }

        var season = await _world.FindSeasonByIdAsync(seasonId, cancellationToken);
        var parentSeason = await _world.FindDivisionSeasonAsync(final.ParentDivisionId, seasonId, cancellationToken);

        if (season is null || parentSeason is null)
        {
            return;
        }

        var kickoffs = SeasonCalendar.PlayoffKickoffs(DateOnly.FromDateTime(season.StartsAt.UtcDateTime));

        var slot = Enumerable
            .Range(final.Slot, WorldRuleSet.PlayoffSlots - final.Slot + 1)
            .FirstOrDefault(candidate => now < PlayoffDraw.LockOf(kickoffs, candidate));

        if (slot == 0)
        {
            return;
        }

        var kickoff = kickoffs[slot - 1];

        var matchday = Matchday.SchedulePlayoff(Guid.CreateVersion7(), parentSeason.Id, slot, kickoff, now);

        var fixture = Fixture.Schedule(
            Guid.CreateVersion7(),
            matchday.Id,
            final.HomeClubId,
            final.AwayClubId!.Value,
            kickoff,
            now);

        final.AttachFixture(fixture.Id, now);

        _competition.AddMatchday(matchday);
        _competition.AddFixture(fixture);

        await _notifications.NotifyFinalSetAsync(final, fixture.Id, kickoff, now, cancellationToken);
    }
}
