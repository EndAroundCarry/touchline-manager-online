namespace Tactical11Manager.Domain.World;

/// <summary>
/// The two buildings a club owns beside its ground (game rules §13.7, `STRC-1`).
/// </summary>
/// <remarks>
/// A kind is the durable answer to "which building is this", so it has a stable code rather than an
/// ordinal: a client keys its tabs and its labels on the code, and a reordered enum must not move a club's
/// levels between buildings.
/// </remarks>
public enum StructureKind
{
    /// <summary>The snack bar: earns for every spectator at a home match.</summary>
    SnackBar = 0,

    /// <summary>The merchandise shop: earns for every fan of the club, every week.</summary>
    Merchandise = 1,
}

/// <summary>Stable codes and parsing for <see cref="StructureKind"/>.</summary>
public static class StructureKinds
{
    /// <summary>Every kind, in the order a manager reads them.</summary>
    public static readonly IReadOnlyList<StructureKind> All = [.. Enum.GetValues<StructureKind>()];

    /// <summary>Converts a kind to its stable code.</summary>
    /// <param name="kind">The kind.</param>
    public static string ToCode(this StructureKind kind) => kind switch
    {
        StructureKind.SnackBar => "snack_bar",
        StructureKind.Merchandise => "merchandise",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown structure kind."),
    };

    /// <summary>Parses a stable code back to its kind.</summary>
    /// <param name="code">The stable code.</param>
    public static StructureKind FromCode(string code) => TryFromCode(code, out var kind)
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown structure kind code.");

    /// <summary>Parses a stable code, reporting failure rather than throwing, for a request's input.</summary>
    /// <param name="code">The stable code, or null.</param>
    /// <param name="kind">The kind, when the code is known.</param>
    public static bool TryFromCode(string? code, out StructureKind kind)
    {
        switch (code)
        {
            case "snack_bar":
                kind = StructureKind.SnackBar;
                return true;
            case "merchandise":
                kind = StructureKind.Merchandise;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
