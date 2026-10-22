namespace Tactical11Manager.Domain.Rules;

// Debt: in the red, administration and dismissal (plan toasty-scott, section 6).
public static partial class EconomyRuleSet
{
    /// <summary>The matchdays a club may stay in administration before its manager is dismissed.</summary>
    public const int AdministrationDismissalMatchdays = 17;

    /// <summary>The most players the board lists in one administration pass.</summary>
    public const int AdministrationMaxListings = 5;

    /// <summary>The price the board asks for a player it lists, as a share of his value, in basis points: 80%.</summary>
    public const int AdministrationAskingBp = 8_000;

    /// <summary>
    /// What the player's agent pays the club when the board releases a player nobody bought, as a share of his value, in basis points: 30%. The
    /// club gets something for him even when nobody can afford him; no release payout is owed, and he joins the free agents at his value.
    /// </summary>
    public const int AdministrationReleaseAgentBp = 3_000;

    /// <summary>What the agent pays a club for a player the board releases, in minor units.</summary>
    /// <param name="valueMinor">The player's value.</param>
    public static long AdministrationReleaseFeeMinor(long valueMinor) => Math.Max(0, valueMinor) * AdministrationReleaseAgentBp / OneHundredPercentBp;

    /// <summary>
    /// How deep, as a multiple of the overdraft limit, the cash may go before the emergency grant (`FIN-16`) steps in as the integrity net.
    /// Tuning aims at it never firing.
    /// </summary>
    public const int SafetyNetLimitMultiple = 2;

    /// <summary>The cash below which the emergency grant steps in, in minor units: twice the overdraft limit below zero.</summary>
    /// <param name="overdraftLimitMinor">The club's overdraft limit.</param>
    public static long SafetyNetFloorMinor(long overdraftLimitMinor) => -overdraftLimitMinor * SafetyNetLimitMultiple;
}
