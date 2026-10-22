using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Jobs;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Application.Jobs;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What the building rules say about an order (`DEBT-3`, `FIN-10`, `STAD-1`, `STRC-3`).</summary>
public enum ClubBuildCheck
{
    /// <summary>The order may go ahead.</summary>
    Allowed = 0,

    /// <summary>The club is in the red or in administration, so it may not build (`DEBT-3`).</summary>
    Embargo = 1,

    /// <summary>The order would take the ground past its largest size (`STAD-1`).</summary>
    StadiumFull = 2,

    /// <summary>The building is already at its highest level (`STRC-1`).</summary>
    MaxLevel = 3,

    /// <summary>The building is already being upgraded; one upgrade runs at a time (`STRC-3`).</summary>
    Building = 4,

    /// <summary>The club cannot pay for the order after what it has committed to bids (`FIN-10`).</summary>
    InsufficientFunds = 5,
}

/// <summary>
/// The rules and the writes of adding places to a ground and upgrading a building, shared by a manager's command and the computer club's
/// evaluation (`INS-12`, `FIN-15`).
/// </summary>
/// <remarks>
/// The checks are one function and the writes are one function, so a computer club that builds faces the identical embargo, ceiling and funds rules
/// a manager does, and its money leaves by the same ledger posting. The caller owns the transaction and the save, and the order: this stages the
/// ground or building, the posting, the audit row and the completion job, and never saves.
/// </remarks>
public sealed class ClubBuilding
{
    private readonly ILedgerRepository _ledger;
    private readonly IJobQueue _jobs;
    private readonly IAuditWriter _audit;

    /// <summary>Initializes the writer.</summary>
    public ClubBuilding(ILedgerRepository ledger, IJobQueue jobs, IAuditWriter audit)
    {
        _ledger = ledger;
        _jobs = jobs;
        _audit = audit;
    }

    /// <summary>Reads an order for new places against the rules.</summary>
    /// <param name="stadium">The club's ground.</param>
    /// <param name="account">The club's account.</param>
    /// <param name="stand">The kind of place.</param>
    /// <param name="count">How many places.</param>
    /// <param name="tier">The tier the club plays in, which prices the places.</param>
    public static ClubBuildCheck CheckSeats(ClubStadium stadium, ClubAccount account, StadiumStand stand, int count, int tier)
    {
        ArgumentNullException.ThrowIfNull(stadium);
        ArgumentNullException.ThrowIfNull(account);

        if (count > stadium.RemainingRoom)
        {
            return ClubBuildCheck.StadiumFull;
        }

        // A club in the red does not build, whatever its cash (DEBT-3).
        if (account.FinancialState != FinancialStateKind.Healthy)
        {
            return ClubBuildCheck.Embargo;
        }

        return StadiumRuleSet.BuildCostMinorFor(stand, tier) * count > account.AvailableMinor
            ? ClubBuildCheck.InsufficientFunds
            : ClubBuildCheck.Allowed;
    }

    /// <summary>Reads an order to raise a building by a level against the rules.</summary>
    /// <param name="building">The building.</param>
    /// <param name="account">The club's account.</param>
    /// <param name="tier">The tier the club plays in, which prices the upgrade.</param>
    public static ClubBuildCheck CheckUpgrade(ClubStructure building, ClubAccount account, int tier)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(account);

        if (building.IsBuilding)
        {
            return ClubBuildCheck.Building;
        }

        if (building.IsAtMaxLevel)
        {
            return ClubBuildCheck.MaxLevel;
        }

        if (account.FinancialState != FinancialStateKind.Healthy)
        {
            return ClubBuildCheck.Embargo;
        }

        return StructureRuleSet.UpgradeCostMinorFor(building.Level + 1, tier) > account.AvailableMinor
            ? ClubBuildCheck.InsufficientFunds
            : ClubBuildCheck.Allowed;
    }

    /// <summary>Stages new places and their payment. The order has been checked by <see cref="CheckSeats"/>.</summary>
    /// <param name="stadium">The club's ground.</param>
    /// <param name="account">The club's account.</param>
    /// <param name="stand">The kind of place.</param>
    /// <param name="count">How many places.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="actor">Who is ordering.</param>
    /// <param name="now">The current instant.</param>
    public void StageSeats(
        ClubStadium stadium,
        ClubAccount account,
        StadiumStand stand,
        int count,
        int tier,
        MarketActor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stadium);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(actor);

        var cost = StadiumRuleSet.BuildCostMinorFor(stand, tier) * count;

        stadium.AddSeats(stand, count, now);

        _ledger.Add(account.Post(
            LedgerPostings.StadiumConstruction(
                Guid.CreateVersion7(),
                account.ClubId,
                stadium.Id,
                stadium.Version,
                stand,
                count,
                cost),
            now));

        _audit.Record(new AuditEntry(
            WorldAuditActions.StadiumSeatsBuilt,
            actor.ActorType,
            actor.UserId,
            AuditTargetTypes.ClubStadium,
            stadium.Id,
            actor.CorrelationId,
            actor.IpHash,
            Reason: $"{stand.ToCode()} x{count}"));
    }

    /// <summary>Stages an upgrade, its payment and the job that finishes it. The order has been checked by <see cref="CheckUpgrade"/>.</summary>
    /// <param name="building">The building.</param>
    /// <param name="account">The club's account.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="actor">Who is ordering.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StageUpgradeAsync(
        ClubStructure building,
        ClubAccount account,
        int tier,
        MarketActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(actor);

        var target = building.Level + 1;
        var cost = StructureRuleSet.UpgradeCostMinorFor(target, tier);
        var dueAt = now.AddDays(StructureRuleSet.BuildDaysFor(target));

        building.StartUpgrade(dueAt, now);

        _ledger.Add(account.Post(
            LedgerPostings.StructureUpgrade(
                Guid.CreateVersion7(),
                account.ClubId,
                building.Id,
                building.Version,
                building.Kind,
                target,
                cost),
            now));

        await _jobs.EnqueueAsync(
            new JobEnqueueRequest
            {
                JobType = StructureJobTypes.CompleteUpgrade,
                BusinessKey = StructureJobTypes.CompleteUpgradeKey(building.Id, target),
                DueAt = dueAt,
                PayloadJson = StructureJobPayload.For(building.Id, target),
            },
            cancellationToken);

        _audit.Record(new AuditEntry(
            WorldAuditActions.StructureUpgradeStarted,
            actor.ActorType,
            actor.UserId,
            AuditTargetTypes.ClubStructure,
            building.Id,
            actor.CorrelationId,
            actor.IpHash,
            Reason: $"{building.Kind.ToCode()} to level {target}"));
    }
}
