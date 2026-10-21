using Tactical11Manager.Application.Squad;
using Tactical11Manager.Contracts.World;

namespace Tactical11Manager.Application.World;

/// <summary>The result of reading a club's snack bar and merchandise shop.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Structures">The buildings, when the read succeeded.</param>
public sealed record GetStructuresResult(SquadReadOutcome Outcome, StructuresResponse? Structures);

/// <summary>
/// Reads the snack bar and the merchandise shop of the club the caller manages (`STRC-1`…`STRC-9`).
/// </summary>
/// <remarks>
/// The club is resolved from the tenure rather than named by the request, so a manager can only ever read
/// their own buildings and their own purse: the costs quoted beside them are priced for their tier.
/// </remarks>
public sealed class GetStructures
{
    private readonly ResolveOwnedClub _access;
    private readonly StructuresComposer _composer;

    /// <summary>Initializes the query.</summary>
    public GetStructures(ResolveOwnedClub access, StructuresComposer composer)
    {
        _access = access;
        _composer = composer;
    }

    /// <summary>Reads the buildings, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetStructuresResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetStructuresResult(access.Outcome.ToReadOutcome(), null);
        }

        // A club without a ground, an account or a building is a defect in generation, not an ordinary state;
        // the read reports it as a missing club rather than inventing a building.
        var structures = await _composer.ComposeAsync(access.ClubId, cancellationToken);

        return structures is null
            ? new GetStructuresResult(SquadReadOutcome.ClubNotFound, null)
            : new GetStructuresResult(SquadReadOutcome.Found, structures);
    }
}
