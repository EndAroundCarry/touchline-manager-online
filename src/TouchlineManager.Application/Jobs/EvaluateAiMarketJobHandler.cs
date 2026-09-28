using Microsoft.Extensions.Logging;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Market;

namespace TouchlineManager.Application.Jobs;

/// <summary>
/// Runs the AI transfer market: every club no human holds lists its surplus players and bids within its
/// needs, valuation, and budget (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="EvaluateAiMarket"/>. The job is world-scoped and carries no payload: it
/// considers every club nobody holds, and the use case's deterministic daily keys make a retried pass a
/// replay rather than a second listing or bid.
/// </remarks>
public sealed partial class EvaluateAiMarketJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = AiMarketJobTypes.Evaluate;

    private readonly EvaluateAiMarket _evaluate;
    private readonly ILogger<EvaluateAiMarketJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public EvaluateAiMarketJobHandler(EvaluateAiMarket evaluate, ILogger<EvaluateAiMarketJobHandler> logger)
    {
        _evaluate = evaluate;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobType => TypeName;

    /// <inheritdoc />
    public async Task HandleAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var result = await _evaluate.ExecuteAsync(cancellationToken);

        LogEvaluated(result.Clubs, result.Listed, result.Bids);

        if (result.Skipped > 0)
        {
            // The policy only proposes decisions the shared writers accept, so a skip is a defect worth
            // seeing rather than routine (INS-12).
            LogSkipped(result.Skipped);
        }
    }

    [LoggerMessage(
        EventId = 3700,
        Level = LogLevel.Information,
        Message = "Evaluated {ClubCount} AI club(s) on the market: {Listed} listing(s) and "
            + "{Bids} bid(s) created (TRF-12).")]
    private partial void LogEvaluated(int clubCount, int listed, int bids);

    [LoggerMessage(
        EventId = 3701,
        Level = LogLevel.Warning,
        Message = "{Skipped} AI market decision(s) were refused by the shared writers (TRF-12).")]
    private partial void LogSkipped(int skipped);
}
