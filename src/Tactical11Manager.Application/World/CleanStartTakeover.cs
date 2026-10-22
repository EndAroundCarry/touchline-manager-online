using System.Globalization;
using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.Market;
using Tactical11Manager.Application.Abstractions.Ops;
using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Finance;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What a clean start did.</summary>
/// <param name="PlayersReleased">How many contracts it closed.</param>
/// <param name="PlayersSigned">How many players the club's new squad holds.</param>
/// <param name="ListingsCancelled">How many of the club's own listings it took off the market.</param>
/// <param name="BidsReleased">How many of the club's leading bids it gave up.</param>
/// <param name="CashBeforeMinor">What the club held before, which is negative for a club in debt.</param>
/// <param name="CashAfterMinor">The tier's opening cash, which is what the club holds now.</param>
public sealed record CleanStartResult(
    int PlayersReleased,
    int PlayersSigned,
    int ListingsCancelled,
    int BidsReleased,
    long CashBeforeMinor,
    long CashAfterMinor);

/// <summary>
/// Sends a club back to its tier's opening state when its new manager chooses a clean start (`TKO-2`, `TKO-3`).
/// </summary>
/// <remarks>
/// <para>
/// It runs inside the takeover's own transaction, beside the tenure and the fans' reset, so a club that was claimed with a clean start has all
/// of it or none. The old players are released to the free agents with no payout: they join the next Monday's batch at their real value (`FA-5`),
/// and a fixture already locked keeps the players it was locked with (`SQ-7`). The club's own listings and leading bids come off the market and
/// every reservation behind them is released before the cash is set, so no money is left promised to an auction that is gone.
/// </para>
/// <para>
/// The new squad is the tier's default, drawn to wages a fresh club can carry (<see cref="CleanStartSquad"/>). The ground goes back to the places
/// every club opens with, which is the one time places leave a ground and an exception to `STAD-6`; the snack bar and the shop go back to level
/// 1, and an upgrade under way is dropped and not refunded. One ledger entry sets the cash to the tier's opening cash, wiping any debt, and the
/// account goes back to healthy. The club's standing returns to the tier's default; the sponsor deal for the season stays, since the manager may
/// think again about it for three matchdays (`SPN-6`).
/// </para>
/// <para>
/// It stages rows and never saves. The caller saves before the fans are reset, because the fans' target is worked out from the squad as it
/// stands in the database, and calls <see cref="SetOpeningOverdraftAsync"/> once the fans and the squad are saved, because the overdraft limit
/// is a quarter of the forecast they give.
/// </para>
/// </remarks>
public sealed class CleanStartTakeover
{
    private readonly IListingRepository _listings;
    private readonly IBidRepository _bids;
    private readonly IClubAccountRepository _accounts;
    private readonly ILedgerRepository _ledger;
    private readonly ISquadRepository _squad;
    private readonly IStadiumRepository _stadiums;
    private readonly IStructureRepository _structures;
    private readonly ITacticsRepository _tactics;
    private readonly ITeamSheetRepository _sheets;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly IFinanceQueries _finance;
    private readonly IAuditWriter _audit;
    private readonly IRequestContext _requestContext;

    /// <summary>Initializes the collaborator.</summary>
    public CleanStartTakeover(
        IListingRepository listings,
        IBidRepository bids,
        IClubAccountRepository accounts,
        ILedgerRepository ledger,
        ISquadRepository squad,
        IStadiumRepository stadiums,
        IStructureRepository structures,
        ITacticsRepository tactics,
        ITeamSheetRepository sheets,
        IGenerationRunRepository generationRuns,
        IFinanceQueries finance,
        IAuditWriter audit,
        IRequestContext requestContext)
    {
        _listings = listings;
        _bids = bids;
        _accounts = accounts;
        _ledger = ledger;
        _squad = squad;
        _stadiums = stadiums;
        _structures = structures;
        _tactics = tactics;
        _sheets = sheets;
        _generationRuns = generationRuns;
        _finance = finance;
        _audit = audit;
        _requestContext = requestContext;
    }

    /// <summary>Sends a club back to its tier's opening state.</summary>
    /// <param name="userId">The manager's account, which the audit entry names.</param>
    /// <param name="club">The club, tracked.</param>
    /// <param name="country">The club's country.</param>
    /// <param name="season">The season being played.</param>
    /// <param name="tier">The tier the club plays in.</param>
    /// <param name="tenureId">The new tenure.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CleanStartResult> ApplyAsync(
        Guid userId,
        Club club,
        Country country,
        Season season,
        int tier,
        Guid tenureId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(season);

        var account = await _accounts.FindByClubAsync(club.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Club {club.Id} has no account, so it cannot start clean (TKO-3).");

        var worldSeed = await _generationRuns.FindWorldSeedAsync(cancellationToken)
            ?? throw new InvalidOperationException("The world's generation seed is missing, so a clean-start squad cannot be drawn (TKO-3).");

        var bidsReleased = await ReleaseOwnBidsAsync(account, now, cancellationToken);
        var listingsCancelled = await CancelOwnListingsAsync(club.Id, now, cancellationToken);
        var released = await ReleaseSquadAsync(club.Id, now, cancellationToken);

        var squad = CleanStartSquad.Draw(worldSeed, country, club, tier, season, now);

        foreach (var member in squad)
        {
            _squad.AddPlayer(member.Player);
            _squad.AddPlayerAttributes(member.Attributes);
            _squad.AddPlayerState(member.State);
            _squad.AddPlayerContract(member.Contract);
            _squad.AddPlayerRegistration(member.Registration);
        }

        var cashBefore = account.CashMinor;
        var openingCash = WorldRuleSet.OpeningCashMinorForTier(tier);

        if (openingCash != cashBefore)
        {
            _ledger.Add(account.Post(
                LedgerPostings.CleanStart(Guid.CreateVersion7(), club.Id, tenureId, openingCash - cashBefore, openingCash),
                now));
        }

        account.RestoreHealth(now);

        var stadium = await _stadiums.FindByClubAsync(club.Id, cancellationToken);
        var groundBefore = stadium?.Capacity ?? 0;

        stadium?.ResetToOpening(now);

        foreach (var building in await _structures.ListByClubAsync(club.Id, cancellationToken))
        {
            building.ResetToOpening(now);
        }

        var standingBefore = club.StandingCentis;

        club.RecordStanding(ClubStanding.OpeningCentis(tier), now);

        await RebuildDefaultPlanAsync(club.Id, squad, now, cancellationToken);
        await _sheets.RemoveDraftSheetsAsync(club.Id, cancellationToken);

        _audit.Record(new AuditEntry(
            WorldAuditActions.ClubCleanStarted,
            AuditActorTypes.User,
            userId,
            AuditTargetTypes.ClubTenure,
            tenureId,
            _requestContext.CorrelationId,
            IpHash: null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Released {released} players, cash {cashBefore} to {openingCash} minor units, ground {groundBefore} to {stadium?.Capacity} places, standing {standingBefore} to {club.StandingCentis} centis (TKO-3).")));

        return new CleanStartResult(released, squad.Count, listingsCancelled, bidsReleased, cashBefore, openingCash);
    }

    /// <summary>
    /// Sets the overdraft limit from the forecast the fresh club has, which the account has not yet seen (`BUD-3`). Called once the squad, the
    /// fans and the ground are saved, since it reads them.
    /// </summary>
    /// <param name="clubId">The club.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SetOpeningOverdraftAsync(Guid clubId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindByClubAsync(clubId, cancellationToken);
        var bases = await _finance.GetClubFinanceBasisAsync([clubId], cancellationToken);

        if (account is null || bases.Count == 0)
        {
            return;
        }

        MatchdayFinances.SetOverdraft(account, bases[0].OverdraftLimitMinor, now);
    }

    /// <summary>Gives up the bids the club leads, releasing the money behind them.</summary>
    private async Task<int> ReleaseOwnBidsAsync(ClubAccount account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bids = await _bids.ListLeadingByClubAsync(account.ClubId, cancellationToken);

        foreach (var bid in bids)
        {
            _ledger.Add(account.Post(
                LedgerPostings.ReservationRelease(Guid.CreateVersion7(), account.ClubId, bid.Id, bid.AmountMinor),
                now));

            bid.Release(now);
        }

        return bids.Count;
    }

    /// <summary>Takes the club's own players off the market, releasing the bidder's money behind each (`TRF-15`).</summary>
    private async Task<int> CancelOwnListingsAsync(Guid clubId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var listings = await _listings.ListOpenBySellerAsync(clubId, cancellationToken);

        foreach (var listing in listings)
        {
            var leading = await _bids.FindLeadingBidAsync(listing.Id, cancellationToken);

            if (leading is not null)
            {
                var bidder = await _accounts.FindByClubAsync(leading.BidderClubId, cancellationToken);

                if (bidder is not null)
                {
                    _ledger.Add(bidder.Post(
                        LedgerPostings.ReservationRelease(Guid.CreateVersion7(), leading.BidderClubId, leading.Id, leading.AmountMinor),
                        now));
                }

                leading.Release(now);
            }

            listing.Cancel(now);
        }

        return listings.Count;
    }

    /// <summary>Closes every contract as released, with no payout, and sends the players to the free agents (`FA-5`).</summary>
    private async Task<int> ReleaseSquadAsync(Guid clubId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var contracts = await _squad.LoadActiveContractsForClubsAsync([clubId], cancellationToken);

        if (contracts.Count == 0)
        {
            return 0;
        }

        var playerIds = contracts.Select(contract => contract.PlayerId).ToList();
        var registrations = await _squad.LoadActiveRegistrationsForPlayersAsync(playerIds, cancellationToken);
        var players = await _squad.LoadPlayersAsync(playerIds, cancellationToken);

        foreach (var contract in contracts)
        {
            contract.Close(PlayerContractCloseReasons.Released, now);
        }

        foreach (var registration in registrations)
        {
            registration.End(now);
        }

        foreach (var player in players)
        {
            player.ReleaseToFreeAgency(now);
        }

        return contracts.Count;
    }

    /// <summary>
    /// Puts the new squad into the club's default plan, as the AI policy would have picked it, and empties the bench. The plan's shape, its
    /// instructions and the club's training stay as they were; only the players are new.
    /// </summary>
    private async Task RebuildDefaultPlanAsync(
        Guid clubId,
        IReadOnlyList<Domain.Squad.Generation.GeneratedSquadMember> squad,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await _tactics.FindDefaultAsync(clubId, cancellationToken);

        if (record is null)
        {
            return;
        }

        var decision = AiClubPolicy.Decide(
            clubId,
            [
                .. squad.Select(member => new AiSquadPlayer(
                    member.Player.Id,
                    member.Player.PrimaryPosition,
                    member.Player.SecondaryPositions,
                    member.Attributes.ToSet().Values,
                    member.State.ConditionBp,
                    IsAvailable: true)),
            ]);

        foreach (var bench in record.Bench)
        {
            _tactics.RemoveBenchSlot(bench);
        }

        foreach (var slot in record.Slots)
        {
            var pick = decision.Slots.FirstOrDefault(candidate =>
                candidate.SlotNumber == slot.SlotNumber && candidate.PositionFamily == slot.PositionFamily);

            slot.Assign(pick?.AssignedPlayerId, now);
        }
    }
}
