using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The attributes the tick engine's decision code reads, taken once per player per match (`tick-engine-v1`).
/// </summary>
/// <remarks>
/// Every value is on the canonical 1..20 scale. Milestone 3 needs the defensive and duelling attributes, Milestone 4
/// the off-the-ball ones (Pace, Acceleration, WorkRate) and Milestone 5 the ones the ball carrier decides with
/// (Finishing, Passing, Crossing, Technique, Vision); the goalkeeping milestone adds its own. Kept apart from <see cref="TickPlayerProfile"/>, which
/// holds the athletic limits, because the physics and the decisions are separate jobs.
/// </remarks>
internal readonly record struct TickPlayerSkills
{
    /// <summary>Gets the Tackling attribute: winning the ball cleanly.</summary>
    public required int Tackling { get; init; }

    /// <summary>Gets the Strength attribute: winning the body contact.</summary>
    public required int Strength { get; init; }

    /// <summary>Gets the Aggression attribute: committing to a challenge, and fouling.</summary>
    public required int Aggression { get; init; }

    /// <summary>Gets the Marking attribute: how tightly he stays with his man.</summary>
    public required int Marking { get; init; }

    /// <summary>Gets the Positioning attribute: how well he finds the right place off the ball.</summary>
    public required int Positioning { get; init; }

    /// <summary>Gets the Anticipation attribute: reading where the ball is going.</summary>
    public required int Anticipation { get; init; }

    /// <summary>Gets the Decisions attribute: choosing the right action.</summary>
    public required int Decisions { get; init; }

    /// <summary>Gets the Dribbling attribute: keeping the ball past a defender.</summary>
    public required int Dribbling { get; init; }

    /// <summary>Gets the Agility attribute: changing direction under a challenge.</summary>
    public required int Agility { get; init; }

    /// <summary>Gets the Composure attribute: staying calm under pressure.</summary>
    public required int Composure { get; init; }

    /// <summary>Gets the Pace attribute: how well he is picked to break behind a defence.</summary>
    public required int Pace { get; init; }

    /// <summary>Gets the Acceleration attribute: how quickly he gets away from a standing start.</summary>
    public required int Acceleration { get; init; }

    /// <summary>Gets the WorkRate attribute: how hard he works to get into a supporting position.</summary>
    public required int WorkRate { get; init; }

    /// <summary>Gets the Finishing attribute: putting a shot where the keeper is not.</summary>
    public required int Finishing { get; init; }

    /// <summary>Gets the Passing attribute: playing the ball where he means to.</summary>
    public required int Passing { get; init; }

    /// <summary>Gets the Crossing attribute: delivering the ball from the flank.</summary>
    public required int Crossing { get; init; }

    /// <summary>Gets the Technique attribute: striking the ball cleanly.</summary>
    public required int Technique { get; init; }

    /// <summary>Gets the Vision attribute: how much of the pitch he sees, and whether he sees the through ball.</summary>
    public required int Vision { get; init; }

    /// <summary>Builds a player's skills from his frozen attributes.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static TickPlayerSkills From(PlayerAttributesV1 attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return new TickPlayerSkills
        {
            Tackling = attributes.ValueOf(MatchAttributeName.Tackling),
            Strength = attributes.ValueOf(MatchAttributeName.Strength),
            Aggression = attributes.ValueOf(MatchAttributeName.Aggression),
            Marking = attributes.ValueOf(MatchAttributeName.Marking),
            Positioning = attributes.ValueOf(MatchAttributeName.Positioning),
            Anticipation = attributes.ValueOf(MatchAttributeName.Anticipation),
            Decisions = attributes.ValueOf(MatchAttributeName.Decisions),
            Dribbling = attributes.ValueOf(MatchAttributeName.Dribbling),
            Agility = attributes.ValueOf(MatchAttributeName.Agility),
            Composure = attributes.ValueOf(MatchAttributeName.Composure),
            Pace = attributes.ValueOf(MatchAttributeName.Pace),
            Acceleration = attributes.ValueOf(MatchAttributeName.Acceleration),
            WorkRate = attributes.ValueOf(MatchAttributeName.WorkRate),
            Finishing = attributes.ValueOf(MatchAttributeName.Finishing),
            Passing = attributes.ValueOf(MatchAttributeName.Passing),
            Crossing = attributes.ValueOf(MatchAttributeName.Crossing),
            Technique = attributes.ValueOf(MatchAttributeName.Technique),
            Vision = attributes.ValueOf(MatchAttributeName.Vision),
        };
    }
}
