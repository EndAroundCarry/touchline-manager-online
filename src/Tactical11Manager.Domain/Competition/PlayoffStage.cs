namespace Tactical11Manager.Domain.Competition;

/// <summary>Which match of the promotion bracket a tie is (`PR-4`).</summary>
public enum PlayoffStage
{
    /// <summary>The lower league's 3rd against its 4th, at the 3rd's ground.</summary>
    SemiFinal = 0,

    /// <summary>The higher league's 14th against the semi-final winner, at the 14th's ground.</summary>
    Final = 1,
}

/// <summary>How a play-off tie was settled (`PR-6`).</summary>
public enum PlayoffDecision
{
    /// <summary>The 90-minute score separated the clubs.</summary>
    Score = 0,

    /// <summary>The score was level and a penalty shoot-out separated them.</summary>
    Penalties = 1,
}

/// <summary>The questions asked about <see cref="PlayoffStage"/>.</summary>
public static class PlayoffStageRules
{
    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this PlayoffStage stage) => stage switch
    {
        PlayoffStage.SemiFinal => PlayoffStages.SemiFinalCode,
        PlayoffStage.Final => PlayoffStages.FinalCode,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown play-off stage."),
    };

    /// <summary>Parses a stable code back to its stage.</summary>
    public static PlayoffStage FromCode(string code) => code switch
    {
        PlayoffStages.SemiFinalCode => PlayoffStage.SemiFinal,
        PlayoffStages.FinalCode => PlayoffStage.Final,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown play-off stage code."),
    };

    /// <summary>The words a manager reads for the stage.</summary>
    public static string Label(this PlayoffStage stage) => stage switch
    {
        PlayoffStage.SemiFinal => "Play-off semi-final",
        PlayoffStage.Final => "Play-off final",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown play-off stage."),
    };
}

/// <summary>Stable codes for <see cref="PlayoffStage"/>.</summary>
public static class PlayoffStages
{
    /// <summary>The code for <see cref="PlayoffStage.SemiFinal"/>.</summary>
    public const string SemiFinalCode = "semi_final";

    /// <summary>The code for <see cref="PlayoffStage.Final"/>.</summary>
    public const string FinalCode = "final";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 16;
}

/// <summary>The questions asked about <see cref="PlayoffDecision"/>.</summary>
public static class PlayoffDecisionRules
{
    /// <summary>Storage and transport representation.</summary>
    public static string ToCode(this PlayoffDecision decision) => decision switch
    {
        PlayoffDecision.Score => PlayoffDecisions.ScoreCode,
        PlayoffDecision.Penalties => PlayoffDecisions.PenaltiesCode,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown play-off decision."),
    };

    /// <summary>Parses a stable code back to its decision.</summary>
    public static PlayoffDecision FromCode(string code) => code switch
    {
        PlayoffDecisions.ScoreCode => PlayoffDecision.Score,
        PlayoffDecisions.PenaltiesCode => PlayoffDecision.Penalties,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown play-off decision code."),
    };
}

/// <summary>Stable codes for <see cref="PlayoffDecision"/>.</summary>
public static class PlayoffDecisions
{
    /// <summary>The code for <see cref="PlayoffDecision.Score"/>.</summary>
    public const string ScoreCode = "score";

    /// <summary>The code for <see cref="PlayoffDecision.Penalties"/>.</summary>
    public const string PenaltiesCode = "penalties";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 16;
}
