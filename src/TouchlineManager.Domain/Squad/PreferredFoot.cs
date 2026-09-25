namespace TouchlineManager.Domain.Squad;

/// <summary>The foot a generated player favours (master plan §6.5).</summary>
public enum PreferredFoot
{
    /// <summary>Right-footed.</summary>
    Right = 0,

    /// <summary>Left-footed.</summary>
    Left = 1,

    /// <summary>Comfortable on either foot.</summary>
    Both = 2,
}

/// <summary>Stable codes and storage representation for <see cref="PreferredFoot"/>.</summary>
public static class PreferredFeet
{
    /// <summary>The code for <see cref="PreferredFoot.Right"/>.</summary>
    public const string RightCode = "right";

    /// <summary>The code for <see cref="PreferredFoot.Left"/>.</summary>
    public const string LeftCode = "left";

    /// <summary>The code for <see cref="PreferredFoot.Both"/>.</summary>
    public const string BothCode = "both";

    /// <summary>The longest stable code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 5;

    /// <summary>Converts a foot to its stable code.</summary>
    /// <param name="foot">The preferred foot.</param>
    public static string ToCode(this PreferredFoot foot) => foot switch
    {
        PreferredFoot.Right => RightCode,
        PreferredFoot.Left => LeftCode,
        PreferredFoot.Both => BothCode,
        _ => throw new ArgumentOutOfRangeException(nameof(foot), foot, "Unknown preferred foot."),
    };

    /// <summary>Parses a stable code back to its foot.</summary>
    /// <param name="code">The stable code.</param>
    public static PreferredFoot FromCode(string code) => code switch
    {
        RightCode => PreferredFoot.Right,
        LeftCode => PreferredFoot.Left,
        BothCode => PreferredFoot.Both,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown preferred foot code."),
    };
}
