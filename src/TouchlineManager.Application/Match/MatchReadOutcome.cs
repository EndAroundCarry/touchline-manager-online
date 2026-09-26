namespace TouchlineManager.Application.Match;

/// <summary>
/// Why a match read succeeded or was refused.
/// </summary>
/// <remarks>
/// One outcome is enough. A match is public game data read by identity, so there is no ownership to refuse,
/// and a result that has not published is answered as not found rather than as a distinct state: a match id
/// reaches a client only once its fixture published (`MAT-7`), so "unknown" and "not yet visible" are the
/// same answer to a manager.
/// </remarks>
public enum MatchReadOutcome
{
    /// <summary>The read succeeded.</summary>
    Found = 0,

    /// <summary>No published match exists with the requested identity.</summary>
    MatchNotFound = 1,
}
