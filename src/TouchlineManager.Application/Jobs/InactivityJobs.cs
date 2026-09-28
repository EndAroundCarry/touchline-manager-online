using System.Globalization;

namespace TouchlineManager.Application.Jobs;

/// <summary>The inactivity ladder's job identity (`OCC-1`–`OCC-3`, ADR-0027).</summary>
public static class InactivityJobTypes
{
    /// <summary>Evaluates the inactivity ladder across the membership.</summary>
    public const string Evaluate = "world.evaluate-inactivity";

    /// <summary>The business key for a UTC day, so the ladder runs at most once a day.</summary>
    /// <param name="day">The UTC day.</param>
    public static string DayKey(DateOnly day) =>
        string.Create(CultureInfo.InvariantCulture, $"{Evaluate}:{day:yyyy-MM-dd}");
}
