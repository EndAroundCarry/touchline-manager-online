namespace TouchlineManager.Contracts.Status;

/// <summary>
/// The published versions of the versioned documents a consent records (`LGL-1`).
/// </summary>
/// <remarks>
/// The server owns these values (<c>Auth:TermsVersion</c>, <c>Auth:PrivacyVersion</c>) and records them on
/// every acceptance, so a page that names a version must name the server's rather than one authored beside
/// its copy. The rules and support pages are not versioned documents and do not appear here.
/// </remarks>
/// <param name="TermsVersion">The current terms-of-service version.</param>
/// <param name="PrivacyVersion">The current privacy-policy version.</param>
public sealed record DocumentVersionsResponse(
    string TermsVersion,
    string PrivacyVersion);

/// <summary>
/// The public service status and the published document versions, read by any visitor (master plan §16
/// Stage 15, `F-55`, ADR-0050).
/// </summary>
/// <remarks>
/// <para>
/// This is the product's first anonymous read. It carries only public game data and operational facts a
/// visitor or a manager can see anyway: the server's instant, whether the game is in read-only maintenance
/// and why, the running season, the next round's kickoff, and the document versions a consent records. It
/// carries no account, no address, and no hidden value — the shape
/// <c>docs/security/data-classification.md</c> §2 allows a public read.
/// </para>
/// <para>
/// The season and the next matchday are null before a world is seeded, so the status page can say the world
/// has not been started rather than showing a zero that looks like a real season.
/// </para>
/// </remarks>
/// <param name="ServerTime">The server's current instant, so a client with a wrong clock shows the right "as of" (`TIME-5`).</param>
/// <param name="ReadOnly">Whether the game is in read-only maintenance (`F-51`).</param>
/// <param name="ReadOnlyMessage">The operator's stated reason while read-only, otherwise null.</param>
/// <param name="SeasonNumber">The running season's ordinal, or null before the world is seeded.</param>
/// <param name="NextMatchdayAt">The next round's kickoff, or null when the world is unseeded or no round remains.</param>
/// <param name="Documents">The published versions of the versioned documents (`LGL-1`).</param>
public sealed record PublicStatusResponse(
    DateTimeOffset ServerTime,
    bool ReadOnly,
    string? ReadOnlyMessage,
    int? SeasonNumber,
    DateTimeOffset? NextMatchdayAt,
    DocumentVersionsResponse Documents);
