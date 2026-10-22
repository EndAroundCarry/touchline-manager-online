using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Finance;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// Signs the board's sponsor deals when the first week of a season ends (`SPN-4`).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="SignDueSponsorDeals"/>, which is idempotent on the clubs that have not signed. A payload that names no
/// division-season is a defect in the enqueuer, so it is dead-lettered rather than retried.
/// </remarks>
public sealed class SignSponsorDealsJobHandler : IJobHandler
{
    private readonly SignDueSponsorDeals _sign;

    /// <summary>Initializes the handler.</summary>
    public SignSponsorDealsJobHandler(SignDueSponsorDeals sign) => _sign = sign;

    /// <inheritdoc />
    public string JobType => SponsorJobTypes.SignDeals;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!SponsorJobTypes.TryReadPayload(job.PayloadJson, out var divisionSeasonId))
        {
            throw new PermanentJobFailureException($"The sponsor deadline job '{job.Id}' carried no readable division-season (SPN-4).");
        }

        await _sign.ExecuteAsync(divisionSeasonId, cancellationToken);
    }
}
