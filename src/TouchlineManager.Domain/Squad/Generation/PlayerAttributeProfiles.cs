using TouchlineManager.Domain.Rules;

namespace TouchlineManager.Domain.Squad.Generation;

/// <summary>The plausible physique range for a position, used by the generator.</summary>
/// <param name="MinHeightCm">The shortest the position gets.</param>
/// <param name="MaxHeightCm">The tallest the position gets.</param>
/// <param name="MinWeightKg">The lightest the position gets.</param>
/// <param name="MaxWeightKg">The heaviest the position gets.</param>
public sealed record PhysicalRange(int MinHeightCm, int MaxHeightCm, int MinWeightKg, int MaxWeightKg);

/// <summary>
/// The per-position attribute emphasis and the age curves the player generator uses
/// (`TRN-9`, `FIC-7`).
/// </summary>
/// <remarks>
/// <para>
/// Each position names only the attributes it moves away from the squad's ability mean; everything else
/// is the mean plus jitter. That keeps a generated centre back reading as a centre back — strong in the
/// air and in the tackle, weak on the ball — without a twenty-eight-column table per position that
/// nobody could review.
/// </para>
/// <para>
/// This is a versioned artifact like the name pools: a change to a bonus changes every generated squad,
/// so <see cref="Version"/> is folded into the generation run's input hash (`FIC-8`, `PYR-14`).
/// </para>
/// </remarks>
public static class PlayerAttributeProfiles
{
    /// <summary>The profile version, bumped whenever a bonus or a curve changes.</summary>
    public const string Version = "player-attr-v1";

    /// <summary>The most a single attribute moves from the position's base, from its own random draw.</summary>
    public const int AttributeJitter = 2;

    /// <summary>The most a player's whole set moves from the squad mean, from their own random draw.</summary>
    public const int PlayerSpread = 3;

    /// <summary>The age a player's ability adjustment stops penalising and starts declining from.</summary>
    public const int PeakAge = 27;

    /// <summary>The age below which a player still has development headroom.</summary>
    public const int DevelopmentAge = 25;

    private static readonly Dictionary<PlayerPosition, IReadOnlyDictionary<AttributeName, int>> Table =
        new Dictionary<PlayerPosition, IReadOnlyDictionary<AttributeName, int>>
        {
            [PlayerPosition.Goalkeeper] = Bonuses(
                (AttributeName.Handling, 8),
                (AttributeName.Reflexes, 8),
                (AttributeName.OneOnOnes, 7),
                (AttributeName.AerialAbility, 6),
                (AttributeName.Positioning, 4),
                (AttributeName.Composure, 3),
                (AttributeName.Decisions, 3),
                (AttributeName.Anticipation, 3),
                (AttributeName.JumpingReach, 2),
                (AttributeName.Strength, 2),
                (AttributeName.Leadership, 1),
                (AttributeName.WorkRate, 1),
                (AttributeName.Passing, -3),
                (AttributeName.FirstTouch, -4),
                (AttributeName.Finishing, -9),
                (AttributeName.Dribbling, -8),
                (AttributeName.Crossing, -6),
                (AttributeName.SetPieces, -6),
                (AttributeName.Technique, -5),
                (AttributeName.Heading, -4),
                (AttributeName.Marking, -4),
                (AttributeName.Tackling, -4),
                (AttributeName.Pace, -3),
                (AttributeName.Acceleration, -3),
                (AttributeName.Agility, -2),
                (AttributeName.Vision, -2),
                (AttributeName.Stamina, -1),
                (AttributeName.Aggression, -1)),
            [PlayerPosition.RightBack] = FullBackBonuses(),
            [PlayerPosition.LeftBack] = FullBackBonuses(),
            [PlayerPosition.CentreBack] = Bonuses(
                (AttributeName.Marking, 5),
                (AttributeName.Tackling, 5),
                (AttributeName.Heading, 5),
                (AttributeName.Strength, 5),
                (AttributeName.JumpingReach, 5),
                (AttributeName.Positioning, 4),
                (AttributeName.Aggression, 4),
                (AttributeName.Anticipation, 3),
                (AttributeName.Decisions, 3),
                (AttributeName.Leadership, 3),
                (AttributeName.Composure, 2),
                (AttributeName.WorkRate, 2),
                (AttributeName.Stamina, 1),
                (AttributeName.Finishing, -6),
                (AttributeName.Dribbling, -5),
                (AttributeName.Crossing, -4),
                (AttributeName.Handling, -9),
                (AttributeName.Reflexes, -9),
                (AttributeName.OneOnOnes, -9),
                (AttributeName.AerialAbility, -4),
                (AttributeName.Pace, -2),
                (AttributeName.Acceleration, -2),
                (AttributeName.Agility, -3),
                (AttributeName.Vision, -2),
                (AttributeName.Technique, -2),
                (AttributeName.FirstTouch, -2),
                (AttributeName.SetPieces, -2),
                (AttributeName.Passing, -1)),
            [PlayerPosition.DefensiveMidfielder] = Bonuses(
                (AttributeName.Tackling, 4),
                (AttributeName.Marking, 3),
                (AttributeName.Positioning, 4),
                (AttributeName.Anticipation, 4),
                (AttributeName.WorkRate, 4),
                (AttributeName.Stamina, 4),
                (AttributeName.Decisions, 3),
                (AttributeName.Passing, 3),
                (AttributeName.Aggression, 3),
                (AttributeName.Composure, 2),
                (AttributeName.Strength, 2),
                (AttributeName.Leadership, 2),
                (AttributeName.FirstTouch, 1),
                (AttributeName.Finishing, -4),
                (AttributeName.Heading, -2),
                (AttributeName.Handling, -9),
                (AttributeName.Reflexes, -9),
                (AttributeName.OneOnOnes, -9),
                (AttributeName.AerialAbility, -5),
                (AttributeName.Pace, -1),
                (AttributeName.Acceleration, -1),
                (AttributeName.Agility, -1),
                (AttributeName.Crossing, -1),
                (AttributeName.SetPieces, -2),
                (AttributeName.Dribbling, -1),
                (AttributeName.JumpingReach, -1)),
            [PlayerPosition.CentralMidfielder] = Bonuses(
                (AttributeName.Passing, 5),
                (AttributeName.FirstTouch, 4),
                (AttributeName.Technique, 4),
                (AttributeName.Vision, 4),
                (AttributeName.Decisions, 4),
                (AttributeName.Stamina, 4),
                (AttributeName.WorkRate, 3),
                (AttributeName.Composure, 3),
                (AttributeName.Anticipation, 3),
                (AttributeName.Positioning, 2),
                (AttributeName.Agility, 2),
                (AttributeName.Tackling, 1),
                (AttributeName.Dribbling, 1),
                (AttributeName.Finishing, -2),
                (AttributeName.Heading, -3),
                (AttributeName.Handling, -9),
                (AttributeName.Reflexes, -9),
                (AttributeName.OneOnOnes, -9),
                (AttributeName.AerialAbility, -6),
                (AttributeName.Strength, -1),
                (AttributeName.Aggression, -1),
                (AttributeName.Marking, -1)),
            [PlayerPosition.AttackingMidfielder] = Bonuses(
                (AttributeName.Technique, 5),
                (AttributeName.Dribbling, 5),
                (AttributeName.Vision, 5),
                (AttributeName.FirstTouch, 5),
                (AttributeName.Passing, 4),
                (AttributeName.Composure, 4),
                (AttributeName.Decisions, 4),
                (AttributeName.Agility, 4),
                (AttributeName.Anticipation, 3),
                (AttributeName.Acceleration, 3),
                (AttributeName.Finishing, 2),
                (AttributeName.SetPieces, 2),
                (AttributeName.Tackling, -4),
                (AttributeName.Marking, -4),
                (AttributeName.Heading, -4),
                (AttributeName.Strength, -3),
                (AttributeName.Handling, -9),
                (AttributeName.Reflexes, -9),
                (AttributeName.OneOnOnes, -9),
                (AttributeName.AerialAbility, -6),
                (AttributeName.Positioning, -2),
                (AttributeName.WorkRate, -1),
                (AttributeName.JumpingReach, -3)),
            [PlayerPosition.RightWinger] = WingerBonuses(),
            [PlayerPosition.LeftWinger] = WingerBonuses(),
            [PlayerPosition.Striker] = Bonuses(
                (AttributeName.Finishing, 7),
                (AttributeName.Composure, 5),
                (AttributeName.Positioning, 5),
                (AttributeName.FirstTouch, 4),
                (AttributeName.Anticipation, 4),
                (AttributeName.Acceleration, 4),
                (AttributeName.Pace, 4),
                (AttributeName.Heading, 4),
                (AttributeName.Strength, 3),
                (AttributeName.Dribbling, 3),
                (AttributeName.Agility, 2),
                (AttributeName.JumpingReach, 2),
                (AttributeName.Tackling, -6),
                (AttributeName.Marking, -6),
                (AttributeName.Handling, -9),
                (AttributeName.Reflexes, -9),
                (AttributeName.OneOnOnes, -9),
                (AttributeName.AerialAbility, -7),
                (AttributeName.Crossing, -2),
                (AttributeName.Passing, -2),
                (AttributeName.Vision, -2),
                (AttributeName.WorkRate, -1),
                (AttributeName.SetPieces, -1),
                (AttributeName.Stamina, -1)),
        };

    private static readonly Dictionary<PlayerPosition, PhysicalRange> Physiques =
        new Dictionary<PlayerPosition, PhysicalRange>
        {
            [PlayerPosition.Goalkeeper] = new(185, 196, 78, 92),
            [PlayerPosition.RightBack] = new(170, 183, 66, 78),
            [PlayerPosition.LeftBack] = new(170, 183, 66, 78),
            [PlayerPosition.CentreBack] = new(182, 194, 76, 90),
            [PlayerPosition.DefensiveMidfielder] = new(175, 188, 70, 84),
            [PlayerPosition.CentralMidfielder] = new(170, 184, 66, 80),
            [PlayerPosition.AttackingMidfielder] = new(168, 181, 64, 77),
            [PlayerPosition.RightWinger] = new(166, 180, 62, 76),
            [PlayerPosition.LeftWinger] = new(166, 180, 62, 76),
            [PlayerPosition.Striker] = new(176, 191, 72, 88),
        };

    /// <summary>Gets how far a position moves an attribute away from the squad's ability mean.</summary>
    /// <param name="position">The position.</param>
    /// <param name="name">The attribute.</param>
    /// <returns>The bonus, or zero when the position does not move that attribute.</returns>
    public static int BonusFor(PlayerPosition position, AttributeName name) =>
        Table.TryGetValue(position, out var bonuses) && bonuses.TryGetValue(name, out var bonus)
            ? bonus
            : 0;

    /// <summary>Gets the plausible physique range for a position.</summary>
    /// <param name="position">The position.</param>
    public static PhysicalRange PhysicalRangeFor(PlayerPosition position) =>
        Physiques.TryGetValue(position, out var range)
            ? range
            : throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position.");

    /// <summary>
    /// Gets how far a player's ability sits from their profile's mean at the given age.
    /// </summary>
    /// <remarks>
    /// Players inside <see cref="PeakAge"/>'s window are unadjusted; younger players are discounted
    /// because they have not developed yet, and older ones because they are past their best. This is what
    /// makes a squad's age spread visible in its ability rather than only on the player list.
    /// </remarks>
    /// <param name="age">The player's age in game years.</param>
    public static int AgeAbilityAdjustment(int age) => age switch
    {
        < 24 => -(24 - age),
        > 29 => -(age - 29),
        _ => 0,
    };

    /// <summary>Gets how much development headroom a player of the given age still has.</summary>
    /// <param name="age">The player's age in game years.</param>
    public static int PotentialUpsideForAge(int age) =>
        age < DevelopmentAge ? DevelopmentAge - age : 0;

    /// <summary>Gets whether a player is old enough to be treated as a senior squad member.</summary>
    /// <param name="age">The player's age in game years.</param>
    public static bool IsSeniorAge(int age) =>
        age is >= WorldRuleSet.PlayerMinimumAge and <= WorldRuleSet.PlayerMaximumAge;

    private static Dictionary<AttributeName, int> FullBackBonuses() => Bonuses(
        (AttributeName.Pace, 4),
        (AttributeName.Acceleration, 4),
        (AttributeName.Stamina, 4),
        (AttributeName.Crossing, 4),
        (AttributeName.WorkRate, 4),
        (AttributeName.Tackling, 3),
        (AttributeName.Agility, 3),
        (AttributeName.Marking, 2),
        (AttributeName.Anticipation, 2),
        (AttributeName.Positioning, 2),
        (AttributeName.Technique, 1),
        (AttributeName.Passing, 1),
        (AttributeName.Decisions, 1),
        (AttributeName.FirstTouch, 1),
        (AttributeName.Finishing, -5),
        (AttributeName.Heading, -2),
        (AttributeName.SetPieces, -3),
        (AttributeName.Strength, -2),
        (AttributeName.Handling, -9),
        (AttributeName.Reflexes, -9),
        (AttributeName.OneOnOnes, -9),
        (AttributeName.AerialAbility, -6),
        (AttributeName.Dribbling, -1),
        (AttributeName.Vision, -1),
        (AttributeName.Composure, -1),
        (AttributeName.JumpingReach, -2));

    private static Dictionary<AttributeName, int> WingerBonuses() => Bonuses(
        (AttributeName.Pace, 5),
        (AttributeName.Acceleration, 5),
        (AttributeName.Dribbling, 5),
        (AttributeName.Agility, 5),
        (AttributeName.Crossing, 4),
        (AttributeName.Technique, 3),
        (AttributeName.FirstTouch, 3),
        (AttributeName.Stamina, 3),
        (AttributeName.Vision, 2),
        (AttributeName.Composure, 2),
        (AttributeName.Finishing, 2),
        (AttributeName.Decisions, 1),
        (AttributeName.Tackling, -5),
        (AttributeName.Marking, -5),
        (AttributeName.Heading, -3),
        (AttributeName.Strength, -3),
        (AttributeName.Handling, -9),
        (AttributeName.Reflexes, -9),
        (AttributeName.OneOnOnes, -9),
        (AttributeName.AerialAbility, -6),
        (AttributeName.Positioning, -3),
        (AttributeName.Aggression, -2),
        (AttributeName.JumpingReach, -3),
        (AttributeName.SetPieces, -1),
        (AttributeName.WorkRate, -1));

    private static Dictionary<AttributeName, int> Bonuses(
        params (AttributeName Name, int Bonus)[] entries) =>
        entries.ToDictionary(entry => entry.Name, entry => entry.Bonus);
}
