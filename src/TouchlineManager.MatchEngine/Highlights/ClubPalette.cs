namespace TouchlineManager.MatchEngine.Highlights;

/// <summary>
/// The two sides' colours, from small generated palettes chosen by club identity.
/// </summary>
/// <remarks>
/// <para>
/// Generated rather than supplied, and drawn from a fixed palette rather than from any real kit: the engine
/// never has a real club's colours to leak (`WORLD-3`), and the renderer's requirement not to distinguish
/// teams by colour alone is met downstream by shirt numbers and labels.
/// </para>
/// <para>
/// One palette, shared by the highlights and the lineups. A goal flashed in a colour and the lineup panel
/// drawn in a different one would read as two clubs, so the second colour is the pair of the first rather
/// than a second derivation — the primary is what the pitch uses and the secondary is the trim the match
/// center draws beside it.
/// </para>
/// </remarks>
public static class ClubPalette
{
    /// <summary>Gets the primary colour a club's kit is drawn in.</summary>
    /// <param name="clubId">The club identity.</param>
    public static string PrimaryOf(Guid clubId) => Palette[Index(clubId)].Primary;

    /// <summary>Gets the secondary colour the match center pairs with the primary.</summary>
    /// <param name="clubId">The club identity.</param>
    public static string SecondaryOf(Guid clubId) => Palette[Index(clubId)].Secondary;

    /// <summary>The pair of colours each palette entry carries, in a fixed order.</summary>
    private static readonly (string Primary, string Secondary)[] Palette =
    [
        ("#1f4e79", "#d6e4f0"),
        ("#8c2f39", "#f2e3e5"),
        ("#2d6a4f", "#dcefe4"),
        ("#6a4c93", "#e8e0f5"),
        ("#b5651d", "#fbe8d4"),
        ("#2c3e50", "#d5dde5"),
        ("#a4133c", "#f7dbe2"),
        ("#006d77", "#d4eef0"),
    ];

    /// <summary>Maps a club identity onto the palette, stably across runs and platforms.</summary>
    /// <param name="clubId">The club identity.</param>
    private static int Index(Guid clubId)
    {
        var bytes = clubId.ToByteArray();
        var hash = 2166136261u;

        foreach (var value in bytes)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return (int)(hash % (uint)Palette.Length);
    }
}
