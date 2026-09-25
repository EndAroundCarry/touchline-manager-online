using Microsoft.EntityFrameworkCore;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Domain.Squad;

namespace TouchlineManager.Infrastructure.Persistence.Repositories;

/// <summary>
/// The squad module's write-side persistence for fixture team sheets.
/// </summary>
/// <remarks>
/// The sheet and its entries load together because neither is meaningful alone, and both are tracked: a
/// replacement selection removes the previous entries and adds the new ones in the same unit of work, which
/// is one <c>SaveChanges</c> and therefore one transaction. Editing entries in place is deliberately not
/// the mechanism — swapping two players between two slots would have to pass through a state that violates
/// one of the sheet's unique indexes.
/// </remarks>
internal sealed class TeamSheetRepository : ITeamSheetRepository
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the repository.</summary>
    public TeamSheetRepository(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public void AddSheet(FixtureTeamSheet sheet) => _dbContext.FixtureTeamSheets.Add(sheet);

    /// <inheritdoc />
    public void AddEntry(TeamSheetEntry entry) => _dbContext.TeamSheetEntries.Add(entry);

    /// <inheritdoc />
    public void RemoveEntries(IReadOnlyList<TeamSheetEntry> entries) =>
        _dbContext.TeamSheetEntries.RemoveRange(entries);

    /// <inheritdoc />
    public async Task<TeamSheetRecord?> FindAsync(
        Guid fixtureId,
        Guid clubId,
        CancellationToken cancellationToken)
    {
        var sheet = await _dbContext.FixtureTeamSheets
            .FirstOrDefaultAsync(
                candidate => candidate.FixtureId == fixtureId && candidate.ClubId == clubId,
                cancellationToken);

        if (sheet is null)
        {
            return null;
        }

        var entries = await _dbContext.TeamSheetEntries
            .Where(entry => entry.TeamSheetId == sheet.Id)
            .OrderBy(entry => entry.SlotNumber)
            .ToListAsync(cancellationToken);

        return new TeamSheetRecord(sheet, entries);
    }
}
