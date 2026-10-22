using Microsoft.Extensions.Logging;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Comms;
using Tactical11Manager.Application.Market;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.Finance;

/// <summary>What reading the clubs' money did.</summary>
/// <param name="Read">How many clubs were read.</param>
/// <param name="Changed">How many moved between healthy, in the red and administration.</param>
/// <param name="Listed">How many players the boards listed.</param>
/// <param name="Dismissed">How many managers were dismissed.</param>
public sealed record FinancialStatesResult(int Read, int Changed, int Listed, int Dismissed);

/// <summary>
/// Reads clubs' money after a batch of postings and moves them between healthy, in the red and administration, acting on what it finds
/// (`DEBT-1`…`DEBT-5`).
/// </summary>
/// <remarks>
/// <para>
/// The transitions are <see cref="ClubFinancialState.Next"/>'s; this is the part that does something about them. A manager is told when the
/// club goes into the red or into administration and when it comes out. A club in administration has its highest earners put on the market by
/// the board, through the same listing writer a manager uses and flagged so the manager cannot take them off. A manager whose club stays in
/// administration for <see cref="EconomyRuleSet.AdministrationDismissalMatchdays"/> matchdays is dismissed: the tenure closes with its own
/// reason, the same cooldown as a resignation starts, and the club is a computer club again. A computer club in administration just keeps
/// selling.
/// </para>
/// <para>
/// It stages rows and never saves, so the caller commits them with the postings that moved the money. The embargo that follows from a state is
/// enforced where the money is spent (the bid writer, the stadium and the buildings), by reading the state this stores.
/// </para>
/// </remarks>
public sealed partial class ClubFinancialStates
{
    /// <summary>The length, in seasons, of the contract a buyer of a board-listed player signs.</summary>
    private const int BoardListingSeasons = 2;

    private readonly IClubAccountRepository _accounts;
    private readonly IFinanceQueries _queries;
    private readonly IInboxRepository _inbox;
    private readonly IListingWriter _listings;
    private readonly IClubTenureRepository _tenures;
    private readonly IOutboxWriter _outbox;
    private readonly IAuditWriter _audit;
    private readonly IOperationalMetrics _metrics;
    private readonly ILogger<ClubFinancialStates> _logger;

    /// <summary>Initializes the collaborator.</summary>
    public ClubFinancialStates(
        IClubAccountRepository accounts,
        IFinanceQueries queries,
        IInboxRepository inbox,
        IListingWriter listings,
        IClubTenureRepository tenures,
        IOutboxWriter outbox,
        IAuditWriter audit,
        IOperationalMetrics metrics,
        ILogger<ClubFinancialStates> logger)
    {
        _accounts = accounts;
        _queries = queries;
        _inbox = inbox;
        _listings = listings;
        _tenures = tenures;
        _outbox = outbox;
        _audit = audit;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>Reads the given clubs' money and acts on what it finds.</summary>
    /// <param name="clubIds">The clubs to read.</param>
    /// <param name="ordinal">The matchday ordinal now, counted across seasons (<see cref="MatchdayOrdinal"/>).</param>
    /// <param name="correlationId">The operation's correlation key, which the audit entries and the board's listing keys carry.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FinancialStatesResult> ApplyAsync(
        IReadOnlyCollection<Guid> clubIds,
        int ordinal,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (clubIds.Count == 0)
        {
            return new FinancialStatesResult(0, 0, 0, 0);
        }

        var accounts = await _accounts.LoadAsync(clubIds, cancellationToken);
        var steps = new List<(ClubAccount Account, FinancialStateKind Before, FinancialStep Step)>();

        foreach (var account in accounts.OrderBy(account => account.ClubId))
        {
            var step = ClubFinancialState.Next(
                account.FinancialState,
                account.StateSinceMatchday,
                ordinal,
                account.CashMinor,
                account.OverdraftLimitMinor);

            var before = account.FinancialState;

            if (step.State != account.FinancialState || step.SinceMatchday != account.StateSinceMatchday)
            {
                account.RecordState(step, now);
            }

            steps.Add((account, before, step));
        }

        var targets = (await _inbox.FindClubTargetsAsync([.. steps.Select(entry => entry.Account.ClubId)], cancellationToken))
            .ToDictionary(target => target.ClubId);

        var changed = 0;

        foreach (var (account, before, step) in steps.Where(entry => entry.Step.Transition != FinancialTransition.None))
        {
            changed++;

            _audit.Record(new AuditEntry(
                FinanceAuditActions.StateChanged,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.Club,
                account.ClubId,
                correlationId,
                IpHash: null,
                $"{before.ToString()} to {step.State.ToString()} at {account.CashMinor} minor units (DEBT-2)."));

            if (targets.TryGetValue(account.ClubId, out var target) && target.ManagerId is { } managerId && managerId != Guid.Empty)
            {
                AddMessage(managerId, MessageFor(account, step), now);
            }
        }

        var administration = steps
            .Where(entry => entry.Step.State == FinancialStateKind.Administration)
            .Select(entry => entry.Account)
            .ToList();

        var listed = 0;
        var dismissed = 0;

        if (administration.Count > 0)
        {
            var basis = (await _queries.GetClubFinanceBasisAsync([.. administration.Select(account => account.ClubId)], cancellationToken))
                .ToDictionary(row => row.ClubId);

            foreach (var account in administration)
            {
                if (!basis.TryGetValue(account.ClubId, out var row))
                {
                    continue;
                }

                var matchdaysLeft = Math.Max(0, EconomyRuleSet.AdministrationDismissalMatchdays - (ordinal - account.StateSinceMatchday));

                listed += await ListForBoardAsync(account, row, matchdaysLeft, ordinal, correlationId, targets, now, cancellationToken);
            }
        }

        var dismissals = steps.Where(entry => entry.Step.Dismiss).Select(entry => entry.Account).ToList();

        if (dismissals.Count > 0)
        {
            dismissed = await DismissAsync(dismissals, now, cancellationToken);
        }

        if (changed > 0 || listed > 0 || dismissed > 0)
        {
            LogRead(steps.Count, changed, listed, dismissed);
        }

        return new FinancialStatesResult(steps.Count, changed, listed, dismissed);
    }

    private static InboxDraft MessageFor(ClubAccount account, FinancialStep step) => step.Transition switch
    {
        FinancialTransition.EnteredRed => FinanceInboxTemplates.InTheRedMessage(account.CashMinor, account.OverdraftLimitMinor),
        FinancialTransition.LeftRed => FinanceInboxTemplates.BackInTheBlackMessage(account.CashMinor),
        FinancialTransition.EnteredAdministration => FinanceInboxTemplates.AdministrationEnteredMessage(
            account.CashMinor,
            account.OverdraftLimitMinor,
            EconomyRuleSet.AdministrationDismissalMatchdays),
        _ => FinanceInboxTemplates.AdministrationLeftMessage(account.CashMinor),
    };

    private async Task<int> ListForBoardAsync(
        ClubAccount account,
        ClubFinanceBasis basis,
        int matchdaysLeft,
        int ordinal,
        string correlationId,
        Dictionary<Guid, ClubInboxTarget> targets,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var squad = await _queries.GetAdministrationSquadAsync(account.ClubId, cancellationToken);
        var sales = ClubFinancialState.ChooseSales(squad, basis.SeasonWageMinor, basis.WageBudgetMinor);

        if (sales.Count == 0)
        {
            return 0;
        }

        var actor = MarketActor.Service(correlationId);
        var listedIds = new List<Guid>();

        foreach (var sale in sales)
        {
            var result = await _listings.ListAsync(
                account.ClubId,
                actor,
                sale.PlayerId,
                sale.AskingMinor,
                BoardListingSeasons,
                $"board:{account.ClubId:N}:{sale.PlayerId:N}:{ordinal}",
                cancellationToken,
                listedByBoard: true);

            // A player the writer refuses (a squad that could not stand the sale) is simply not listed this time.
            if (result.Outcome != MarketOutcome.Found || !result.Created)
            {
                continue;
            }

            listedIds.Add(sale.PlayerId);

            _audit.Record(new AuditEntry(
                FinanceAuditActions.BoardListedPlayer,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.TransferListing,
                result.ListingId,
                correlationId,
                IpHash: null,
                $"The board listed a player of club {account.ClubId:D} at {sale.AskingMinor} minor units (DEBT-4)."));
        }

        if (listedIds.Count > 0
            && targets.TryGetValue(account.ClubId, out var target)
            && target.ManagerId is { } managerId
            && managerId != Guid.Empty)
        {
            var names = await _inbox.FindPlayerNamesAsync(listedIds, cancellationToken);
            var draft = FinanceInboxTemplates.BoardListedMessage(
                [.. listedIds.Select(id => names.TryGetValue(id, out var name) ? name : "a player")],
                matchdaysLeft);

            AddMessage(managerId, draft, now);
        }

        return listedIds.Count;
    }

    private async Task<int> DismissAsync(
        IReadOnlyList<ClubAccount> accounts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var clubIds = accounts.Select(account => account.ClubId).ToHashSet();
        var rows = (await _tenures.ListOpenAsync(cancellationToken))
            .Where(row => clubIds.Contains(row.Tenure.ClubId))
            .ToList();

        foreach (var row in rows)
        {
            var account = accounts.First(candidate => candidate.ClubId == row.Tenure.ClubId);
            var matchdays = EconomyRuleSet.AdministrationDismissalMatchdays;

            row.Tenure.Close(ClubTenureEndReasons.Dismissed, now);
            row.Manager.BeginTakeoverCooldown(WorldRuleSet.ResignationCooldown, now);

            var draft = FinanceInboxTemplates.ManagerDismissedMessage(row.Club.Name, matchdays);

            AddMessage(row.Manager.Id, draft, now);

            _outbox.Enqueue(EmailOutbox.For(OccupancyEmails.Dismissed(row.User.Email, row.Club.Name, matchdays)));

            _audit.Record(new AuditEntry(
                FinanceAuditActions.ManagerDismissed,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.ClubTenure,
                row.Tenure.Id,
                $"dismissal:{account.ClubId:N}",
                IpHash: null,
                $"Dismissed after {matchdays} matchdays in administration (DEBT-5)."));

            _metrics.TenureClosed(ClubTenureEndReasons.Dismissed);
        }

        return rows.Count;
    }

    private void AddMessage(Guid managerId, InboxDraft draft, DateTimeOffset now) =>
        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));

    [LoggerMessage(
        EventId = 3303,
        Level = LogLevel.Information,
        Message = "Read the money of {ClubCount} clubs: {ChangedCount} changed state, {ListedCount} players listed by boards, {DismissedCount} managers dismissed (DEBT-2).")]
    private partial void LogRead(int clubCount, int changedCount, int listedCount, int dismissedCount);
}
