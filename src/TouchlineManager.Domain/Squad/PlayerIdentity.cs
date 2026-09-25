namespace TouchlineManager.Domain.Squad;

/// <summary>
/// The identity a generated player is created from (`FIC-4`, `FIC-7`).
/// </summary>
/// <remarks>
/// <para>
/// This is the generator's output and the aggregate's input, exactly as <c>ClubIdentity</c> is for a
/// club. Keeping it separate from <see cref="Player"/> means the deterministic, testable part of
/// generation — the pool lookups and the arithmetic that turns an ordinal into a person — can be
/// asserted without building an aggregate.
/// </para>
/// <para>
/// <see cref="Potential"/> and <see cref="Reputation"/> are class C2: they are stored so the engine and
/// the AI valuation can read them, and they are never mapped to a manager-facing response
/// (`data-classification.md` §2.1).
/// </para>
/// </remarks>
/// <param name="FullName">The generated full name.</param>
/// <param name="ShortName">The abbreviated form shown in tables and lineups.</param>
/// <param name="NationalityCode">The nationality country code. The club's country at MVP.</param>
/// <param name="NameSeed">The stored seed the name was drawn from, so a name can be reproduced alone.</param>
/// <param name="BirthGameYear">The game year the player was born in (`TIME-3`).</param>
/// <param name="BirthDayOfYear">The day of the game year the player was born on, 1–366.</param>
/// <param name="PreferredFoot">The foot the player favours.</param>
/// <param name="HeightCm">The player's height in centimetres.</param>
/// <param name="WeightKg">The player's weight in kilograms.</param>
/// <param name="PrimaryPosition">The position the player is most at home in.</param>
/// <param name="SecondaryPositions">The other positions the player can cover.</param>
/// <param name="Potential">The hidden development ceiling (`TRN-9`). Class C2, never serialized.</param>
/// <param name="Reputation">The hidden generation reputation. Class C2, never serialized.</param>
public sealed record PlayerIdentity(
    string FullName,
    string ShortName,
    string NationalityCode,
    string NameSeed,
    int BirthGameYear,
    int BirthDayOfYear,
    PreferredFoot PreferredFoot,
    int HeightCm,
    int WeightKg,
    PlayerPosition PrimaryPosition,
    IReadOnlyList<PlayerPosition> SecondaryPositions,
    int Potential,
    int Reputation);
