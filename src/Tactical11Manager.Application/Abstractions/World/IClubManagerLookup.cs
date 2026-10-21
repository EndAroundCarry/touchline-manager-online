namespace Tactical11Manager.Application.Abstractions.World;

/// <summary>The person who runs a club today, as other managers may see them.</summary>
/// <param name="ManagerId">The manager.</param>
/// <param name="DisplayName">The account's display name, which other signed-in managers may see (`COM-11`).</param>
/// <param name="AccountClosing">Whether the account is being deleted or has been anonymized (`COM-11`).</param>
public sealed record ClubManagerRow(Guid ManagerId, string DisplayName, bool AccountClosing);

/// <summary>
/// A narrow, read-only view of who runs a club and whether their account is still open, for the club page and
/// for direct messages (`COM-10` to `COM-12`).
/// </summary>
/// <remarks>
/// It joins the open tenure to the manager and the account, which belong to the world and auth modules, so the
/// comms and squad modules read them through this port and never write them (`MOD-3`). A club with no open
/// tenure is run by the game's own manager, and the lookup returns null for it.
/// </remarks>
public interface IClubManagerLookup
{
    /// <summary>Finds the manager who holds the club's open tenure, or null for an AI-run club.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ClubManagerRow?> FindByClubAsync(Guid clubId, CancellationToken cancellationToken);

    /// <summary>Returns whether the manager's account is being deleted or has been anonymized.</summary>
    /// <param name="managerId">The manager.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsAccountClosingAsync(Guid managerId, CancellationToken cancellationToken);
}
