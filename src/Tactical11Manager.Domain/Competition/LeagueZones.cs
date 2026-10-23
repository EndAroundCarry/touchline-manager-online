using Tactical11Manager.Domain.Rules;

namespace Tactical11Manager.Domain.Competition;

/// <summary>What a table place leads to at the end of the season (`PR-1`).</summary>
public enum LeagueZone
{
    /// <summary>The place decides nothing about next season's league.</summary>
    None = 0,

    /// <summary>The club goes up without a play-off.</summary>
    Promotion = 1,

    /// <summary>The club plays a promotion play-off: the 3rd and 4th of a lower league, or the 14th of a higher one.</summary>
    Playoff = 2,

    /// <summary>The club goes down without a play-off.</summary>
    Relegation = 3,
}

/// <summary>The table zones of a league, which the table page names in words (`PR-1`).</summary>
public static class LeagueZones
{
    /// <summary>Gets the zone of a table place.</summary>
    /// <param name="hasParent">Whether the league feeds a higher one, so its top places can go up.</param>
    /// <param name="hasFeeder">Whether a lower league feeds it, so its bottom places can go down.</param>
    /// <param name="place">The 1-based table place.</param>
    public static LeagueZone For(bool hasParent, bool hasFeeder, int place)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(place, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(place, WorldRuleSet.ClubsPerDivision);

        if (hasParent)
        {
            if (place <= WorldRuleSet.DirectPromotionPlaces)
            {
                return LeagueZone.Promotion;
            }

            if (place is WorldRuleSet.PlayoffSemiHomePlace or WorldRuleSet.PlayoffSemiAwayPlace)
            {
                return LeagueZone.Playoff;
            }
        }

        if (hasFeeder)
        {
            if (place == WorldRuleSet.PlayoffHolderPlace)
            {
                return LeagueZone.Playoff;
            }

            if (place > WorldRuleSet.ClubsPerDivision - WorldRuleSet.DirectRelegationPlaces)
            {
                return LeagueZone.Relegation;
            }
        }

        return LeagueZone.None;
    }

    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this LeagueZone zone) => zone switch
    {
        LeagueZone.None => "none",
        LeagueZone.Promotion => "promotion",
        LeagueZone.Playoff => "playoff",
        LeagueZone.Relegation => "relegation",
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown league zone."),
    };

    /// <summary>The words a manager reads for the zone, empty for none.</summary>
    public static string Label(this LeagueZone zone) => zone switch
    {
        LeagueZone.None => string.Empty,
        LeagueZone.Promotion => "Promotion",
        LeagueZone.Playoff => "Play-off",
        LeagueZone.Relegation => "Relegation",
        _ => throw new ArgumentOutOfRangeException(nameof(zone), zone, "Unknown league zone."),
    };
}
