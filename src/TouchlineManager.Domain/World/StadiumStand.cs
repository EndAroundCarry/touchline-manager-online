namespace TouchlineManager.Domain.World;

/// <summary>
/// The four kinds of place a club's stadium sells (master plan §6.8, game rules §13.5).
/// </summary>
/// <remarks>
/// A stand is the durable answer to "what kind of ticket is this", so it has a stable code rather than an
/// ordinal: a client keys its labels and its prices on the code, and a reordered enum must not move a club's
/// seats between kinds. The order is the order a manager reads them in — cheapest first.
/// </remarks>
public enum StadiumStand
{
    /// <summary>Standing room on the terraces: the cheapest place, open to the weather.</summary>
    Standing = 0,

    /// <summary>An uncovered seat.</summary>
    Seating = 1,

    /// <summary>A seat under the roof.</summary>
    CoveredSeating = 2,

    /// <summary>A hospitality seat: few of them, and the most expensive place in the ground.</summary>
    Vip = 3,
}

/// <summary>Stable codes and parsing for <see cref="StadiumStand"/>.</summary>
public static class StadiumStands
{
    /// <summary>Every stand, in the order a manager reads them.</summary>
    public static readonly IReadOnlyList<StadiumStand> All = [.. Enum.GetValues<StadiumStand>()];

    /// <summary>Converts a stand to its stable code.</summary>
    /// <param name="stand">The stand.</param>
    public static string ToCode(this StadiumStand stand) => stand switch
    {
        StadiumStand.Standing => "standing",
        StadiumStand.Seating => "seating",
        StadiumStand.CoveredSeating => "covered_seating",
        StadiumStand.Vip => "vip",
        _ => throw new ArgumentOutOfRangeException(nameof(stand), stand, "Unknown stadium stand."),
    };

    /// <summary>Parses a stable code back to its stand.</summary>
    /// <param name="code">The stable code.</param>
    public static StadiumStand FromCode(string code) => TryFromCode(code, out var stand)
        ? stand
        : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown stadium stand code.");

    /// <summary>Parses a stable code, reporting failure rather than throwing, for a request's input.</summary>
    /// <param name="code">The stable code, or null.</param>
    /// <param name="stand">The stand, when the code is known.</param>
    public static bool TryFromCode(string? code, out StadiumStand stand)
    {
        switch (code)
        {
            case "standing":
                stand = StadiumStand.Standing;
                return true;
            case "seating":
                stand = StadiumStand.Seating;
                return true;
            case "covered_seating":
                stand = StadiumStand.CoveredSeating;
                return true;
            case "vip":
                stand = StadiumStand.Vip;
                return true;
            default:
                stand = default;
                return false;
        }
    }
}
