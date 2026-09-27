namespace TouchlineManager.Domain.Comms;

/// <summary>
/// The kind of thing an inbox message reports, which the screen groups by (master plan §6.9, §11.1).
/// </summary>
/// <remarks>
/// A category is presentation vocabulary, not a rule: it says which shelf a message sits on rather than what
/// happened. What happened is the <see cref="InboxMessage.TemplateKey"/>, and the values here are stable
/// codes because a client branches on them.
/// </remarks>
public enum InboxCategory
{
    /// <summary>A result the club played.</summary>
    Result = 0,

    /// <summary>The club's place in its table moved.</summary>
    Table = 1,

    /// <summary>A player's cards earned a suspension (`DIS-2`, `DIS-4`).</summary>
    Discipline = 2,

    /// <summary>A player was injured (`DIS-1`).</summary>
    Injury = 3,

    /// <summary>The club's own side needed a repair (`DIS-7`).</summary>
    Squad = 4,
}

/// <summary>Stable codes and parsing for <see cref="InboxCategory"/>.</summary>
public static class InboxCategories
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 10;

    /// <summary>Every category, in declaration order.</summary>
    public static readonly IReadOnlyList<InboxCategory> All = [.. Enum.GetValues<InboxCategory>()];

    /// <summary>Converts a category to its stable code.</summary>
    /// <param name="category">The category.</param>
    public static string ToCode(this InboxCategory category) => category switch
    {
        InboxCategory.Result => "result",
        InboxCategory.Table => "table",
        InboxCategory.Discipline => "discipline",
        InboxCategory.Injury => "injury",
        InboxCategory.Squad => "squad",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown inbox category."),
    };

    /// <summary>Parses a stable code back to its category.</summary>
    /// <param name="code">The stable code.</param>
    public static InboxCategory FromCode(string code) => code switch
    {
        "result" => InboxCategory.Result,
        "table" => InboxCategory.Table,
        "discipline" => InboxCategory.Discipline,
        "injury" => InboxCategory.Injury,
        "squad" => InboxCategory.Squad,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown inbox category code."),
    };
}
