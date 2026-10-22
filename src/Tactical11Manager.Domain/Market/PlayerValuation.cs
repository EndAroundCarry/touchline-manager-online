using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Market;

/// <summary>Everything a transfer valuation is derived from (`TRF-12`).</summary>
/// <remarks>
/// What the market can see of a player: his skill and his age. His hidden potential is not an input, so a value can never give a gem away and
/// can never be wrong in the other direction either: the market prices what a player of that age is assumed to become, and a real gem is
/// under-priced and a flop over-priced (`player-valuation-v2`). The club's tier is not an input either: the same player is worth the same
/// everywhere.
/// </remarks>
/// <param name="AbilityTenths">The mean of the player's attributes in tenths of a point, so 115 is 11.5.</param>
/// <param name="Age">The player's age in game years.</param>
public sealed record PlayerValuationInput(int AbilityTenths, int Age)
{
    /// <summary>The input for a player known by his whole-point ability only.</summary>
    /// <param name="ability">The player's ability, 1–20.</param>
    /// <param name="age">The player's age in game years.</param>
    public static PlayerValuationInput FromAbility(int ability, int age) => new(ability * 10, age);
}

/// <summary>
/// The deterministic transfer value of a player (`TRF-12`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function, so the AI's asking price and bid ceiling are reproducible from the facts alone. The value grows exponentially
/// with skill, about ×1.6 for each point and steeper above 17, and is moved by the market's view of the player's age
/// (<see cref="EconomyRuleSet.PlayerValueMinor"/>). Training a player raises his value, and the age curve rewards selling at 24 to 26.
/// </para>
/// <para>
/// The value is what a club asks and bids, and a manager sees it as a listing's price, never as a number of its own (`MAT-11`).
/// </para>
/// </remarks>
public static class PlayerValuation
{
    /// <summary>The version label, so a change to the valuation is a named rule change (`FIC-8`, `RULE-3`).</summary>
    public const string Version = "player-valuation-v2";

    /// <summary>Estimates a player's transfer value, in minor units.</summary>
    /// <param name="input">The facts the value is derived from.</param>
    /// <exception cref="ArgumentOutOfRangeException">When a fact is off its scale.</exception>
    public static long Estimate(PlayerValuationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.AbilityTenths is < WorldRuleSet.AttributeMin * 10 or > WorldRuleSet.AttributeMax * 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                input.AbilityTenths,
                $"A skill is between {WorldRuleSet.AttributeMin} and {WorldRuleSet.AttributeMax} (TRN-4).");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(input.Age);

        return EconomyRuleSet.PlayerValueMinor(input.AbilityTenths, input.Age);
    }
}
