using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Abstractions.Finance;

/// <summary>
/// Persistence for the per-club season finance summary written at rollover (master plan §6.8).
/// </summary>
/// <remarks>
/// A staging port like the rest of finance: it adds rows to the current unit of work and never saves, so the
/// summary commits with the rollover phase that produced it. It has no update or delete, because a summary of
/// a finished season is a record.
/// </remarks>
public interface IClubSeasonFinanceRepository
{
    /// <summary>Stages a club's season finance summary.</summary>
    /// <param name="summary">The summary.</param>
    void Add(ClubSeasonFinance summary);

    /// <summary>Stages one category total within a summary.</summary>
    /// <param name="line">The line.</param>
    void AddLine(ClubSeasonFinanceLine line);
}
