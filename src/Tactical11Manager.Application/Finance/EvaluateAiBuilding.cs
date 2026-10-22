using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Persistence;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Application.World;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

/// <summary>What one AI building evaluation did.</summary>
/// <param name="Clubs">How many computer clubs were considered.</param>
/// <param name="Orders">How many orders for new places were carried out.</param>
/// <param name="Upgrades">How many building upgrades were started.</param>
/// <param name="Skipped">How many orders the shared rules refused. Expected to be zero.</param>
public sealed record EvaluateAiBuildingResult(int Clubs, int Orders, int Upgrades, int Skipped);

/// <summary>
/// Lets every club no human holds grow its ground and upgrade its buildings with the money it earns (`ai-finance-v1`, `STRC-2`, plan toasty-scott
/// section 7).
/// </summary>
/// <remarks>
/// <para>
/// The decisions are made by the pure <see cref="AiFinancePolicy"/> and carried out through the <see cref="ClubBuilding"/> a manager's command
/// uses, so a computer club faces the identical embargo, ceiling and funds rules and its money leaves by the same ledger posting
/// (`INS-12`, `FIN-15`).
/// </para>
/// <para>
/// The crowd is never stored, so the evidence that the ground sold out is the crowd the club's present fans would make against the ground it has,
/// read once the season has played enough home matches to mean something. The whole pass is one transaction in club identity order, so it is
/// reproducible.
/// </para>
/// </remarks>
public sealed class EvaluateAiBuilding
{
    /// <summary>The rounds a season must have published before the ground policy has five home matches of evidence.</summary>
    public const int EvidenceRounds = 2 * AiFinancePolicy.SalesWindow;

    private readonly IAiFinanceQueries _queries;
    private readonly IFinanceQueries _finance;
    private readonly IClubAccountRepository _accounts;
    private readonly IStadiumRepository _stadiums;
    private readonly IStructureRepository _structures;
    private readonly ClubBuilding _building;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;

    /// <summary>Initializes the use case.</summary>
    public EvaluateAiBuilding(
        IAiFinanceQueries queries,
        IFinanceQueries finance,
        IClubAccountRepository accounts,
        IStadiumRepository stadiums,
        IStructureRepository structures,
        ClubBuilding building,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRequestContext requestContext)
    {
        _queries = queries;
        _finance = finance;
        _accounts = accounts;
        _stadiums = stadiums;
        _structures = structures;
        _building = building;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _requestContext = requestContext;
    }

    /// <summary>Evaluates every computer club.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EvaluateAiBuildingResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var money = await _queries.LoadAiClubMoneyAsync(cancellationToken);

        if (money.Count == 0)
        {
            return new EvaluateAiBuildingResult(0, 0, 0, 0);
        }

        var bases = (await _finance.GetClubFinanceBasisAsync([.. money.Select(club => club.ClubId)], cancellationToken))
            .ToDictionary(basis => basis.ClubId);

        var rounds = await _queries.GetRoundsPublishedAsync(cancellationToken);
        var actor = MarketActor.Service(_requestContext.CorrelationId);
        var now = _clock.UtcNow;

        var orders = 0;
        var upgrades = 0;
        var skipped = 0;

        await using var transaction = await _unitOfWork.BeginTransactionAsync(
            TransactionIsolation.ReadCommitted,
            cancellationToken);

        foreach (var club in money.OrderBy(club => club.ClubId))
        {
            if (!bases.TryGetValue(club.ClubId, out var basis))
            {
                continue;
            }

            var input = new AiBuildingInput(
                basis.TierNumber,
                basis.Seats,
                basis.Fans,
                basis.MoodBp,
                RecentSoldFor(basis, rounds),
                club.AvailableMinor,
                basis.SeasonWageMinor,
                club.State != FinancialStateKind.Healthy,
                basis.SnackBarLevel,
                basis.MerchandiseLevel);

            var stands = AiFinancePolicy.ChooseStands(input);
            var wanted = AiFinancePolicy.ChooseUpgrades(input);

            if (stands.Count == 0 && wanted.Count == 0)
            {
                continue;
            }

            var account = await _accounts.FindByClubAsync(club.ClubId, cancellationToken);

            if (account is null)
            {
                continue;
            }

            if (stands.Count > 0 && await _stadiums.FindByClubAsync(club.ClubId, cancellationToken) is { } stadium)
            {
                foreach (var order in stands)
                {
                    if (ClubBuilding.CheckSeats(stadium, account, order.Stand, order.Places, basis.TierNumber) != ClubBuildCheck.Allowed)
                    {
                        skipped++;

                        continue;
                    }

                    _building.StageSeats(stadium, account, order.Stand, order.Places, basis.TierNumber, actor, now);
                    orders++;
                }
            }

            foreach (var upgrade in wanted)
            {
                var building = await _structures.FindByClubAsync(
                    club.ClubId,
                    upgrade.Shop ? StructureKind.Merchandise : StructureKind.SnackBar,
                    cancellationToken);

                if (building is null || ClubBuilding.CheckUpgrade(building, account, basis.TierNumber) != ClubBuildCheck.Allowed)
                {
                    skipped++;

                    continue;
                }

                await _building.StageUpgradeAsync(building, account, basis.TierNumber, actor, now, cancellationToken);
                upgrades++;
            }
        }

        if (orders > 0 || upgrades > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new EvaluateAiBuildingResult(money.Count, orders, upgrades, skipped);
    }

    /// <summary>The share of the ground the last home matches sold, as the club's present fans would fill it, once there are five matches to read.</summary>
    private static IReadOnlyList<int> RecentSoldFor(ClubFinanceBasis basis, int roundsPublished)
    {
        var capacity = basis.Seats.Total;

        if (roundsPublished < EvidenceRounds || capacity <= 0)
        {
            return [];
        }

        var crowd = StadiumRuleSet.AttendanceFor(basis.Seats, basis.Fans, basis.MoodBp);
        var soldBp = (int)Math.Min(EconomyRuleSet.OneHundredPercentBp, (long)crowd * EconomyRuleSet.OneHundredPercentBp / capacity);

        return [.. Enumerable.Repeat(soldBp, AiFinancePolicy.SalesWindow)];
    }
}
