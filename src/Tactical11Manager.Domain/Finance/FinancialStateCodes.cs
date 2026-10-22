namespace Tactical11Manager.Domain.Finance;

/// <summary>Stable codes for <see cref="FinancialStateKind"/>, so a stored value never moves with the enum's order.</summary>
public static class FinancialStateCodes
{
    /// <summary>The longest code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 16;

    /// <summary>The codes of every state.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Enum.GetValues<FinancialStateKind>().Select(state => state.ToCode())];

    /// <summary>The code of a state.</summary>
    /// <param name="state">The state.</param>
    public static string ToCode(this FinancialStateKind state) => state switch
    {
        FinancialStateKind.Healthy => "healthy",
        FinancialStateKind.InTheRed => "in_the_red",
        FinancialStateKind.Administration => "administration",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown financial state."),
    };

    /// <summary>The state a stored code stands for.</summary>
    /// <param name="code">The code.</param>
    public static FinancialStateKind FromCode(string code) => code switch
    {
        "healthy" => FinancialStateKind.Healthy,
        "in_the_red" => FinancialStateKind.InTheRed,
        "administration" => FinancialStateKind.Administration,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown financial state code."),
    };
}
