namespace Tactical11Manager.Domain.Squad;

/// <summary>
/// The experience rule: how a player earns it, where he starts, and the level and skill bonus it gives.
/// </summary>
/// <remarks>
/// <para>
/// Experience is stored as basis points of the maximum, like condition and morale (`TRN-5`…`TRN-7`): 0–10,000,
/// so half a point is 50 and the full 100 points are 10,000. A player earns half a point for every match he
/// plays at least <see cref="MinimumMinutes"/> minutes of, substitutes included, so the maximum is about 200
/// matches.
/// </para>
/// <para>
/// Five even levels, one every 20 points. Level <c>n</c> adds <c>n</c> per cent to the skills the match engine
/// plays with. The bonus is frozen into the match snapshot rather than the experience, so the engine never
/// learns what a level is.
/// </para>
/// <para>
/// Pure: no clock and no random number generator. <see cref="Version"/> is bumped whenever a constant here
/// changes.
/// </para>
/// </remarks>
public static class PlayerExperience
{
    /// <summary>The rule version, bumped whenever a constant changes.</summary>
    public const string Version = "experience-v1";

    /// <summary>The most experience a player can hold: 100 points.</summary>
    public const int MaximumBasisPoints = 10_000;

    /// <summary>What one qualifying match earns: half a point.</summary>
    public const int PerMatchBasisPoints = 50;

    /// <summary>The minutes a player must be on the pitch for a match to count.</summary>
    public const int MinimumMinutes = 15;

    /// <summary>The most experience a player starts with, however old he is: half the maximum.</summary>
    public const int StartingCapBasisPoints = 5_000;

    /// <summary>The age a player starts at nothing; each year after it adds <see cref="PerYearOfAgeBasisPoints"/>.</summary>
    public const int StartingAge = 18;

    /// <summary>What each year of age past <see cref="StartingAge"/> adds to a starting player: four points.</summary>
    public const int PerYearOfAgeBasisPoints = 400;

    /// <summary>The experience that separates one level from the next: 20 points.</summary>
    public const int LevelStepBasisPoints = 2_000;

    /// <summary>The highest level: reached at the maximum.</summary>
    public const int MaximumLevel = MaximumBasisPoints / LevelStepBasisPoints;

    /// <summary>The skill bonus each level gives, in basis points of the skill: 1%.</summary>
    public const int BonusPerLevelBasisPoints = 100;

    /// <summary>Gets what one match earns a player who was on the pitch for the given minutes.</summary>
    /// <param name="minutesPlayed">The minutes the player played.</param>
    public static int EarnedFor(int minutesPlayed) =>
        minutesPlayed >= MinimumMinutes ? PerMatchBasisPoints : 0;

    /// <summary>Gets the experience a newly generated player of the given age starts with.</summary>
    /// <param name="age">The player's age in whole years.</param>
    public static int StartingFor(int age) =>
        Math.Clamp((age - StartingAge) * PerYearOfAgeBasisPoints, 0, StartingCapBasisPoints);

    /// <summary>Gets the level, 0–5. Under one step there is no level.</summary>
    /// <param name="experienceBp">The stored experience in basis points.</param>
    public static int LevelOf(int experienceBp) =>
        Math.Clamp(experienceBp, 0, MaximumBasisPoints) / LevelStepBasisPoints;

    /// <summary>Gets the bonus to the played skills, in basis points of the skill, 0–500.</summary>
    /// <param name="experienceBp">The stored experience in basis points.</param>
    public static int SkillBonusBasisPointsOf(int experienceBp) =>
        LevelOf(experienceBp) * BonusPerLevelBasisPoints;
}
