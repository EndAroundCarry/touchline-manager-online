namespace TouchlineManager.Domain.World;

/// <summary>
/// The identity a generator produced for a club (`WORLD-3`).
/// </summary>
/// <remarks>
/// <para>
/// Keeping the generated identity as one value makes the generator's contract explicit and gives the
/// reproducibility rule (`PYR-14`) something testable: the same seed and generator version must produce
/// equal <see cref="ClubIdentity"/> values, so a test can compare identities without a database.
/// </para>
/// <para>
/// The URL slug and the comparison form of the name are deliberately <em>not</em> here. They are
/// derived by <see cref="Club"/> from the name, so a generated club cannot end up with a slug that
/// disagrees with the name it is presented under.
/// </para>
/// </remarks>
/// <param name="Name">The full fictional club name.</param>
/// <param name="ShortName">The abbreviated name used in tables and the match viewer.</param>
/// <param name="City">The generated home city.</param>
/// <param name="Region">The generated region or state.</param>
/// <param name="BadgeSeed">The deterministic seed a badge is drawn from. No real mark is involved.</param>
public sealed record ClubIdentity(
    string Name,
    string ShortName,
    string City,
    string Region,
    string BadgeSeed);
