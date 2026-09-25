using TouchlineManager.Domain.Finance;

namespace TouchlineManager.Application.Abstractions.Finance;

/// <summary>
/// Persistence for club money.
/// </summary>
/// <remarks>
/// <para>
/// Stage 3 only opens accounts, during world generation. The ledger, wages, reservations, and
/// compensating entries arrive in Stage 9, which is why there is no move-making method here yet: a
/// balance change that skipped the append-only ledger would be a hole in the one rule the finance module
/// exists to enforce (`FIN-12`).
/// </para>
/// <para>
/// This port lives in the finance namespace even though Stage 3 writes it, because the module that owns
/// <c>finance.club_accounts</c> owns every write to it (`MOD-1`).
/// </para>
/// </remarks>
public interface IClubAccountRepository
{
    /// <summary>Opens an account for a newly generated club.</summary>
    void Add(ClubAccount account);

    /// <summary>Finds a club's account, or null when none has been opened.</summary>
    Task<ClubAccount?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken);
}
