namespace TouchlineManager.Domain.Squad;

/// <summary>
/// A position-specific training programme: the weighted set of attributes one player trains (`TRN-1`,
/// `TRN-2`).
/// </summary>
/// <remarks>
/// Programmes are chosen per player. The default is the one matching the player's primary position
/// (<see cref="TrainingProgrammes.DefaultFor"/>); the manager may override any single player. The
/// <c>Mental</c>, <c>Physical</c> and <c>Recovery</c> programmes are never a default, only a manual choice.
/// </remarks>
public enum TrainingProgramme
{
    /// <summary>Handling, reflexes, and the rest of a goalkeeper's craft.</summary>
    Goalkeeper = 0,

    /// <summary>Tackling, marking, heading, and positioning.</summary>
    Defender = 1,

    /// <summary>Crossing, pace, and stamina for a full back or wing back.</summary>
    WingBack = 2,

    /// <summary>Passing, vision, and decision making for a central midfielder.</summary>
    Midfielder = 3,

    /// <summary>Pace, dribbling, and crossing for a wide attacker.</summary>
    Winger = 4,

    /// <summary>Finishing, composure, and movement for a striker.</summary>
    Forward = 5,

    /// <summary>The mental family: decisions, composure, and awareness.</summary>
    Mental = 6,

    /// <summary>The athletic core: pace, acceleration, stamina, and strength.</summary>
    Physical = 7,

    /// <summary>No development, in exchange for faster recovery from fatigue.</summary>
    Recovery = 8,
}

/// <summary>One attribute a programme trains, and how much it matters to it.</summary>
/// <param name="Attribute">The trained attribute.</param>
/// <param name="Weight">3 for a core attribute, 2 for an important one, 1 for a supporting one.</param>
public sealed record TrainingAttributeWeight(AttributeName Attribute, int Weight);

/// <summary>One programme's catalogue entry.</summary>
/// <param name="Programme">The programme.</param>
/// <param name="Code">The stable code stored and sent over the API.</param>
/// <param name="Label">The display name.</param>
/// <param name="Description">One sentence saying who the programme is for.</param>
/// <param name="Attributes">The attributes the programme trains, heaviest first.</param>
public sealed record TrainingProgrammeDefinition(
    TrainingProgramme Programme,
    string Code,
    string Label,
    string Description,
    IReadOnlyList<TrainingAttributeWeight> Attributes)
{
    private readonly int[] _weights = BuildWeights(Attributes);

    /// <summary>Gets the weight of one attribute in this programme, or 0 when it is not trained.</summary>
    /// <param name="attribute">The attribute.</param>
    public int WeightOf(AttributeName attribute) => _weights[(int)attribute];

    private static int[] BuildWeights(IReadOnlyList<TrainingAttributeWeight> attributes)
    {
        var weights = new int[AttributeNames.Count];

        foreach (var entry in attributes)
        {
            weights[(int)entry.Attribute] = entry.Weight;
        }

        return weights;
    }
}

/// <summary>
/// The training programme catalogue: the single source of truth for which attributes each programme trains
/// and how strongly (`TRN-1`, `TRN-2`).
/// </summary>
/// <remarks>
/// The catalogue is served to the web client in the training response, so no consumer duplicates the
/// weights. The weights are tunable constants; the tests pin their shape (every attribute valid, every
/// weight between 1 and 3, every position mapped), not their values.
/// </remarks>
public static class TrainingProgrammes
{
    /// <summary>The weight of a core attribute.</summary>
    public const int CoreWeight = 3;

    /// <summary>The weight of an important attribute.</summary>
    public const int ImportantWeight = 2;

    /// <summary>The weight of a supporting attribute.</summary>
    public const int SupportingWeight = 1;

    /// <summary>The longest programme code, so a column can be sized to hold every value.</summary>
    public const int MaxCodeLength = 10;

    /// <summary>Every programme definition, in declaration order.</summary>
    public static readonly IReadOnlyList<TrainingProgrammeDefinition> All =
    [
        Define(
            TrainingProgramme.Goalkeeper,
            "goalkeeper",
            "Goalkeeper",
            "Shot stopping, handling, and commanding the box.",
            (AttributeName.Handling, 3),
            (AttributeName.Reflexes, 3),
            (AttributeName.OneOnOnes, 3),
            (AttributeName.AerialAbility, 2),
            (AttributeName.Positioning, 2),
            (AttributeName.Decisions, 1),
            (AttributeName.Composure, 1),
            (AttributeName.Anticipation, 1),
            (AttributeName.Agility, 1),
            (AttributeName.JumpingReach, 1)),
        Define(
            TrainingProgramme.Defender,
            "defender",
            "Defender",
            "Winning the ball, holding a line, and dealing with crosses.",
            (AttributeName.Tackling, 3),
            (AttributeName.Marking, 3),
            (AttributeName.Heading, 3),
            (AttributeName.Positioning, 3),
            (AttributeName.Strength, 2),
            (AttributeName.JumpingReach, 2),
            (AttributeName.Anticipation, 2),
            (AttributeName.Decisions, 1),
            (AttributeName.Composure, 1),
            (AttributeName.Aggression, 1)),
        Define(
            TrainingProgramme.WingBack,
            "wingback",
            "Wing back",
            "Getting up and down the flank: crossing, pace, and defending the wide channel.",
            (AttributeName.Crossing, 3),
            (AttributeName.Pace, 3),
            (AttributeName.Stamina, 3),
            (AttributeName.Tackling, 2),
            (AttributeName.Acceleration, 2),
            (AttributeName.WorkRate, 2),
            (AttributeName.Marking, 2),
            (AttributeName.Dribbling, 1),
            (AttributeName.Positioning, 1),
            (AttributeName.Passing, 1)),
        Define(
            TrainingProgramme.Midfielder,
            "midfielder",
            "Central midfielder",
            "Controlling the tempo: passing, vision, and the engine to keep doing it.",
            (AttributeName.Passing, 3),
            (AttributeName.Vision, 3),
            (AttributeName.Decisions, 2),
            (AttributeName.FirstTouch, 2),
            (AttributeName.Technique, 2),
            (AttributeName.Stamina, 2),
            (AttributeName.WorkRate, 2),
            (AttributeName.Composure, 2),
            (AttributeName.Tackling, 1),
            (AttributeName.Positioning, 1)),
        Define(
            TrainingProgramme.Winger,
            "winger",
            "Winger",
            "Beating a full back: pace, dribbling, and delivery.",
            (AttributeName.Pace, 3),
            (AttributeName.Acceleration, 3),
            (AttributeName.Dribbling, 3),
            (AttributeName.Crossing, 3),
            (AttributeName.Agility, 2),
            (AttributeName.Technique, 2),
            (AttributeName.FirstTouch, 1),
            (AttributeName.Stamina, 1),
            (AttributeName.Vision, 1)),
        Define(
            TrainingProgramme.Forward,
            "forward",
            "Forward",
            "Putting the ball in the net: finishing, composure, and movement in the box.",
            (AttributeName.Finishing, 3),
            (AttributeName.Composure, 3),
            (AttributeName.Positioning, 2),
            (AttributeName.Heading, 2),
            (AttributeName.FirstTouch, 2),
            (AttributeName.Acceleration, 2),
            (AttributeName.Anticipation, 2),
            (AttributeName.Dribbling, 1),
            (AttributeName.Strength, 1),
            (AttributeName.Pace, 1)),
        Define(
            TrainingProgramme.Mental,
            "mental",
            "Mental",
            "Reading the game: decisions, composure, and awareness for any position.",
            (AttributeName.Decisions, 3),
            (AttributeName.Composure, 3),
            (AttributeName.Vision, 2),
            (AttributeName.Positioning, 2),
            (AttributeName.Anticipation, 2),
            (AttributeName.WorkRate, 2),
            (AttributeName.Aggression, 1),
            (AttributeName.Leadership, 1)),
        Define(
            TrainingProgramme.Physical,
            "physical",
            "Physical",
            "The athletic core for any position: speed, stamina, and strength.",
            (AttributeName.Pace, 3),
            (AttributeName.Acceleration, 3),
            (AttributeName.Stamina, 3),
            (AttributeName.Strength, 3),
            (AttributeName.Agility, 2),
            (AttributeName.JumpingReach, 2)),
        Define(
            TrainingProgramme.Recovery,
            "recovery",
            "Recovery",
            "No development this period; clears fatigue faster.",
            []),
    ];

    /// <summary>Gets one programme's definition.</summary>
    /// <param name="programme">The programme.</param>
    public static TrainingProgrammeDefinition Of(TrainingProgramme programme) =>
        All.FirstOrDefault(definition => definition.Programme == programme)
        ?? throw new ArgumentOutOfRangeException(nameof(programme), programme, "Unknown training programme.");

    /// <summary>Gets the programme a player trains when the manager has not chosen one.</summary>
    /// <param name="position">The player's primary position.</param>
    public static TrainingProgramme DefaultFor(PlayerPosition position) => position switch
    {
        PlayerPosition.Goalkeeper => TrainingProgramme.Goalkeeper,
        PlayerPosition.CentreBack => TrainingProgramme.Defender,
        PlayerPosition.RightBack or PlayerPosition.LeftBack => TrainingProgramme.WingBack,
        PlayerPosition.DefensiveMidfielder
            or PlayerPosition.CentralMidfielder
            or PlayerPosition.AttackingMidfielder => TrainingProgramme.Midfielder,
        PlayerPosition.RightWinger or PlayerPosition.LeftWinger => TrainingProgramme.Winger,
        PlayerPosition.Striker => TrainingProgramme.Forward,
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown position."),
    };

    /// <summary>Converts a programme to its stable code.</summary>
    /// <param name="programme">The programme.</param>
    public static string ToCode(this TrainingProgramme programme) => Of(programme).Code;

    /// <summary>Parses a stable code back to its programme.</summary>
    /// <param name="code">The stable code.</param>
    public static TrainingProgramme FromCode(string code) =>
        TryFromCode(code, out var programme)
            ? programme
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown training programme code.");

    /// <summary>Tries to parse a stable code back to its programme.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="programme">The programme, when the code is known.</param>
    public static bool TryFromCode(string? code, out TrainingProgramme programme)
    {
        foreach (var definition in All)
        {
            if (string.Equals(definition.Code, code, StringComparison.Ordinal))
            {
                programme = definition.Programme;

                return true;
            }
        }

        programme = default;

        return false;
    }

    private static TrainingProgrammeDefinition Define(
        TrainingProgramme programme,
        string code,
        string label,
        string description,
        params (AttributeName Attribute, int Weight)[] attributes) =>
        new(
            programme,
            code,
            label,
            description,
            [.. attributes.Select(entry => new TrainingAttributeWeight(entry.Attribute, entry.Weight))]);
}
