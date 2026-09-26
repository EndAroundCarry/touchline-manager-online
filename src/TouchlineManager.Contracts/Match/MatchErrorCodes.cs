namespace TouchlineManager.Contracts.Match;

/// <summary>
/// The stable refusal codes the match read surface returns.
/// </summary>
/// <remarks>
/// One code is enough because a match is only ever readable once its fixture has published: an unknown
/// identity and a result that has not been published yet are the same answer to a manager, since a match
/// id reaches a client only after publication (`MAT-7`).
/// </remarks>
public static class MatchErrorCodes
{
    /// <summary>No published match exists with the requested identity.</summary>
    public const string MatchNotFound = "MATCH_NOT_FOUND";
}
