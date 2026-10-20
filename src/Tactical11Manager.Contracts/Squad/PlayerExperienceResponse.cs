namespace Tactical11Manager.Contracts.Squad;

/// <summary>
/// A player's experience as a screen shows it: the points, the level, and what the level adds in matches.
/// </summary>
/// <remarks>
/// Experience is public, like age, so it rides on a player of any club. The rule lives on the server; a client
/// shows these numbers and never repeats the thresholds. The bonus is a percentage of the skills the match
/// engine plays with and never changes the attributes displayed on the 1–20 scale.
/// </remarks>
/// <param name="Points">The experience, 0–100, to one decimal place.</param>
/// <param name="Level">The level, 0–<paramref name="MaxLevel"/>; 0 until the first step is reached.</param>
/// <param name="MaxLevel">The highest level a player can reach.</param>
/// <param name="SkillBonusPercent">The per cent the level adds to the skills played with in a match.</param>
/// <param name="NextLevelPoints">The points at which the next level is reached, or null at the highest level.</param>
public sealed record PlayerExperienceResponse(
    decimal Points,
    int Level,
    int MaxLevel,
    int SkillBonusPercent,
    decimal? NextLevelPoints);
