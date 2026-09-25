using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Ratings;

/// <summary>
/// The nine things the engine rates a side on (master plan §8.4).
/// </summary>
/// <remarks>
/// Eight of them are weighted means of player attributes; <see cref="Cohesion"/> is about how well the
/// eleven fit together rather than how good they are, and is computed from role familiarity instead of
/// from the weight table.
/// </remarks>
public enum MatchUnit
{
    /// <summary>Keeping the ball and moving it forward from deep.</summary>
    BuildUp = 0,

    /// <summary>Making chances.</summary>
    Creation = 1,

    /// <summary>Putting chances away.</summary>
    Finishing = 2,

    /// <summary>Winning the ball back.</summary>
    DefensivePressure = 3,

    /// <summary>Denying space and protecting the goal.</summary>
    DefensiveShape = 4,

    /// <summary>Stopping shots.</summary>
    Goalkeeping = 5,

    /// <summary>Corners and free kicks, at both ends.</summary>
    SetPieces = 6,

    /// <summary>Lasting the ninety and repeating the effort.</summary>
    Fitness = 7,

    /// <summary>How well the eleven fit their jobs.</summary>
    Cohesion = 8,
}

/// <summary>One attribute's contribution to a unit rating.</summary>
/// <param name="Attribute">The attribute.</param>
/// <param name="Weight">How much it counts. Weights are relative, not normalized.</param>
public sealed record AttributeWeight(MatchAttributeName Attribute, int Weight);

/// <summary>How much a player deployed in one band contributes to a unit rating.</summary>
/// <param name="Family">The band.</param>
/// <param name="Weight">How much a player in that band counts. Zero excludes them entirely.</param>
public sealed record FamilyWeight(MatchPositionFamily Family, int Weight);

/// <summary>How one unit rating is composed.</summary>
/// <param name="Attributes">Which attributes matter, and how much.</param>
/// <param name="Families">Which players matter, and how much, by the band they are deployed in.</param>
public sealed record UnitWeighting(
    IReadOnlyList<AttributeWeight> Attributes,
    IReadOnlyList<FamilyWeight> Families)
{
    /// <summary>Gets how much a player in the given band contributes, or zero if they do not.</summary>
    /// <param name="family">The band the player is deployed in.</param>
    public int FamilyWeightOf(MatchPositionFamily family)
    {
        foreach (var entry in Families)
        {
            if (entry.Family == family)
            {
                return entry.Weight;
            }
        }

        return 0;
    }

    /// <summary>Gets the total attribute weight, which the weighted mean divides by.</summary>
    public int TotalAttributeWeight
    {
        get
        {
            var total = 0;

            foreach (var entry in Attributes)
            {
                total += entry.Weight;
            }

            return total;
        }
    }
}

/// <summary>
/// The unit-rating weight tables, versioned with the engine.
/// </summary>
/// <remarks>
/// <para>
/// The one part of the rules too structured to be a scalar on <c>EngineRulesV1</c>. It is still a versioned
/// rule set rather than inline magic numbers (RULE-1): changing a weight is a rules change, and the label
/// below is what a result's provenance refers to.
/// </para>
/// <para>
/// Two design rules run through the table. First, <b>no single attribute dominates a unit</b> — each unit
/// spreads across five or six, so a striker with one outstanding attribute is not automatically the best
/// striker. Second, <b>the weights say who does the job, not who is best at it</b>: a defender contributes
/// to build-up and a striker barely does, because that is who touches the ball in that phase.
/// </para>
/// </remarks>
public static class UnitRatingWeights
{
    /// <summary>The version label of this table.</summary>
    public const string Version = EngineVersions.RatingWeightsLabel;

    /// <summary>
    /// The weightings, keyed by unit, built and checked once when the type is first used.
    /// </summary>
    /// <remarks>
    /// A static constructor rather than a lazy flag. The table is immutable and its check must happen exactly
    /// once before anything reads it, which is what a static constructor guarantees; a mutable "already
    /// validated" field would need a lock to make the same promise, and would leave a window in which two
    /// threads could both be checking.
    /// </remarks>
    public static IReadOnlyDictionary<MatchUnit, UnitWeighting> V1 { get; }

    static UnitRatingWeights()
    {
        var table = Build();

        CheckAgainstItsOwnRules(table);

        V1 = table;
    }

    /// <summary>Gets one unit's weighting.</summary>
    /// <param name="unit">The unit.</param>
    public static UnitWeighting Of(MatchUnit unit) => V1[unit];

    private static Dictionary<MatchUnit, UnitWeighting> Build()
    {
        var table = new Dictionary<MatchUnit, UnitWeighting>
        {
            [MatchUnit.BuildUp] = new(
                [
                    new(MatchAttributeName.Passing, 6),
                    new(MatchAttributeName.Technique, 5),
                    new(MatchAttributeName.FirstTouch, 5),
                    new(MatchAttributeName.Composure, 3),
                    new(MatchAttributeName.Decisions, 3),
                    new(MatchAttributeName.Vision, 3),
                ],
                [
                    new(MatchPositionFamily.Goalkeeper, 1),
                    new(MatchPositionFamily.Defence, 4),
                    new(MatchPositionFamily.Midfield, 6),
                    new(MatchPositionFamily.Attack, 2),
                ]),

            [MatchUnit.Creation] = new(
                [
                    new(MatchAttributeName.Vision, 6),
                    new(MatchAttributeName.Passing, 5),
                    new(MatchAttributeName.Technique, 5),
                    new(MatchAttributeName.Dribbling, 4),
                    new(MatchAttributeName.Crossing, 3),
                    new(MatchAttributeName.Decisions, 3),
                ],
                [
                    new(MatchPositionFamily.Defence, 1),
                    new(MatchPositionFamily.Midfield, 6),
                    new(MatchPositionFamily.Attack, 5),
                ]),

            [MatchUnit.Finishing] = new(
                [
                    new(MatchAttributeName.Finishing, 7),
                    new(MatchAttributeName.Composure, 5),
                    new(MatchAttributeName.Technique, 4),
                    new(MatchAttributeName.Heading, 3),
                    new(MatchAttributeName.Anticipation, 3),
                    new(MatchAttributeName.Pace, 2),
                ],
                [
                    new(MatchPositionFamily.Midfield, 3),
                    new(MatchPositionFamily.Attack, 7),
                ]),

            [MatchUnit.DefensivePressure] = new(
                [
                    new(MatchAttributeName.Tackling, 6),
                    new(MatchAttributeName.WorkRate, 5),
                    new(MatchAttributeName.Aggression, 4),
                    new(MatchAttributeName.Stamina, 4),
                    new(MatchAttributeName.Anticipation, 4),
                    new(MatchAttributeName.Pace, 3),
                ],
                [
                    new(MatchPositionFamily.Defence, 5),
                    new(MatchPositionFamily.Midfield, 5),
                    new(MatchPositionFamily.Attack, 1),
                ]),

            [MatchUnit.DefensiveShape] = new(
                [
                    new(MatchAttributeName.Marking, 6),
                    new(MatchAttributeName.Positioning, 6),
                    new(MatchAttributeName.Anticipation, 4),
                    new(MatchAttributeName.Decisions, 4),
                    new(MatchAttributeName.Tackling, 3),
                    new(MatchAttributeName.Strength, 2),
                ],
                [
                    new(MatchPositionFamily.Goalkeeper, 1),
                    new(MatchPositionFamily.Defence, 6),
                    new(MatchPositionFamily.Midfield, 4),
                ]),

            // The goalkeeper is the only player who stops shots, so this unit reads one player. That is
            // why a weak goalkeeper is the most concentrated risk a side can carry.
            [MatchUnit.Goalkeeping] = new(
                [
                    new(MatchAttributeName.Handling, 6),
                    new(MatchAttributeName.Reflexes, 6),
                    new(MatchAttributeName.OneOnOnes, 4),
                    new(MatchAttributeName.AerialAbility, 4),
                    new(MatchAttributeName.Positioning, 4),
                    new(MatchAttributeName.Composure, 2),
                ],
                [
                    new(MatchPositionFamily.Goalkeeper, 1),
                ]),

            [MatchUnit.SetPieces] = new(
                [
                    new(MatchAttributeName.SetPieces, 7),
                    new(MatchAttributeName.Crossing, 5),
                    new(MatchAttributeName.Heading, 4),
                    new(MatchAttributeName.JumpingReach, 4),
                    new(MatchAttributeName.Technique, 3),
                ],
                [
                    new(MatchPositionFamily.Defence, 3),
                    new(MatchPositionFamily.Midfield, 4),
                    new(MatchPositionFamily.Attack, 4),
                ]),

            [MatchUnit.Fitness] = new(
                [
                    new(MatchAttributeName.Stamina, 7),
                    new(MatchAttributeName.WorkRate, 6),
                    new(MatchAttributeName.Pace, 4),
                    new(MatchAttributeName.Strength, 3),
                    new(MatchAttributeName.Agility, 3),
                ],
                [
                    new(MatchPositionFamily.Goalkeeper, 1),
                    new(MatchPositionFamily.Defence, 4),
                    new(MatchPositionFamily.Midfield, 5),
                    new(MatchPositionFamily.Attack, 4),
                ]),
        };

        return table;
    }

    /// <summary>
    /// Checks the table against its own rules.
    /// </summary>
    /// <remarks>
    /// A weighting with no attributes, a negative weight, or a unit that nobody contributes to would each
    /// divide by zero or silently rate every side identically. Checking here rather than in a test means a
    /// malformed table fails the first match simulated, by name, rather than as a mysteriously even game.
    /// </remarks>
    /// <param name="table">The table to check.</param>
    /// <exception cref="InvalidOperationException">When the table contradicts itself.</exception>
    private static void CheckAgainstItsOwnRules(Dictionary<MatchUnit, UnitWeighting> table)
    {
        foreach (var unit in Enum.GetValues<MatchUnit>())
        {
            if (unit == MatchUnit.Cohesion)
            {
                continue;
            }

            if (!table.TryGetValue(unit, out var weighting))
            {
                throw new InvalidOperationException($"The rating table has no weighting for {unit}.");
            }

            if (weighting.Attributes.Count == 0 || weighting.TotalAttributeWeight <= 0)
            {
                throw new InvalidOperationException($"{unit} has no attribute weights.");
            }

            if (weighting.Attributes.Any(attribute => attribute.Weight <= 0))
            {
                throw new InvalidOperationException($"{unit} has a non-positive attribute weight.");
            }

            if (weighting.Attributes.Select(attribute => attribute.Attribute).Distinct().Count()
                != weighting.Attributes.Count)
            {
                throw new InvalidOperationException($"{unit} weights the same attribute twice.");
            }

            if (!weighting.Families.Any(family => family.Weight > 0))
            {
                throw new InvalidOperationException($"{unit} has no contributing band.");
            }
        }
    }
}
