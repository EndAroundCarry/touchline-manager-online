using Tactical11Manager.Application.Abstractions.Squad;
using Tactical11Manager.Contracts.Squad;
using Tactical11Manager.Domain.Market;
using Tactical11Manager.Domain.Rules;
using Tactical11Manager.Domain.Squad;

namespace Tactical11Manager.Application.Squad;

/// <summary>Why a release quote or a release succeeded or was refused (`CON-12`).</summary>
public enum ContractReleaseOutcome
{
    /// <summary>A quote was produced.</summary>
    Quoted = 0,

    /// <summary>The player was released.</summary>
    Released = 1,

    /// <summary>No world has been seeded.</summary>
    WorldNotSeeded = 2,

    /// <summary>The account has no manager profile.</summary>
    NoManagerProfile = 3,

    /// <summary>The manager holds no club.</summary>
    NoClub = 4,

    /// <summary>The club, or its account, no longer exists.</summary>
    ClubNotFound = 5,

    /// <summary>No active contract with that identity belongs to the caller's club.</summary>
    ContractNotFound = 6,

    /// <summary>The command is conditional and no <c>If-Match</c> version was supplied (`CONC-1`).</summary>
    PreconditionRequired = 7,

    /// <summary>The supplied version was stale. The client must reapply against current state.</summary>
    PreconditionFailed = 8,

    /// <summary>The player has an open transfer listing, which must be withdrawn first.</summary>
    PlayerListed = 9,

    /// <summary>The release would leave the club below the minimum squad (`SQ-2`).</summary>
    SquadTooSmall = 10,

    /// <summary>The release would leave the club with fewer than two goalkeepers (`SQ-2`).</summary>
    GoalkeepersTooFew = 11,

    /// <summary>The season is closing, when no contract may change (`CON-8`).</summary>
    SeasonRollover = 12,

    /// <summary>The club's available cash cannot cover the payout (`FIN-10`).</summary>
    InsufficientFunds = 13,
}

/// <summary>The result of asking for a release quote.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Quote">The quote, when one was produced.</param>
public sealed record RequestReleaseQuoteResult(ContractReleaseOutcome Outcome, ReleaseQuoteResponse? Quote);

/// <summary>The result of releasing a player.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Release">The release, when the player was released.</param>
public sealed record ReleasePlayerResult(ContractReleaseOutcome Outcome, ReleasePlayerResponse? Release);

/// <summary>Shared rules for the two release use cases, so neither restates the other's (`CON-12`).</summary>
public static class ContractRelease
{
    /// <summary>Prices a release from the contract's facts and the calendar.</summary>
    public static ReleaseQuote Price(ReleaseContext context, DateTimeOffset now) =>
        ReleaseCost.Calculate(
            context.WeeklyWageMinor,
            now,
            context.CurrentSeasonEndsAt,
            Math.Max(0, context.EndSeasonNumber - context.CurrentSeasonNumber),
            context.WeeksPerSeason);

    /// <summary>
    /// The first rule that refuses the release, or null when it is allowed. The order is the order a manager
    /// can act on: nothing changes during rollover, then the listing, then the squad, then the money.
    /// </summary>
    public static ContractReleaseOutcome? Refusal(ReleaseContext context, ReleaseQuote quote, long availableMinor)
    {
        if (context.IsRollover)
        {
            return ContractReleaseOutcome.SeasonRollover;
        }

        if (context.IsListed)
        {
            return ContractReleaseOutcome.PlayerListed;
        }

        if (!SquadLegality.MeetsMinimum(context.RegisteredCount - 1))
        {
            return ContractReleaseOutcome.SquadTooSmall;
        }

        var remainingGoalkeepers = context.GoalkeeperCount
            - (context.PrimaryPosition == PlayerPosition.Goalkeeper ? 1 : 0);

        if (remainingGoalkeepers < WorldRuleSet.MinimumGoalkeepers)
        {
            return ContractReleaseOutcome.GoalkeepersTooFew;
        }

        return quote.PayoutMinor > availableMinor ? ContractReleaseOutcome.InsufficientFunds : null;
    }

    /// <summary>When a player released at an instant joins the pool: the next weekly batch (`FA-3`).</summary>
    public static DateTimeOffset JoinsPoolAt(DateTimeOffset now) =>
        FreeAgentWeeks.OpensAt(FreeAgentWeeks.WeekStartAtOrBefore(now).AddDays(7));

    /// <summary>The stable code a client branches on for a refusal.</summary>
    public static string? CodeOf(ContractReleaseOutcome? outcome) => outcome switch
    {
        ContractReleaseOutcome.SeasonRollover => SquadErrorCodes.ReleaseSeasonRollover,
        ContractReleaseOutcome.PlayerListed => SquadErrorCodes.ReleasePlayerListed,
        ContractReleaseOutcome.SquadTooSmall => SquadErrorCodes.ReleaseSquadTooSmall,
        ContractReleaseOutcome.GoalkeepersTooFew => SquadErrorCodes.ReleaseGoalkeepersTooFew,
        ContractReleaseOutcome.InsufficientFunds => SquadErrorCodes.ReleaseInsufficientFunds,
        _ => null,
    };

    /// <summary>Carries an ownership refusal over into the release vocabulary.</summary>
    public static ContractReleaseOutcome FromAccess(ClubAccessOutcome outcome) => outcome switch
    {
        ClubAccessOutcome.WorldNotSeeded => ContractReleaseOutcome.WorldNotSeeded,
        ClubAccessOutcome.NoManagerProfile => ContractReleaseOutcome.NoManagerProfile,
        ClubAccessOutcome.NoClub => ContractReleaseOutcome.NoClub,
        _ => ContractReleaseOutcome.ClubNotFound,
    };
}
