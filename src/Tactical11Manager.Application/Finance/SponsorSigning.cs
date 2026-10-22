using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Application.Finance;

/// <summary>What signing a sponsor deal came to.</summary>
public enum SponsorSigningOutcome
{
    /// <summary>The deal was signed.</summary>
    Agreed = 0,

    /// <summary>The club already had a deal and took another offer or objective in the days after a takeover (`SPN-6`).</summary>
    Changed = 1,

    /// <summary>The club has no place in the world's current season.</summary>
    NotInSeason = 2,

    /// <summary>The club was made no offers for the season.</summary>
    NoOffers = 3,

    /// <summary>The version the caller read is not the deal's version now.</summary>
    PreconditionFailed = 4,

    /// <summary>The club has signed, and the days a new manager may think again have passed (`SPN-2`).</summary>
    AlreadySigned = 5,
}

/// <summary>Who is signing, for the audit trail.</summary>
/// <param name="Signer">The kind of signer recorded on the deal.</param>
/// <param name="UserId">The authenticated account, for a manager.</param>
/// <param name="CorrelationId">The correlation key of the request or job.</param>
/// <param name="IpHash">The hashed client address, for a manager.</param>
public sealed record SponsorActor(SponsorSigner Signer, Guid? UserId, string CorrelationId, string? IpHash);

/// <summary>The result of signing.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Deal">The deal, staged, when the signing went through.</param>
/// <param name="BackPaidMatchdays">How many matchdays already played the signing paid for.</param>
/// <param name="BackPayMinor">What it paid for them.</param>
public sealed record SponsorSigningResult(SponsorSigningOutcome Outcome, ClubSponsorDeal? Deal, int BackPaidMatchdays, long BackPayMinor);

/// <summary>
/// Signs a club's sponsor deal for the season: the manager's choice, the board's at the deadline, and a new manager's second thoughts
/// (`SPN-2`, `SPN-3`, `SPN-4`, `SPN-6`, `OBJ-1`).
/// </summary>
/// <remarks>
/// <para>
/// It stages and never saves: the caller owns the transaction, takes this club's sponsor lock through it, and commits the deal, the back pay
/// and the account together. The account is loaded before the published matchdays are counted, and a publication loads the accounts before it
/// reads the deals, so a matchday published while a club signs is paid either by the publication or by the back pay and never by neither:
/// whichever commits second finds the account changed under it and is retried.
/// </para>
/// <para>
/// The matchdays already played are paid at signing at the deal's fixed figure (`SPN-3`). The overdraft limit is set from the forecast the deal
/// gives, at signing (`BUD-3`).
/// </para>
/// </remarks>
public sealed class SponsorSigning
{
    private readonly ISponsorQueries _queries;
    private readonly ISponsorRepository _sponsors;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly IFinanceQueries _finance;
    private readonly IFanQueries _fanQueries;
    private readonly IInboxRepository _inbox;
    private readonly IAuditWriter _audit;
    private readonly IAdvisoryLock _locks;

    /// <summary>Initializes the collaborator.</summary>
    public SponsorSigning(
        ISponsorQueries queries,
        ISponsorRepository sponsors,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        IFinanceQueries finance,
        IFanQueries fanQueries,
        IInboxRepository inbox,
        IAuditWriter audit,
        IAdvisoryLock locks)
    {
        _queries = queries;
        _sponsors = sponsors;
        _accounts = accounts;
        _ledger = ledger;
        _finance = finance;
        _fanQueries = fanQueries;
        _inbox = inbox;
        _audit = audit;
        _locks = locks;
    }

    /// <summary>The objective the board thinks fits the club: its rank by squad strength in its division, rounded to a choice.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SeasonObjective> RealisticObjectiveAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var division = await _fanQueries.ListDivisionOfClubAsync(clubId, cancellationToken);

        if (division.Count == 0)
        {
            return SeasonObjective.StayUp;
        }

        var strengths = await _fanQueries.GetSquadStrengthsAsync([.. division.Select(row => row.ClubId)], cancellationToken);
        var rank = FanBaseKeeper.RankByStrength(strengths).TryGetValue(clubId, out var found) ? found : division.Count;

        return AiFinancePolicy.ChooseObjective(rank);
    }

    /// <summary>
    /// Whether a club that has signed may still take another offer and objective: a new manager's first matchdays (`SPN-6`).
    /// </summary>
    /// <param name="context">The club's season.</param>
    /// <param name="deal">The deal the club has.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> CanChangeAsync(SponsorSeasonContext context, ClubSponsorDeal deal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deal);

        if (deal.IsSettled)
        {
            return false;
        }

        // Only a deal somebody else signed can be thought over again, and only for the first matchdays after the takeover.
        var start = await _queries.GetTenureStartAsync(context.ClubId, cancellationToken);

        if (start is not { } since || deal.SignedAt >= since)
        {
            return false;
        }

        return await _queries.CountMatchdaysPublishedSinceAsync(context.DivisionSeasonId, since, cancellationToken)
            < SponsorDeadline.TakeoverWindowMatchdays;
    }

    /// <summary>Signs the club's deal, or lets a new manager take another.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="kind">The offer taken, or null for the board's balanced offer.</param>
    /// <param name="objective">The objective chosen, or null for the board's realistic one.</param>
    /// <param name="expectedVersion">The deal's version the caller read (0 before there is a deal), or null to skip the check as the board does.</param>
    /// <param name="actor">Who is signing.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>Must run inside the caller's transaction, which it takes the club's sponsor lock through.</remarks>
    public async Task<SponsorSigningResult> SignAsync(
        Guid clubId,
        SponsorOfferKind? kind,
        SeasonObjective? objective,
        long? expectedVersion,
        SponsorActor actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await _locks.AcquireAsync(AdvisoryLockKey.ClubSponsor(clubId), cancellationToken);

        // The account first, then the count of published matchdays: see the remarks on the class.
        var account = await _accounts.FindByClubAsync(clubId, cancellationToken);
        var context = await _queries.GetClubContextAsync(clubId, cancellationToken);

        if (account is null || context is null)
        {
            return Refused(SponsorSigningOutcome.NotInSeason);
        }

        var offers = await _sponsors.ListOffersAsync(clubId, context.SeasonId, cancellationToken);

        if (offers.Count == 0)
        {
            return Refused(SponsorSigningOutcome.NoOffers);
        }

        var deal = await _sponsors.FindDealAsync(clubId, context.SeasonId, cancellationToken);

        if (expectedVersion is { } version && version != (deal?.Version ?? 0))
        {
            return Refused(SponsorSigningOutcome.PreconditionFailed);
        }

        if (deal is not null && (actor.Signer != SponsorSigner.Manager || !await CanChangeAsync(context, deal, cancellationToken)))
        {
            return Refused(SponsorSigningOutcome.AlreadySigned);
        }

        var isChange = deal is not null;
        var chosenKind = kind ?? SponsorOfferKind.Balanced;
        var chosenObjective = objective ?? await RealisticObjectiveAsync(clubId, cancellationToken);
        var offer = offers.First(candidate => candidate.Kind == chosenKind);
        var quote = SponsorOffers.QuoteFor(offer, chosenObjective);

        var backPaid = 0;
        var backPay = 0L;

        if (deal is null)
        {
            deal = ClubSponsorDeal.Sign(Guid.CreateVersion7(), offer, chosenObjective, quote.MetMinor, quote.ExceededMinor, actor.Signer, now);
            _sponsors.AddDeal(deal);

            backPaid = context.PublishedMatchdays;
            backPay = deal.FixedMatchdayMinor * backPaid;

            if (backPay > 0)
            {
                _ledger.Add(account.Post(
                    LedgerPostings.SponsorBackPay(Guid.CreateVersion7(), clubId, deal.Id, backPaid, backPay),
                    now));
            }
        }
        else
        {
            deal.Replace(offer, chosenObjective, quote.MetMinor, quote.ExceededMinor, actor.Signer, now);
        }

        await SetOverdraftAsync(account, deal, now, cancellationToken);

        _audit.Record(new AuditEntry(
            isChange ? FinanceAuditActions.SponsorChanged : FinanceAuditActions.SponsorSigned,
            actor.Signer == SponsorSigner.Manager ? AuditActorTypes.User : AuditActorTypes.Service,
            actor.UserId,
            AuditTargetTypes.SponsorDeal,
            deal.Id,
            actor.CorrelationId,
            actor.IpHash,
            $"{actor.Signer.ToCode()} signed the {chosenKind.ToCode()} offer at {chosenObjective.ToCode()} (SPN-2)."));

        if (actor.Signer == SponsorSigner.Board)
        {
            await TellManagerAsync(clubId, offer, chosenObjective, now, cancellationToken);
        }

        return new SponsorSigningResult(
            isChange ? SponsorSigningOutcome.Changed : SponsorSigningOutcome.Agreed,
            deal,
            backPaid,
            backPay);
    }

    private static SponsorSigningResult Refused(SponsorSigningOutcome outcome) => new(outcome, null, 0, 0);

    /// <summary>Sets the overdraft limit from the forecast the deal gives (`BUD-3`), which the club's account has not yet seen.</summary>
    private async Task SetOverdraftAsync(ClubAccount account, ClubSponsorDeal deal, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bases = await _finance.GetClubFinanceBasisAsync([account.ClubId], cancellationToken);

        if (bases.Count == 0)
        {
            return;
        }

        var basis = bases[0];

        MatchdayFinances.SetOverdraft(
            account,
            (basis with { SponsorFixedMatchdayMinor = deal.FixedMatchdayMinor }).OverdraftLimitMinor,
            now);
    }

    private async Task TellManagerAsync(Guid clubId, ClubSponsorOffer offer, SeasonObjective objective, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var targets = await _inbox.FindClubTargetsAsync([clubId], cancellationToken);

        if (targets.Count == 0 || targets[0].ManagerId is not { } managerId || managerId == Guid.Empty)
        {
            return;
        }

        var draft = FinanceInboxTemplates.SponsorSignedByBoardMessage(
            offer.SponsorName,
            offer.Kind.ToCode(),
            objective.ToCode(),
            offer.FixedSeasonMinor);

        _inbox.Add(InboxMessage.Record(
            Guid.CreateVersion7(),
            managerId,
            draft.Category,
            draft.TemplateKey,
            draft.ParametersJson,
            draft.RelatedEntityId,
            now));
    }
}
