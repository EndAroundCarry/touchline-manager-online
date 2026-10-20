namespace Tactical11Manager.Domain.Fans;

/// <summary>How a team's season has gone so far, as the fans read it (`FAN-3`).</summary>
/// <param name="Games">The league games it has played this season.</param>
/// <param name="Points">The points it took from them.</param>
public readonly record struct FanForm(int Games, int Points)
{
    /// <summary>A team that has not played yet this season.</summary>
    public static FanForm None => new(0, 0);

    /// <summary>Gets the points a game in hundredths, or 0 before the first game.</summary>
    public int PointsPerGameCentis => Games <= 0 ? 0 : (Points * 100) / Games;
}
