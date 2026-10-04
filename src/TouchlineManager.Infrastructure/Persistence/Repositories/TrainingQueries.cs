using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Application.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection queries the training screen and the player's training tab read (master plan §10.4, §11.1;
/// `TRN-1`, `TRN-2`, `TRN-17`).
/// </summary>
/// <remarks>
/// One query for the plan, the club's identity, and the squad with each player's attributes and programme
/// override, so the screen makes one round trip. Positions come back as domain values and state in basis points, because the conversion
/// `TRN-8` requires belongs to the application mapper.
/// </remarks>
internal sealed class TrainingQueries : ITrainingQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public TrainingQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<TrainingSnapshot?> GetTrainingAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var club = await (
            from candidate in _dbContext.Clubs
            join country in _dbContext.Countries on candidate.CountryId equals country.Id
            where candidate.Id == clubId
            select new { Club = candidate, Country = country })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var plan = await _dbContext.TrainingPlans
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new TrainingPlanRow(
                candidate.Intensity,
                candidate.EffectiveDate,
                candidate.Version))
            .FirstOrDefaultAsync(cancellationToken);

        // The active contract is joined rather than left-joined: a player with no contract is not in
        // anybody's squad (`SQ-6`), and there is no development to steer (`TRN-2`).
        var players = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            where contract.ClubId == clubId && contract.Status == ContractStatus.Active
            select new
            {
                Player = player,
                State = state,
                Attributes = attributes,
            })
            .ToListAsync(cancellationToken);

        var playerIds = players.Select(row => row.Player.Id).ToList();

        var focuses = playerIds.Count == 0
            ? new List<PlayerTrainingFocus>()
            : await _dbContext.PlayerTrainingFocuses
                .Where(focus => playerIds.Contains(focus.PlayerId))
                .ToListAsync(cancellationToken);

        var focusByPlayer = focuses.ToDictionary(focus => focus.PlayerId);

        var rows = players
            // Goalkeepers first and then by name, the order a manager reads a squad in.
            .OrderBy(row => (int)row.Player.PrimaryPosition)
            .ThenBy(row => row.Player.FullName, StringComparer.Ordinal)
            .Select(row =>
            {
                var focus = focusByPlayer.GetValueOrDefault(row.Player.Id);

                return new TrainingPlayerRow(
                    row.Player.Id,
                    row.Player.FullName,
                    row.Player.ShortName,
                    row.Player.PrimaryPosition,
                    row.Player.BirthGameYear,
                    new SquadStateRow(
                        row.State.ConditionBp,
                        row.State.FatigueBp,
                        row.State.MoraleBp,
                        row.State.MatchSharpnessBp),
                    row.Attributes.ToSet(),
                    focus?.Programme,
                    focus?.Version);
            })
            .ToList();

        return new TrainingSnapshot(
            clubId,
            club.Club.Name,
            club.Club.ShortName,
            club.Country.Code,
            season.SequenceNumber,
            season.GameYear,
            plan,
            rows);
    }

    /// <inheritdoc />
    public async Task<PlayerTrainingSnapshot?> GetPlayerTrainingAsync(
        Guid playerId,
        int days,
        CancellationToken cancellationToken)
    {
        var player = await (
            from contract in _dbContext.PlayerContracts
            join candidate in _dbContext.Players on contract.PlayerId equals candidate.Id
            where contract.PlayerId == playerId && contract.Status == ContractStatus.Active
            select new { contract.ClubId, candidate.PrimaryPosition })
            .FirstOrDefaultAsync(cancellationToken);

        if (player is null)
        {
            return null;
        }

        var programme = await _dbContext.PlayerTrainingFocuses
            .Where(focus => focus.PlayerId == playerId)
            .Select(focus => (TrainingProgramme?)focus.Programme)
            .FirstOrDefaultAsync(cancellationToken);

        var intensity = await _dbContext.TrainingPlans
            .Where(plan => plan.ClubId == player.ClubId)
            .Select(plan => (TrainingIntensity?)plan.Intensity)
            .FirstOrDefaultAsync(cancellationToken);

        // The newest rows come back first so the window is the most recent days; the caller reads them oldest
        // first, which is the order a chart draws them in.
        var recent = await _dbContext.PlayerTrainingDays
            .AsNoTracking()
            .Where(day => day.PlayerId == playerId)
            .OrderByDescending(day => day.Day)
            .Take(days)
            .ToListAsync(cancellationToken);

        recent.Reverse();

        return new PlayerTrainingSnapshot(
            playerId,
            player.ClubId,
            player.PrimaryPosition,
            programme,
            intensity ?? TrainingMapping.DefaultIntensity,
            [.. recent.Select(day => new PlayerTrainingDayRow(
                day.Day,
                day.Programme,
                day.Intensity,
                day.DevelopmentMilli,
                day.DeclineMilli,
                day.PointsGained,
                day.PointsLost,
                day.ParseChanges()))]);
    }
}
