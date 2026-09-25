using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Squad;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.Application.Squad;

/// <summary>The result of reading a club's training plan and squad.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Training">The plan and squad, when the read succeeded.</param>
public sealed record GetTrainingResult(SquadReadOutcome Outcome, TrainingResponse? Training);

/// <summary>
/// Reads the training plan and squad of the club the caller manages (master plan §10.4, §11.1; `TRN-1`,
/// `TRN-2`).
/// </summary>
/// <remarks>
/// The subject is the caller's own club, resolved from their tenure rather than named in the request, for
/// the same reason the squad and tactics reads are: a training plan, and who is on an individual focus, are
/// club state. A club that has not set a plan yet still reads: the response says so and carries the
/// implicit defaults the progression job would apply.
/// </remarks>
public sealed class GetTraining
{
    private readonly ResolveOwnedClub _access;
    private readonly ITrainingQueries _queries;
    private readonly IClock _clock;

    /// <summary>Initializes the use case.</summary>
    public GetTraining(ResolveOwnedClub access, ITrainingQueries queries, IClock clock)
    {
        _access = access;
        _queries = queries;
        _clock = clock;
    }

    /// <summary>Reads the training state, or refuses.</summary>
    /// <param name="userId">The authenticated account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<GetTrainingResult> ExecuteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var access = await _access.ExecuteAsync(userId, clubId: null, cancellationToken);

        if (access.Outcome != ClubAccessOutcome.Granted)
        {
            return new GetTrainingResult(access.Outcome.ToReadOutcome(), null);
        }

        var snapshot = await _queries.GetTrainingAsync(access.ClubId, cancellationToken);

        return snapshot is null
            ? new GetTrainingResult(SquadReadOutcome.ClubNotFound, null)
            : new GetTrainingResult(SquadReadOutcome.Found, snapshot.ToResponse(_clock.UtcNow));
    }
}
