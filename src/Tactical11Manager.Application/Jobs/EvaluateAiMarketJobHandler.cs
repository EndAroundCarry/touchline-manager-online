using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Market;

namespace Tactical11Manager.Application.Jobs;

/// <summary>
/// Runs the AI transfer market: every club no human holds lists its surplus players and bids within its
/// needs, valuation, and budget (`TRF-12`, master plan §7.2).
/// </summary>
/// <remarks>
/// A thin shell over <see cref="EvaluateAiMarket"/> and <see cref="EvaluateAiBuilding"/>: the day's pass of a computer club's money, its players
/// first and then its ground and buildings. The job is world-scoped and carries no payload: it
/// considers every club nobody holds, and the use case's deterministic daily keys make a retried pass a
/// replay rather than a second listing or bid.
/// </remarks>
public sealed partial class EvaluateAiMarketJobHandler : IJobHandler
{
    /// <summary>The job type this handler serves.</summary>
    public const string TypeName = AiMarketJobTypes.Evaluate;

    private readonly EvaluateAiMarket _evaluate;
    private readonly EvaluateAiBuilding _building;
    private readonly ILogger<EvaluateAiMarketJobHandler> _logger;

    /// <summary>Initializes the handler.</summary>
    public EvaluateAiMarketJobHandler(EvaluateAiMarket evaluate, EvaluateAiBuilding building, ILogger<EvaluateAiMarketJobHandler> logger)
    {
        _evaluate = evaluate;
        _building = building;
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

        var building = await _building.ExecuteAsync(cancellationToken);

        LogBuilt(building.Clubs, building.Orders, building.Upgrades);

        if (building.Skipped > 0)
        {
            LogBuildingSkipped(building.Skipped);
        }
    }

    [LoggerMessage(
        EventId = 3702,
        Level = LogLevel.Information,
        Message = "Evaluated {ClubCount} AI club(s) for building: {Orders} order(s) for places and "
            + "{Upgrades} upgrade(s) started (STRC-2).")]
    private partial void LogBuilt(int clubCount, int orders, int upgrades);

    [LoggerMessage(
        EventId = 3703,
        Level = LogLevel.Warning,
        Message = "{Skipped} AI building order(s) were refused by the shared rules; the policy proposes only "
            + "orders they accept (INS-12).")]
    private partial void LogBuildingSkipped(int skipped);

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
