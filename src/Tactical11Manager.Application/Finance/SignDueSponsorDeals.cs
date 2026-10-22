using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Application.Finance;

/// <summary>
/// At the deadline, signs the board's deal for every club of a division that has not signed: the balanced offer at the realistic objective
/// (`SPN-4`).
/// </summary>
/// <remarks>
/// Each club is signed in its own transaction, so one that cannot be signed does not hold up the others, and a retried job finds only the
/// clubs that are left. The deal pays the matchdays already played at signing, exactly as a manager's does.
/// </remarks>
public sealed partial class SignDueSponsorDeals
{
    private readonly ISponsorQueries _queries;
    private readonly SponsorSigning _signing;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SignDueSponsorDeals> _logger;

    /// <summary>Initializes the use case.</summary>
    public SignDueSponsorDeals(
        ISponsorQueries queries,
        SponsorSigning signing,
        IClock clock,
        IUnitOfWork unitOfWork,
        ILogger<SignDueSponsorDeals> logger)
    {
        _queries = queries;
        _signing = signing;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Signs the clubs of one division-season that have not signed.</summary>
    /// <param name="divisionSeasonId">The division-season.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many clubs the board signed for.</returns>
    public async Task<int> ExecuteAsync(Guid divisionSeasonId, CancellationToken cancellationToken)
    {
        var clubIds = await _queries.ListClubsWithoutDealAsync(divisionSeasonId, cancellationToken);
        var signed = 0;

        foreach (var clubId in clubIds)
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(
                TransactionIsolation.ReadCommitted,
                cancellationToken);

            var result = await _signing.SignAsync(
                clubId,
                kind: null,
                objective: null,
                expectedVersion: null,
                new SponsorActor(SponsorSigner.Board, UserId: null, $"sponsor-deadline:{divisionSeasonId:N}", IpHash: null),
                _clock.UtcNow,
                cancellationToken);

            if (result.Outcome != SponsorSigningOutcome.Agreed)
            {
                await transaction.RollbackAsync(cancellationToken);

                continue;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            signed++;
        }

        LogSigned(divisionSeasonId, signed, clubIds.Count);

        return signed;
    }

    [LoggerMessage(
        EventId = 3620,
        Level = LogLevel.Information,
        Message = "Sponsor deadline of division-season {DivisionSeasonId}: the board signed {Signed} of {Unsigned} unsigned clubs (SPN-4).")]
    private partial void LogSigned(Guid divisionSeasonId, int signed, int unsigned);
}
