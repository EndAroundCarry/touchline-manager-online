namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// The version of the whole world bootstrap: club identity plus the squads generated for those clubs.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClubIdentityGenerator.Version"/> and <c>PlayerGenerator.Version</c> version their own
/// algorithms, but a generation run produces both, and the field a run records has to describe the run.
/// This is that version, and it is what <c>GenerationRun.GeneratorVersion</c> is stamped with
/// (`FIC-8`, `PYR-14`).
/// </para>
/// <para>
/// It is <c>world-gen-v2</c> because it succeeds the Stage 3 bootstrap, which stamped
/// <c>ClubIdentityGenerator.Version</c> (<c>world-gen-v1</c>) and produced no players. The sub-generator
/// versions are folded into the run's input hash as well, so a change to either is visible even if this
/// constant were left alone.
/// </para>
/// </remarks>
public static class WorldBootstrapGenerator
{
    /// <summary>The version stamped onto every world-bootstrap generation run.</summary>
    public const string Version = "world-gen-v2";
}
