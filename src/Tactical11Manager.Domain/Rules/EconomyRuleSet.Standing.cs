using Tactical11Manager.Domain.Finance;

namespace Tactical11Manager.Domain.Rules;

// A club's standing: the slow lever behind how long it takes to become rich (plan toasty-scott, section 4).
public static partial class EconomyRuleSet
{
    /// <summary>The standing of a tier-1 club that has just been founded, on the 1–100 scale.</summary>
    public const int OpeningStandingTier1 = 50;

    /// <summary>How much lower the opening standing is for each tier below the first.</summary>
    public const int OpeningStandingDropPerTier = 10;

    /// <summary>The lowest standing a club can have.</summary>
    public const int StandingMin = 1;

    /// <summary>The highest standing a club can have.</summary>
    public const int StandingMax = 100;

    /// <summary>The share of the distance to the tier's opening standing a club's standing falls back by at each rollover, in basis points: 10%.</summary>
    public const int StandingPullBp = 1_000;

    /// <summary>The standing a club in the given tier starts with and is pulled back toward.</summary>
    /// <param name="tier">The tier number, starting at 1.</param>
    public static int OpeningStandingFor(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);

        return Math.Max(StandingMin, OpeningStandingTier1 - (OpeningStandingDropPerTier * (tier - 1)));
    }

    /// <summary>What a final place does to a club's standing, in points: the champion +6, the top three +4, the top six +2, the bottom six of the table -2 unless it was relegated, a relegated club -5.</summary>
    /// <param name="place">The final place, 1 to <see cref="ClubsPerDivision"/>.</param>
    /// <param name="relegated">Whether the place sent the club down.</param>
    public static int StandingFinishDelta(int place, bool relegated)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(place, 1);

        if (relegated)
        {
            return -5;
        }

        return place switch
        {
            1 => 6,
            <= 3 => 4,
            <= 6 => 2,
            > ClubsPerDivision - 6 => -2,
            _ => 0,
        };
    }

    /// <summary>What a season objective's outcome does to a club's standing, in points: met +1, exceeded +2, missed -2.</summary>
    /// <param name="outcome">How the objective turned out.</param>
    public static int StandingObjectiveDelta(ObjectiveOutcome outcome) => outcome switch
    {
        ObjectiveOutcome.Exceeded => 2,
        ObjectiveOutcome.Met => 1,
        ObjectiveOutcome.Missed => -2,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown objective outcome."),
    };
}
