using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The finance module's read projections (master plan §6.8, `FIN-3`, `FIN-7`).
/// </summary>
/// <remarks>
/// Every query here is one round trip shaped for one caller, and none of them loads an aggregate graph.
/// Gate revenue is posted inside a matchday publication, so its basis read is one query for the whole
/// division; the weekly run touches every club, so its obligations read is two queries for the world.
/// </remarks>
internal sealed class FinanceQueries : IFinanceQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public FinanceQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubRevenueBasis>> GetMatchdayRevenueBasisAsync(
        Guid matchdayId,
        CancellationToken cancellationToken)
    {
        var divisionSeasonId = await _dbContext.Matchdays
            .Where(matchday => matchday.Id == matchdayId)
            .Select(matchday => (Guid?)matchday.DivisionSeasonId)
            .FirstOrDefaultAsync(cancellationToken);

        if (divisionSeasonId is null)
        {
            return [];
        }

        var rows = await (
            from entry in _dbContext.ClubSeasonEntries
            join stadium in _dbContext.ClubStadiums on entry.ClubId equals stadium.ClubId
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join fanBase in _dbContext.ClubFanBases on entry.ClubId equals fanBase.ClubId into fanBases
            from fanBase in fanBases.DefaultIfEmpty()
            join snackBar in _dbContext.ClubStructures.Where(building => building.Kind == StructureKind.SnackBar)
                on entry.ClubId equals snackBar.ClubId into snackBars
            from snackBar in snackBars.DefaultIfEmpty()
            where entry.DivisionSeasonId == divisionSeasonId.Value
            select new
            {
                entry.ClubId,
                stadium.StandingSeats,
                stadium.SeatingSeats,
                stadium.CoveredSeats,
                stadium.VipSeats,
                division.TierNumber,
                Fans = (int?)fanBase.Fans,
                MoodBp = (int?)fanBase.MoodBp,
                SnackBarLevel = (int?)snackBar.Level,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // A club whose fan base has not been opened yet (the safety net in the fan keeper covers the same case when
        // the result is applied) is priced as a new club of its tier: the opening fans, calm.
        return
        [
            .. rows.Select(row =>
            {
                var opening = FanRuleSet.OpeningFor(row.TierNumber);

                return new ClubRevenueBasis(
                    row.ClubId,
                    new StadiumSeats(row.StandingSeats, row.SeatingSeats, row.CoveredSeats, row.VipSeats),
                    row.TierNumber,
                    row.Fans ?? opening.Fans,
                    row.MoodBp ?? opening.MoodBp,
                    row.SnackBarLevel ?? StructureRuleSet.OpeningLevel);
            }),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubWeeklyObligations>> GetWeeklyObligationsAsync(
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return [];
        }

        var tiers = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            where divisionSeason.SeasonId == season.SeasonId
            select new { entry.ClubId, division.TierNumber })
            .ToListAsync(cancellationToken);

        var stadiums = await _dbContext.ClubStadiums
            .AsNoTracking()
            .Select(stadium => new
            {
                stadium.ClubId,
                stadium.StandingSeats,
                stadium.SeatingSeats,
                stadium.CoveredSeats,
                stadium.VipSeats,
            })
            .ToListAsync(cancellationToken);

        var seatsByClub = stadiums.ToDictionary(
            row => row.ClubId,
            row => new StadiumSeats(row.StandingSeats, row.SeatingSeats, row.CoveredSeats, row.VipSeats));

        // The merchandise shop sells to the fans as they stand at the close of the week, and the fan review
        // follows the finance run, so this is the week's closing mood (STRC-6). A club whose fan base has not
        // been opened yet sells to a new club's opening fans, calm, as the gate does.
        var fanBases = await _dbContext.ClubFanBases
            .AsNoTracking()
            .Select(fanBase => new { fanBase.ClubId, fanBase.Fans, fanBase.MoodBp })
            .ToListAsync(cancellationToken);

        var fansByClub = fanBases.ToDictionary(row => row.ClubId);

        var buildings = await _dbContext.ClubStructures
            .AsNoTracking()
            .Select(building => new { building.ClubId, building.Kind, building.Level })
            .ToListAsync(cancellationToken);

        var levelByBuilding = buildings.ToDictionary(row => (row.ClubId, row.Kind), row => row.Level);

        return
        [
            .. tiers
                .OrderBy(row => row.ClubId)
                .Select(row =>
                {
                    var opening = FanRuleSet.OpeningFor(row.TierNumber);
                    var hasFans = fansByClub.TryGetValue(row.ClubId, out var fanBase);

                    return new ClubWeeklyObligations(
                        row.ClubId,
                        row.TierNumber,
                        seatsByClub.TryGetValue(row.ClubId, out var seats) ? seats : default,
                        hasFans ? fanBase!.Fans : opening.Fans,
                        hasFans ? fanBase!.MoodBp : opening.MoodBp,
                        levelByBuilding.GetValueOrDefault((row.ClubId, StructureKind.SnackBar), StructureRuleSet.OpeningLevel),
                        levelByBuilding.GetValueOrDefault((row.ClubId, StructureKind.Merchandise), StructureRuleSet.OpeningLevel));
                }),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClubFinanceBasis>> GetClubFinanceBasisAsync(
        IReadOnlyCollection<Guid> clubIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clubIds);

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null || clubIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
            join stadium in _dbContext.ClubStadiums on entry.ClubId equals stadium.ClubId
            where divisionSeason.SeasonId == season.SeasonId && clubIds.Contains(entry.ClubId)
            select new
            {
                entry.ClubId,
                division.TierNumber,
                stadium.StandingSeats,
                stadium.SeatingSeats,
                stadium.CoveredSeats,
                stadium.VipSeats,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var wages = (await _dbContext.PlayerContracts
                .Where(contract => contract.Status == ContractStatus.Active && clubIds.Contains(contract.ClubId))
                .GroupBy(contract => contract.ClubId)
                .Select(group => new
                {
                    ClubId = group.Key,
                    Total = group.Sum(contract => contract.SeasonWageMinor),
                    Count = group.Count(),
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ClubId);

        var fans = (await _dbContext.ClubFanBases
                .AsNoTracking()
                .Where(fanBase => clubIds.Contains(fanBase.ClubId))
                .Select(fanBase => new { fanBase.ClubId, fanBase.Fans, fanBase.MoodBp })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ClubId);

        var levels = (await _dbContext.ClubStructures
                .AsNoTracking()
                .Where(building => clubIds.Contains(building.ClubId))
                .Select(building => new { building.ClubId, building.Kind, building.Level })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => (row.ClubId, row.Kind), row => row.Level);

        var sponsors = (await _dbContext.SponsorDeals
                .AsNoTracking()
                .Where(deal => deal.SeasonId == season.SeasonId && clubIds.Contains(deal.ClubId))
                .Select(deal => new { deal.ClubId, deal.FixedMatchdayMinor })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ClubId, row => row.FixedMatchdayMinor);

        return
        [
            .. rows
                .OrderBy(row => row.ClubId)
                .Select(row =>
                {
                    var opening = FanRuleSet.OpeningFor(row.TierNumber);
                    var hasFans = fans.TryGetValue(row.ClubId, out var fanBase);
                    var hasWages = wages.TryGetValue(row.ClubId, out var wage);

                    return new ClubFinanceBasis(
                        row.ClubId,
                        row.TierNumber,
                        new StadiumSeats(row.StandingSeats, row.SeatingSeats, row.CoveredSeats, row.VipSeats),
                        hasFans ? fanBase!.Fans : opening.Fans,
                        hasFans ? fanBase!.MoodBp : opening.MoodBp,
                        levels.GetValueOrDefault((row.ClubId, StructureKind.SnackBar), StructureRuleSet.OpeningLevel),
                        levels.GetValueOrDefault((row.ClubId, StructureKind.Merchandise), StructureRuleSet.OpeningLevel),
                        sponsors.GetValueOrDefault(row.ClubId),
                        hasWages ? wage!.Total : 0,
                        hasWages ? wage!.Count : 0);
                }),
        ];
    }

    /// <inheritdoc />
    public async Task<ClubMoneyPosition?> GetMoneyPositionAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .AsNoTracking()
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var leadingWages = await (
            from bid in _dbContext.TransferBids
            join listing in _dbContext.TransferListings on bid.ListingId equals listing.Id
            where bid.BidderClubId == clubId && bid.Status == BidStatus.Leading && listing.Status == ListingStatus.Open
            select (long?)listing.GeneratedBuyerSeasonWageMinor)
            .SumAsync(cancellationToken);

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);
        var played = 0;

        if (season is not null)
        {
            played = await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join matchday in _dbContext.Matchdays on divisionSeason.Id equals matchday.DivisionSeasonId
                where entry.ClubId == clubId
                    && divisionSeason.SeasonId == season.SeasonId
                    && matchday.Kind == MatchdayKind.League
                    && matchday.PublicationStatus == MatchdayPublicationStatus.Published
                select (int?)matchday.RoundNumber)
                .MaxAsync(cancellationToken) ?? 0;
        }

        return new ClubMoneyPosition(account.CashMinor, account.ReservedMinor, leadingWages ?? 0, played);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdministrationPlayer>> GetAdministrationSquadAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return [];
        }

        var rows = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            join attributes in _dbContext.PlayerAttributes on player.Id equals attributes.PlayerId
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && _dbContext.PlayerRegistrations.Any(registration =>
                    registration.PlayerId == player.Id && registration.Status == RegistrationStatus.Active)
            select new
            {
                PlayerId = player.Id,
                player.PrimaryPosition,
                player.BirthGameYear,
                contract.SeasonWageMinor,
                Attributes = attributes,
                IsListed = _dbContext.TransferListings.Any(listing =>
                    listing.PlayerId == player.Id && listing.Status == ListingStatus.Open),
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderBy(row => row.PlayerId)
                .Select(row =>
                {
                    var values = row.Attributes.ToSet().Values;
                    var tenths = values.Count == 0
                        ? WorldRuleSet.AttributeMin * 10
                        : (int)Math.Round(values.Average() * 10, MidpointRounding.AwayFromZero);

                    return new AdministrationPlayer(
                        row.PlayerId,
                        PlayerPositions.FamilyOf(row.PrimaryPosition),
                        row.SeasonWageMinor,
                        PlayerValuation.Estimate(new PlayerValuationInput(
                            Math.Clamp(tenths, WorldRuleSet.AttributeMin * 10, WorldRuleSet.AttributeMax * 10),
                            Math.Max(0, season.GameYear - row.BirthGameYear))),
                        row.IsListed);
                }),
        ];
    }

    /// <inheritdoc />
    public async Task<int> GetSettledMatchdayOrdinalAsync(CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return 0;
        }

        var latest = await (
            from matchday in _dbContext.Matchdays
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.SeasonId == season.SeasonId
                && matchday.Kind == MatchdayKind.League
                && matchday.PublicationStatus == MatchdayPublicationStatus.Published
            select (int?)matchday.RoundNumber)
            .MaxAsync(cancellationToken);

        return MatchdayOrdinal.Of(season.SequenceNumber, latest ?? 0);
    }

    /// <inheritdoc />
    public async Task<FinanceSummarySnapshot?> GetFinanceSummaryAsync(
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.CashMinor, candidate.ReservedMinor })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var wage = await _dbContext.PlayerContracts
            .Where(contract => contract.ClubId == clubId && contract.Status == ContractStatus.Active)
            .GroupBy(contract => contract.ClubId)
            .Select(group => new
            {
                Total = group.Sum(contract => contract.SeasonWageMinor),
                Count = group.Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var world = await _dbContext.GameWorlds
            .OrderBy(candidate => candidate.CreatedAt)
            .Select(candidate => new { candidate.Id, candidate.CurrentSeasonNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var placement = world is null
            ? null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                join division in _dbContext.Divisions on divisionSeason.DivisionId equals division.Id
                join season in _dbContext.Seasons on divisionSeason.SeasonId equals season.Id
                where entry.ClubId == clubId
                    && season.WorldId == world.Id
                    && season.SequenceNumber == world.CurrentSeasonNumber
                select new { division.TierNumber, season.StartsAt, season.DisplayLabel, season.SequenceNumber })
                .FirstOrDefaultAsync(cancellationToken);

        // The two squad risks the summary warns about: how many goalkeepers the club holds, and how many
        // contracts run out at the end of the season it is playing (`SQ-2`, `CON-6`).
        var goalkeepers = await (
            from contract in _dbContext.PlayerContracts
            join player in _dbContext.Players on contract.PlayerId equals player.Id
            where contract.ClubId == clubId
                && contract.Status == ContractStatus.Active
                && player.PrimaryPosition == PlayerPosition.Goalkeeper
            select contract.Id)
            .CountAsync(cancellationToken);

        var expiring = placement is null
            ? 0
            : await _dbContext.PlayerContracts.CountAsync(
                contract => contract.ClubId == clubId
                    && contract.Status == ContractStatus.Active
                    && contract.EndSeasonNumber == placement.SequenceNumber,
                cancellationToken);

        // The season's totals are the entries written since the season opened, so the opening balance and any
        // earlier season's moves are outside them. A club with no season yet sees its whole ledger.
        var totalsQuery = _dbContext.LedgerEntries.Where(entry => entry.ClubId == clubId);

        if (placement is not null)
        {
            totalsQuery = totalsQuery.Where(entry => entry.CreatedAt >= placement.StartsAt);
        }

        var totals = await totalsQuery
            .GroupBy(entry => entry.Category)
            .Select(group => new { Category = group.Key, Amount = group.Sum(entry => entry.CashDeltaMinor) })
            .ToListAsync(cancellationToken);

        return new FinanceSummarySnapshot(
            account.CashMinor,
            account.ReservedMinor,
            EconomyRuleSet.PerWeeklyRun(wage?.Total ?? 0),
            wage?.Count ?? 0,
            placement?.TierNumber ?? 1,
            placement?.DisplayLabel,
            goalkeepers,
            expiring,
            [.. totals
                .OrderBy(total => total.Category)
                .Select(total => new FinanceCategoryTotal(total.Category, total.Amount))]);
    }

    /// <inheritdoc />
    public async Task<ClubFinanceStanding?> GetFinanceStandingAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.ClubAccounts
            .AsNoTracking()
            .Where(candidate => candidate.ClubId == clubId)
            .Select(candidate => new { candidate.FinancialState, candidate.StateSinceMatchday })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);
        var divisionSeasonId = season is null
            ? null
            : await (
                from entry in _dbContext.ClubSeasonEntries
                join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
                where entry.ClubId == clubId && divisionSeason.SeasonId == season.SeasonId
                select (Guid?)divisionSeason.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (divisionSeasonId is null)
        {
            return new ClubFinanceStanding(account.FinancialState, account.StateSinceMatchday, null, 0);
        }

        var table = await _dbContext.Standings
            .AsNoTracking()
            .Where(standing => standing.DivisionSeasonId == divisionSeasonId)
            .Select(standing => new { standing.ClubId, standing.Rank })
            .ToListAsync(cancellationToken);

        int? place = table.Where(row => row.ClubId == clubId).Select(row => (int?)row.Rank).FirstOrDefault();

        return new ClubFinanceStanding(account.FinancialState, account.StateSinceMatchday, place, table.Count);
    }

    /// <inheritdoc />
    public async Task<FinanceLedgerPage> GetFinanceLedgerAsync(
        Guid clubId,
        long? beforeSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.LedgerEntries.Where(entry => entry.ClubId == clubId);

        if (beforeSequence is { } before)
        {
            query = query.Where(entry => entry.Sequence < before);
        }

        // One extra row decides whether a next page exists, so the client never has to ask for an empty page
        // just to learn the ledger ended.
        var entries = await query
            .OrderByDescending(entry => entry.Sequence)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = entries.Count > pageSize;

        if (hasMore)
        {
            entries.RemoveAt(entries.Count - 1);
        }

        return new FinanceLedgerPage(entries, hasMore);
    }
}
