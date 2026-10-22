using Tactical11Manager.Application.Abstractions.Comms;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Application.Fans;
using Tactical11Manager.Domain.Comms;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Finance;

/// <summary>What the offers are made from, for one club.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Fans">The club's fans when the season opens.</param>
/// <param name="StandingPoints">The club's standing in points when the season opens.</param>
/// <param name="StrengthRank">The club's rank by squad strength in its division, 1 for the strongest.</param>
/// <param name="IsHuman">Whether a manager holds the club, who chooses for it; a computer club signs at once.</param>
public sealed record SponsorClubInput(Guid ClubId, int Fans, int StandingPoints, int StrengthRank, bool IsHuman);

/// <summary>A club a manager holds, whose sponsor is the manager's to choose.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="Realistic">The objective the board thinks fits the squad.</param>
public sealed record SponsorUnsignedClub(Guid ClubId, SeasonObjective Realistic);

/// <summary>What opening a season's sponsors did.</summary>
/// <param name="Offers">How many offers were made.</param>
/// <param name="AiDeals">How many computer clubs signed at once.</param>
/// <param name="Choosing">The clubs held by a manager, who now has until the deadline to choose.</param>
public sealed record SponsorOpening(int Offers, int AiDeals, IReadOnlyList<SponsorUnsignedClub> Choosing);

/// <summary>
/// Makes every club of a division its three sponsor offers when a season opens, and signs the computer clubs' deals (`SPN-1`, `SPN-4`).
/// </summary>
/// <remarks>
/// <para>
/// It is the one place offers are written, whether the season is the world's first or a rollover's: the seeder calls <see cref="Open"/> with
/// what it has just generated, and the rollover calls <see cref="OpenSeasonAsync"/>, which reads the same figures from the database. Both stage
/// into the caller's unit of work and never save, so a phase that is retried finds its offers present and a crash leaves none of them.
/// </para>
/// <para>
/// A computer club signs at once through <c>ai-finance-v1</c>: the offer its temperament asks for, at the board's realistic objective. A club a
/// manager holds is left unsigned and is the manager's to decide until the deadline (`SPN-4`).
/// </para>
/// </remarks>
public sealed class SponsorSeasonOpener
{
    private readonly ISponsorRepository _sponsors;
    private readonly IClubRepository _clubs;
    private readonly IFanQueries _fanQueries;
    private readonly FanBaseKeeper _fans;
    private readonly IInboxRepository _inbox;

    /// <summary>Initializes the collaborator.</summary>
    public SponsorSeasonOpener(
        ISponsorRepository sponsors,
        IClubRepository clubs,
        IFanQueries fanQueries,
        FanBaseKeeper fans,
        IInboxRepository inbox)
    {
        _sponsors = sponsors;
        _clubs = clubs;
        _fanQueries = fanQueries;
        _fans = fans;
        _inbox = inbox;
    }

    /// <summary>Stages the offers of a division's clubs, and the computer clubs' deals.</summary>
    /// <param name="worldId">The world, which seeds the sponsors' names.</param>
    /// <param name="seasonId">The season the offers are for.</param>
    /// <param name="seasonNumber">The season's ordinal in the world.</param>
    /// <param name="tier">The tier the clubs play in.</param>
    /// <param name="clubs">What each club's offers are made from.</param>
    /// <param name="now">The current instant.</param>
    public SponsorOpening Open(
        Guid worldId,
        Guid seasonId,
        int seasonNumber,
        int tier,
        IReadOnlyList<SponsorClubInput> clubs,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(clubs);

        var offers = 0;
        var aiDeals = 0;
        var unsigned = new List<SponsorUnsignedClub>();

        foreach (var club in clubs.OrderBy(club => club.ClubId))
        {
            var made = SponsorOffers.For(worldId, seasonNumber, club.ClubId, tier, club.Fans, club.StandingPoints)
                .Select(offer => ClubSponsorOffer.Record(
                    Guid.CreateVersion7(),
                    club.ClubId,
                    seasonId,
                    offer,
                    tier,
                    club.Fans,
                    club.StandingPoints,
                    now))
                .ToList();

            _sponsors.AddOffers(made);
            offers += made.Count;

            if (club.IsHuman)
            {
                unsigned.Add(new SponsorUnsignedClub(club.ClubId, AiFinancePolicy.ChooseObjective(club.StrengthRank)));

                continue;
            }

            var kind = AiFinancePolicy.ChooseOffer(AiMatchPlanPolicy.TemperamentOf(club.ClubId));
            var objective = AiFinancePolicy.ChooseObjective(club.StrengthRank);
            var offer = made.First(candidate => candidate.Kind == kind);
            var quote = SponsorOffers.QuoteFor(offer, objective);

            _sponsors.AddDeal(ClubSponsorDeal.Sign(
                Guid.CreateVersion7(),
                offer,
                objective,
                quote.MetMinor,
                quote.ExceededMinor,
                SponsorSigner.Ai,
                now));

            aiDeals++;
        }

        return new SponsorOpening(offers, aiDeals, unsigned);
    }

    /// <summary>Reads what a rollover's clubs stand on, stages their offers, and tells the managers who have a choice to make.</summary>
    /// <param name="season">The season that is opening.</param>
    /// <param name="tier">The tier the clubs are placed in.</param>
    /// <param name="clubIds">The clubs placed in the tier.</param>
    /// <param name="humanClubIds">The clubs a manager holds.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Runs after the fans have opened the season, so a club that changed tier is offered money for the fans it now has.
    /// </remarks>
    public async Task<SponsorOpening> OpenSeasonAsync(
        Season season,
        int tier,
        IReadOnlyList<Guid> clubIds,
        IReadOnlySet<Guid> humanClubIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(clubIds);
        ArgumentNullException.ThrowIfNull(humanClubIds);

        if (clubIds.Count == 0)
        {
            return new SponsorOpening(0, 0, []);
        }

        var fanBases = await _fans.LoadAsync(clubIds, _ => tier, season.Id, now, cancellationToken);
        var standings = (await _clubs.LoadAsync(clubIds, cancellationToken)).ToDictionary(club => club.Id);
        var ranks = FanBaseKeeper.RankByStrength(await _fanQueries.GetSquadStrengthsAsync(clubIds, cancellationToken));

        var opening = Open(
            season.WorldId,
            season.Id,
            season.SequenceNumber,
            tier,
            [
                .. clubIds.Select(clubId => new SponsorClubInput(
                    clubId,
                    fanBases[clubId].Fans,
                    standings.TryGetValue(clubId, out var club) ? club.Reputation : EconomyRuleSet.OpeningStandingFor(tier),
                    ranks.TryGetValue(clubId, out var rank) ? rank : clubIds.Count,
                    humanClubIds.Contains(clubId))),
            ],
            now);

        await TellManagersAsync(season, opening.Choosing, now, cancellationToken);

        return opening;
    }

    private async Task TellManagersAsync(
        Season season,
        IReadOnlyList<SponsorUnsignedClub> unsigned,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (unsigned.Count == 0)
        {
            return;
        }

        var targets = (await _inbox.FindClubTargetsAsync([.. unsigned.Select(club => club.ClubId)], cancellationToken))
            .ToDictionary(target => target.ClubId);
        var deadline = SponsorDeadline.LockAtFor(DateOnly.FromDateTime(season.StartsAt.UtcDateTime));

        foreach (var club in unsigned)
        {
            if (!targets.TryGetValue(club.ClubId, out var target) || target.ManagerId is not { } managerId || managerId == Guid.Empty)
            {
                continue;
            }

            var draft = FinanceInboxTemplates.SponsorOffersReadyMessage(deadline, club.Realistic.ToCode());

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
}
