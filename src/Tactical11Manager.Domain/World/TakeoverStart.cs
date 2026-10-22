namespace Tactical11Manager.Domain.World;

/// <summary>How a new manager takes a club over (`TKO-1`).</summary>
public enum TakeoverStart
{
    /// <summary>The manager inherits everything: squad, cash or debt, ground and buildings, as they stand.</summary>
    Keep = 0,

    /// <summary>
    /// The old players are released to the free agents and the club returns to its tier's opening squad, ground, buildings and cash, with no
    /// debt (`TKO-2`, `TKO-3`).
    /// </summary>
    CleanStart = 1,
}

/// <summary>Stable codes and parsing for <see cref="TakeoverStart"/>.</summary>
public static class TakeoverStarts
{
    /// <summary>The code for <see cref="TakeoverStart.Keep"/>.</summary>
    public const string KeepCode = "keep";

    /// <summary>The code for <see cref="TakeoverStart.CleanStart"/>.</summary>
    public const string CleanStartCode = "clean_start";

    /// <summary>Every start, in the order a manager is offered them.</summary>
    public static readonly IReadOnlyList<TakeoverStart> All = [TakeoverStart.Keep, TakeoverStart.CleanStart];

    /// <summary>Converts a start to its stable code.</summary>
    /// <param name="start">The start.</param>
    public static string ToCode(this TakeoverStart start) => start switch
    {
        TakeoverStart.Keep => KeepCode,
        TakeoverStart.CleanStart => CleanStartCode,
        _ => throw new ArgumentOutOfRangeException(nameof(start), start, "Unknown takeover start."),
    };

    /// <summary>Parses a stable code back to its start.</summary>
    /// <param name="code">The stable code.</param>
    public static TakeoverStart FromCode(string code) => TryFromCode(code, out var start)
        ? start
        : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown takeover start code.");

    /// <summary>Parses a stable code, or reports that it is not one.</summary>
    /// <param name="code">The code, or null.</param>
    /// <param name="start">The start, when the code is one.</param>
    public static bool TryFromCode(string? code, out TakeoverStart start)
    {
        switch (code)
        {
            case KeepCode:
                start = TakeoverStart.Keep;

                return true;
            case CleanStartCode:
                start = TakeoverStart.CleanStart;

                return true;
            default:
                start = TakeoverStart.Keep;

                return false;
        }
    }
}
