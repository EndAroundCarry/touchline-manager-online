using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Competition;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The projection queries the squad, player, and contract screens read (master plan §10.3).
/// </summary>
/// <remarks>
/// <para>
/// Every query is one round trip shaped for one screen, or a small number of them, and none loads an
/// aggregate graph. The state rows come back in basis points and the positions as domain values, because
/// the conversion `TRN-8` requires belongs to the response mapping rather than to the query.
/// </para>
/// <para>
/// Secondary positions are a stored code list with a computed accessor on the entity, so the players are
/// projected as entities and read in memory rather than selected field by field. The alternative — a
/// translatable projection — would have to re-parse the list in SQL for no gain.
/// </para>
/// </remarks>
internal sealed class SquadQueries : ISquadQueries
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public SquadQueries(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<SquadSnapshot?> GetSquadAsync(Guid clubId, CancellationToken cancellationToken)
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

        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            where contract.ClubId == clubId && contract.Status == ContractStatus.Active
            orderby player.FullName
            select new { Player = player, Contract = contract, State = state })
            .Take(WorldRuleSet.SquadMaximumRegistered)
            .ToListAsync(cancellationToken);

        var playerIds = rows.Select(row => row.Player.Id).ToList();
        var availability = await OpenAvailabilityAsync(playerIds, cancellationToken);

        var players = rows
            .Select(row => new SquadPlayerRow(
                row.Player.Id,
                row.Player.FullName,
                row.Player.ShortName,
                row.Player.NationalityCode,
                row.Player.BirthGameYear,
                row.Player.PreferredFoot,
                row.Player.PrimaryPosition,
                row.Player.SecondaryPositions,
                new SquadStateRow(
                    row.State.ConditionBp,
                    row.State.FatigueBp,
                    row.State.MoraleBp,
                    row.State.MatchSharpnessBp),
                new SquadContractRow(
                    row.Contract.Id,
                    row.Contract.StartSeasonNumber,
                    row.Contract.EndSeasonNumber,
                    row.Contract.WeeklyWageMinor,
                    row.Contract.SquadStatus,
                    row.Contract.Status),
                availability.GetValueOrDefault(row.Player.Id, [])))
            // Goalkeepers first and then by position, which the stored code cannot express because it orders
            // alphabetically. Twenty-five rows at most, so sorting them here costs nothing.
            .OrderBy(player => (int)player.PrimaryPosition)
            .ThenBy(player => player.FullName, StringComparer.Ordinal)
            .ToList();

        return new SquadSnapshot(
            clubId,
            club.Club.Name,
            club.Club.ShortName,
            club.Country.Code,
            season.SequenceNumber,
            season.GameYear,
            players);
    }

    /// <inheritdoc />
    public async Task<PlayerSnapshot?> GetPlayerAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        // The active contract is joined rather than left-joined: a player with no contract is not in
        // anybody's squad (`SQ-6`), and there is no club to authorize a read against.
        var row = await (
            from player in _dbContext.Players
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            join state in _dbContext.PlayerStates on player.Id equals state.PlayerId
            join contract in _dbContext.PlayerContracts on player.Id equals contract.PlayerId
            where player.Id == playerId && contract.Status == ContractStatus.Active
            select new
            {
                Player = player,
                Attributes = attributes,
                State = state,
                Contract = contract,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var registration = await _dbContext.PlayerRegistrations
            .Where(candidate => candidate.PlayerId == playerId
                && candidate.Status == RegistrationStatus.Active)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Select(candidate => new SquadRegistrationRow(
                candidate.Id,
                candidate.ClubId,
                candidate.Status,
                candidate.EffectiveFixtureBoundaryRound))
            .FirstOrDefaultAsync(cancellationToken);

        var availability = await OpenAvailabilityAsync([playerId], cancellationToken);

        // The player's own season line for this club in the season being played. It is read from the
        // publication's own projection rather than recomputed, so the profile agrees with the division
        // leaderboard, and the season is the world's current one, so a read during the rollover window keeps
        // answering for the season being played (STA-2, CAL-6). A player who has not appeared has none.
        var seasonStat = await (
            from stat in _dbContext.PlayerSeasonStats
            join divisionSeason in _dbContext.DivisionSeasons on stat.DivisionSeasonId equals divisionSeason.Id
            where stat.PlayerId == playerId
                && stat.ClubId == row.Contract.ClubId
                && divisionSeason.SeasonId == season.SeasonId
            select new SquadSeasonStatRow(
                stat.Appearances,
                stat.Starts,
                stat.MinutesPlayed,
                stat.Goals,
                stat.Assists,
                stat.Shots,
                stat.ShotsOnTarget,
                stat.Saves,
                stat.PassesAttempted,
                stat.PassesCompleted,
                stat.DribblesAttempted,
                stat.DribblesCompleted,
                stat.YellowCards,
                stat.RedCards,
                stat.RatedAppearances == 0
                    ? null
                    : (int?)(stat.RatingBasisPointsTotal / stat.RatedAppearances)))
            .FirstOrDefaultAsync(cancellationToken);

        // The player's whole career: every season line they have, newest first, and the totals summed across
        // them. Rows survive rollover, so a career is the same projection the leaderboard reads, aggregated,
        // not a second source of truth; a player who has never appeared has no career rather than a row of
        // zeroes (STA-2). The rating is recomputed from the summed basis points and rated appearances, so a
        // season with more rated games carries the weight it should (TRN-8).
        var careerLines = await (
            from stat in _dbContext.PlayerSeasonStats
            join divisionSeason in _dbContext.DivisionSeasons on stat.DivisionSeasonId equals divisionSeason.Id
            join seasonRow in _dbContext.Seasons on divisionSeason.SeasonId equals seasonRow.Id
            join club in _dbContext.Clubs on stat.ClubId equals club.Id
            where stat.PlayerId == playerId
            orderby seasonRow.SequenceNumber descending
            select new
            {
                seasonRow.SequenceNumber,
                seasonRow.DisplayLabel,
                ClubId = club.Id,
                ClubName = club.Name,
                Stat = stat,
            })
            .ToListAsync(cancellationToken);

        SquadCareer? career = null;

        if (careerLines.Count > 0)
        {
            var seasons = careerLines
                .Select(line => new SquadCareerSeasonRow(
                    line.SequenceNumber,
                    line.DisplayLabel,
                    line.ClubId,
                    line.ClubName,
                    ToSeasonStat(line.Stat)))
                .ToList();

            var ratedAppearances = careerLines.Sum(line => line.Stat.RatedAppearances);
            var ratingBasisPoints = careerLines.Sum(line => line.Stat.RatingBasisPointsTotal);

            var totals = new SquadSeasonStatRow(
                careerLines.Sum(line => line.Stat.Appearances),
                careerLines.Sum(line => line.Stat.Starts),
                careerLines.Sum(line => line.Stat.MinutesPlayed),
                careerLines.Sum(line => line.Stat.Goals),
                careerLines.Sum(line => line.Stat.Assists),
                careerLines.Sum(line => line.Stat.Shots),
                careerLines.Sum(line => line.Stat.ShotsOnTarget),
                careerLines.Sum(line => line.Stat.Saves),
                careerLines.Sum(line => line.Stat.PassesAttempted),
                careerLines.Sum(line => line.Stat.PassesCompleted),
                careerLines.Sum(line => line.Stat.DribblesAttempted),
                careerLines.Sum(line => line.Stat.DribblesCompleted),
                careerLines.Sum(line => line.Stat.YellowCards),
                careerLines.Sum(line => line.Stat.RedCards),
                ratedAppearances == 0 ? null : (int?)(ratingBasisPoints / ratedAppearances));

            career = new SquadCareer(
                totals,
                careerLines.Select(line => line.SequenceNumber).Distinct().Count(),
                seasons);
        }

        return new PlayerSnapshot(
            row.Player.Id,
            row.Contract.ClubId,
            row.Player.FullName,
            row.Player.ShortName,
            row.Player.NationalityCode,
            row.Player.BirthGameYear,
            row.Player.BirthDayOfYear,
            row.Player.PreferredFoot,
            row.Player.HeightCm,
            row.Player.WeightKg,
            row.Player.PrimaryPosition,
            row.Player.SecondaryPositions,
            row.Player.Status,
            row.Attributes.ToSet(),
            new SquadStateRow(
                row.State.ConditionBp,
                row.State.FatigueBp,
                row.State.MoraleBp,
                row.State.MatchSharpnessBp),
            new SquadContractRow(
                row.Contract.Id,
                row.Contract.StartSeasonNumber,
                row.Contract.EndSeasonNumber,
                row.Contract.WeeklyWageMinor,
                row.Contract.SquadStatus,
                row.Contract.Status),
            registration,
            availability.GetValueOrDefault(playerId, []),
            seasonStat,
            career,
            season.SequenceNumber,
            season.GameYear);
    }

    /// <summary>Projects one stored season line into the read shape, converting the rating's storage unit.</summary>
    private static SquadSeasonStatRow ToSeasonStat(PlayerSeasonStat stat) =>
        new(
            stat.Appearances,
            stat.Starts,
            stat.MinutesPlayed,
            stat.Goals,
            stat.Assists,
            stat.Shots,
            stat.ShotsOnTarget,
            stat.Saves,
            stat.PassesAttempted,
            stat.PassesCompleted,
            stat.DribblesAttempted,
            stat.DribblesCompleted,
            stat.YellowCards,
            stat.RedCards,
            stat.RatedAppearances == 0
                ? null
                : (int?)(stat.RatingBasisPointsTotal / stat.RatedAppearances));

    /// <inheritdoc />
    public async Task<ContractsSnapshot?> GetContractsAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var club = await _dbContext.Clubs
            .Where(candidate => candidate.Id == clubId)
            .Select(candidate => new { candidate.Id, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            return null;
        }

        var contracts = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            where contract.ClubId == clubId && contract.Status == ContractStatus.Active
            // Closest to expiring first, which is the order a manager plans renewals in.
            orderby contract.EndSeasonNumber, player.FullName
            select new ContractRow(
                contract.Id,
                player.Id,
                player.FullName,
                player.ShortName,
                player.PrimaryPosition,
                player.BirthGameYear,
                contract.StartSeasonNumber,
                contract.EndSeasonNumber,
                contract.WeeklyWageMinor,
                contract.SquadStatus,
                contract.Status))
            .ToListAsync(cancellationToken);

        return new ContractsSnapshot(
            clubId,
            club.Name,
            season.SequenceNumber,
            season.GameYear,
            contracts);
    }

    /// <inheritdoc />
    public async Task<ContractRenewalContext?> GetRenewalContextAsync(
        Guid contractId,
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return null;
        }

        var contract = await _dbContext.PlayerContracts
            .Where(candidate => candidate.Id == contractId && candidate.Status == ContractStatus.Active)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.PlayerId,
                candidate.ClubId,
                candidate.Version,
                candidate.SquadStatus,
                candidate.EndSeasonNumber,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (contract is null)
        {
            return null;
        }

        var player = await _dbContext.Players
            .Where(candidate => candidate.Id == contract.PlayerId)
            .Select(candidate => new { candidate.BirthGameYear, candidate.Potential })
            .FirstOrDefaultAsync(cancellationToken);

        // Read as the entity rather than projected: the attribute set's values are a computed accessor over
        // the row's columns, so the mean is taken in memory rather than re-parsed in SQL.
        var attributes = await _dbContext.PlayerAttributes
            .FirstOrDefaultAsync(candidate => candidate.PlayerId == contract.PlayerId, cancellationToken);

        if (player is null || attributes is null)
        {
            return null;
        }

        var moraleBp = await _dbContext.PlayerStates
            .Where(candidate => candidate.PlayerId == contract.PlayerId)
            .Select(candidate => (int?)candidate.MoraleBp)
            .FirstOrDefaultAsync(cancellationToken);

        var placement = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where entry.ClubId == contract.ClubId && divisionSeason.SeasonId == season.SeasonId
            select new { divisionSeason.Id, division.TierNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (placement is null)
        {
            return null;
        }

        var appearances = await _dbContext.PlayerSeasonStats
            .Where(stat => stat.DivisionSeasonId == placement.Id
                && stat.PlayerId == contract.PlayerId
                && stat.ClubId == contract.ClubId)
            .Select(stat => (int?)stat.Appearances)
            .FirstOrDefaultAsync(cancellationToken);

        var values = attributes.ToSet().Values;

        // The mean of the twenty-eight attributes: the same ability signal the AI policy uses, and no
        // authority beyond what it needs to be (it is an input to a wage, not a published "overall").
        var ability = values.Count == 0
            ? WorldRuleSet.AttributeMin
            : (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);

        return new ContractRenewalContext(
            contract.Id,
            contract.PlayerId,
            contract.ClubId,
            contract.Version,
            contract.SquadStatus,
            ability,
            player.Potential,
            season.GameYear - player.BirthGameYear,
            appearances ?? 0,
            moraleBp ?? 0,
            placement.TierNumber,
            season.SequenceNumber,
            Math.Max(0, contract.EndSeasonNumber - season.SequenceNumber));
    }

    /// <summary>Reads every open injury and suspension for the given players, keyed by player.</summary>
    /// <remarks>
    /// A player can hold more than one open record — an injury and a suspension overlap — so the result is
    /// a list per player rather than a single record, and the screen renders however many there are.
    /// </remarks>
    private async Task<Dictionary<Guid, IReadOnlyList<SquadAvailabilityRow>>> OpenAvailabilityAsync(
        List<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        if (playerIds.Count == 0)
        {
            return [];
        }

        var records = await _dbContext.PlayerUnavailabilities
            .Where(record => playerIds.Contains(record.PlayerId) && record.ResolvedAt == null)
            .OrderBy(record => record.RemainingFixtures)
            .Select(record => new
            {
                record.PlayerId,
                Row = new SquadAvailabilityRow(
                    record.Id,
                    record.Type,
                    record.Severity,
                    record.RemainingFixtures,
                    record.StartedAt),
            })
            .ToListAsync(cancellationToken);

        return records
            .GroupBy(record => record.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SquadAvailabilityRow>)[.. group.Select(record => record.Row)]);
    }
}
