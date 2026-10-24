using Microsoft.EntityFrameworkCore;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The computer clubs' money reads (`ai-finance-v1`, plan toasty-scott section 7).
/// </summary>
/// <remarks>
/// "AI-controlled" is the absence of an open tenure, and open spans <c>active</c> and <c>inactive</c> (`OCC-8`): an inactive tenure still occupies
/// its club, so the club's money is not the AI's to spend while a manager may still return.
/// </remarks>
internal sealed class AiFinanceQueries : IAiFinanceQueries
{
    private readonly Tactical11ManagerDbContext _dbContext;

    /// <summary>Initializes the queries.</summary>
    public AiFinanceQueries(Tactical11ManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiClubMoney>> LoadAiClubMoneyAsync(CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return [];
        }

        var rows = await (
            from entry in _dbContext.ClubSeasonEntries
            join divisionSeason in _dbContext.DivisionSeasons on entry.DivisionSeasonId equals divisionSeason.Id
            join club in _dbContext.Clubs on entry.ClubId equals club.Id
            join account in _dbContext.ClubAccounts on club.Id equals account.ClubId
            where divisionSeason.SeasonId == season.SeasonId
                && club.Status == ClubStatus.Active
                && !_dbContext.ClubTenures.Any(tenure => tenure.ClubId == club.Id
                    && tenure.ControlStatus != ClubTenureControlStatus.Closed)
            orderby club.Id
            select new { club.Id, account.CashMinor, account.ReservedMinor, account.FinancialState })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new AiClubMoney(row.Id, row.CashMinor - row.ReservedMinor, row.FinancialState))];
    }

    /// <inheritdoc />
    public async Task<int> GetRoundsPublishedAsync(CancellationToken cancellationToken)
    {
        var season = await CurrentSeasonQuery.ResolveAsync(_dbContext, cancellationToken);

        if (season is null)
        {
            return 0;
        }

        return await (
            from matchday in _dbContext.Matchdays
            join divisionSeason in _dbContext.DivisionSeasons on matchday.DivisionSeasonId equals divisionSeason.Id
            where divisionSeason.SeasonId == season.SeasonId
                && matchday.Kind == MatchdayKind.League
                && matchday.PublicationStatus == MatchdayPublicationStatus.Published
            select (int?)matchday.RoundNumber)
            .MaxAsync(cancellationToken) ?? 0;
    }
}
