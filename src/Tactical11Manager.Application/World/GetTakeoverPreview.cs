using Tactical11Manager.Application.Abstractions;
using Tactical11Manager.Application.Abstractions.Fans;
using Tactical11Manager.Application.Abstractions.Finance;
using Tactical11Manager.Application.Abstractions.World;
using Tactical11Manager.Contracts.World;
using Tactical11Manager.Domain.Competition;
using Tactical11Manager.Domain.Finance;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.World;

namespace Tactical11Manager.Application.World;

/// <summary>What happened when a takeover preview was read.</summary>
public enum TakeoverPreviewOutcome
{
    /// <summary>Read.</summary>
    Found = 0,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 1,

    /// <summary>No such club.</summary>
    ClubNotFound = 2,

    /// <summary>The club is not one a new manager may take over (`WORLD-8`).</summary>
    ClubNotClaimable = 3,

    /// <summary>Another manager already holds the club.</summary>
    ClubAlreadyClaimed = 4,
}

/// <summary>The result of a takeover preview.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Preview">The preview, when one was read.</param>
public sealed record TakeoverPreviewResult(TakeoverPreviewOutcome Outcome, TakeoverPreviewResponse? Preview);

/// <summary>
/// Reads a club as it would be under each way of taking it over, so a manager chooses between keeping it and starting clean with both in view
/// (`TKO-4`).
/// </summary>
/// <remarks>
/// Read-only. The clean start's side is drawn by the same function the claim uses (<see cref="CleanStartSquad"/>), so the squad it shows is the
/// squad the claim writes, and the rest of it is the tier's opening state. A club that cannot be claimed has no preview, for the reasons a claim
/// would be refused.
/// </remarks>
public sealed class GetTakeoverPreview
{
    private readonly IClock _clock;
    private readonly IWorldRepository _world;
    private readonly IClubRepository _clubs;
    private readonly IClubTenureRepository _tenures;
    private readonly IGenerationRunRepository _generationRuns;
    private readonly IOnboardingQueries _queries;
    private readonly IClubAccountRepository _accounts;
    private readonly IFinanceQueries _finance;
    private readonly IFanQueries _fans;

    /// <summary>Initializes the query.</summary>
    public GetTakeoverPreview(
        IClock clock,
        IWorldRepository world,
        IClubRepository clubs,
        IClubTenureRepository tenures,
        IGenerationRunRepository generationRuns,
        IOnboardingQueries queries,
        IClubAccountRepository accounts,
        IFinanceQueries finance,
        IFanQueries fans)
    {
        _clock = clock;
        _world = world;
        _clubs = clubs;
        _tenures = tenures;
        _generationRuns = generationRuns;
        _queries = queries;
        _accounts = accounts;
        _finance = finance;
        _fans = fans;
    }

    /// <summary>Reads both options for a club.</summary>
    /// <param name="clubId">The club.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TakeoverPreviewResult> ExecuteAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var world = await _world.FindWorldAsync(cancellationToken);

        if (world is null)
        {
            return Refused(TakeoverPreviewOutcome.WorldNotSeeded);
        }

        var club = await _clubs.FindAsync(clubId, cancellationToken);

        if (club is null)
        {
            return Refused(TakeoverPreviewOutcome.ClubNotFound);
        }

        var placement = await _queries.GetClubDashboardAsync(club.Id, world.CurrentSeasonNumber, cancellationToken);

        if (placement is null
            || club.Status != ClubStatus.Active
            || placement.Division.CountryId != club.CountryId
            || placement.Division.Status != DivisionStatus.Active)
        {
            return Refused(TakeoverPreviewOutcome.ClubNotClaimable);
        }

        if (await _tenures.FindOpenByClubAsync(club.Id, cancellationToken) is not null)
        {
            return Refused(TakeoverPreviewOutcome.ClubAlreadyClaimed);
        }

        var tier = placement.Division.TierNumber;
        var bases = await _finance.GetClubFinanceBasisAsync([club.Id], cancellationToken);
        var basis = bases.Count == 0 ? null : bases[0];
        var account = await _accounts.FindByClubAsync(club.Id, cancellationToken);
        var strengths = await _fans.GetSquadStrengthsAsync([club.Id], cancellationToken);
        var worldSeed = await _generationRuns.FindWorldSeedAsync(cancellationToken);

        if (basis is null || account is null || worldSeed is null)
        {
            return Refused(TakeoverPreviewOutcome.ClubNotClaimable);
        }

        var keep = new TakeoverOptionResponse(
            TakeoverStarts.KeepCode,
            basis.ContractedPlayers,
            strengths.TryGetValue(club.Id, out var strength) ? Shown(strength) : 0m,
            basis.SeasonWageMinor,
            account.CashMinor,
            Math.Max(0, -account.CashMinor),
            account.FinancialState.ToCode(),
            basis.Seats.Total,
            StadiumRuleSet.LevelFor(basis.Seats.Total),
            basis.SnackBarLevel,
            basis.MerchandiseLevel,
            basis.ForecastMinor,
            basis.WageBudgetMinor,
            account.OverdraftLimitMinor);

        var squad = CleanStartSquad.Draw(worldSeed, placement.Country, club, tier, placement.Season, _clock.UtcNow);
        var opening = FanRuleSet.OnTakeover(tier);

        // The season's sponsor deal stays with the club (`SPN-6`), so the fresh club's forecast is the opening ground, buildings and fans on
        // the deal it already has: the same forecast the overdraft limit is set from once the claim is made.
        var fresh = basis with
        {
            Seats = EconomyRuleSet.OpeningSeats,
            Fans = opening.Fans,
            MoodBp = opening.MoodBp,
            SnackBarLevel = StructureRuleSet.OpeningLevel,
            MerchandiseLevel = StructureRuleSet.OpeningLevel,
            SeasonWageMinor = squad.Sum(member => member.Contract.SeasonWageMinor),
            ContractedPlayers = squad.Count,
        };

        var cleanStart = new TakeoverOptionResponse(
            TakeoverStarts.CleanStartCode,
            squad.Count,
            Shown(CleanStartSquad.StrengthOf(squad)),
            fresh.SeasonWageMinor,
            WorldRuleSet.OpeningCashMinorForTier(tier),
            0,
            FinancialStateKind.Healthy.ToCode(),
            EconomyRuleSet.OpeningSeats.Total,
            StadiumRuleSet.LevelFor(EconomyRuleSet.OpeningSeats.Total),
            StructureRuleSet.OpeningLevel,
            StructureRuleSet.OpeningLevel,
            fresh.ForecastMinor,
            fresh.WageBudgetMinor,
            fresh.OverdraftLimitMinor);

        return new TakeoverPreviewResult(
            TakeoverPreviewOutcome.Found,
            new TakeoverPreviewResponse(club.Id, club.Name, tier, keep, cleanStart));
    }

    /// <summary>
    /// Turns the strength scale (hundredths of a point of ability) into the shown ability, to a tenth. The board's choice between players it rates
    /// equally falls on their ids, which are new when the squad is drawn again, so the hundredths of a drawn squad can move; a tenth is steady.
    /// </summary>
    private static decimal Shown(int strength) => decimal.Round(strength / 100m, 1, MidpointRounding.AwayFromZero);

    private static TakeoverPreviewResult Refused(TakeoverPreviewOutcome outcome) => new(outcome, null);
}
