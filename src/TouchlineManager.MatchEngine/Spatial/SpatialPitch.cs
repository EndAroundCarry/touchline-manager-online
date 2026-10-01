namespace TouchlineManager.MatchEngine.Spatial;

/// <summary>
/// A 2D point on the normalized pitch (0..10,000 X, 0..7,000 Y).
/// </summary>
public readonly record struct SpatialPoint(int X, int Y)
{
    public static readonly SpatialPoint Center = new(5000, 3500);

    public double DistanceTo(SpatialPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public SpatialPoint Clamp() =>
        new(Math.Clamp(X, 0, SpatialPitch.PitchLength), Math.Clamp(Y, 0, SpatialPitch.PitchWidth));
}

/// <summary>
/// Physical dimensions and coordinate systems for the spatial match simulation.
/// </summary>
public static class SpatialPitch
{
    public const int PitchLength = 10_000;
    public const int PitchWidth = 7_000;

    public const int GoalYMin = 3_000;
    public const int GoalYMax = 4_000;
    public const int GoalYCenter = 3_500;

    public const int HomeGoalX = 0;
    public const int AwayGoalX = 10_000;

    public const int PenaltyBoxWidth = 1_650;
    public const int PenaltyBoxHeight = 4_000;
    public const int PenaltyBoxYMin = 1_500;
    public const int PenaltyBoxYMax = 5_500;

    public const int PenaltySpotHomeX = 1_100;
    public const int PenaltySpotAwayX = 8_900;
    public const int PenaltySpotY = 3_500;

    /// <summary>Returns true if the point is inside the home penalty box.</summary>
    public static bool IsInHomePenaltyBox(SpatialPoint p) =>
        p.X <= PenaltyBoxWidth && p.Y >= PenaltyBoxYMin && p.Y <= PenaltyBoxYMax;

    /// <summary>Returns true if the point is inside the away penalty box.</summary>
    public static bool IsInAwayPenaltyBox(SpatialPoint p) =>
        p.X >= (PitchLength - PenaltyBoxWidth) && p.Y >= PenaltyBoxYMin && p.Y <= PenaltyBoxYMax;

    /// <summary>Gets the target goal line coordinate for the attacking side.</summary>
    public static SpatialPoint OpponentGoalCenter(bool isHome) =>
        isHome ? new SpatialPoint(AwayGoalX, GoalYCenter) : new SpatialPoint(HomeGoalX, GoalYCenter);

    /// <summary>Mirrors tactical coordinates for away side (attacking from right to left).</summary>
    public static SpatialPoint Orient(int x, int y, bool isHome) =>
        isHome ? new SpatialPoint(x, y * PitchWidth / 10_000) : new SpatialPoint(PitchLength - x, PitchWidth - (y * PitchWidth / 10_000));
}
