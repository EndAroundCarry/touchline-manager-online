using TouchlineManager.MatchEngine.Model;

namespace TouchlineManager.MatchEngine.Tick;

/// <summary>
/// The attributes the tick engine's decision code reads, taken once per player per match (`tick-engine-v1`).
/// </summary>
/// <remarks>
/// Every value is on the canonical 1..20 scale. Milestone 3 needs the defensive and duelling attributes, Milestone 4
/// the off-the-ball ones (Pace, Acceleration, WorkRate) and Milestone 5 the ones the ball carrier decides with
/// (Finishing, Passing, Crossing, Technique, Vision) and Milestone 6 the goalkeeper's (Reflexes, Handling, OneOnOnes,
/// AerialAbility, JumpingReach) and Milestone 7 the set-piece ones (Heading, SetPieces). Kept apart from <see cref="TickPlayerProfile"/>, which
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

    /// <summary>Gets the Reflexes attribute: how soon a goalkeeper reacts to a shot.</summary>
    public required int Reflexes { get; init; }

    /// <summary>Gets the Handling attribute: whether a goalkeeper holds the ball or spills it.</summary>
    public required int Handling { get; init; }

    /// <summary>Gets the OneOnOnes attribute: how well a goalkeeper comes out to a lone attacker.</summary>
    public required int OneOnOnes { get; init; }

    /// <summary>Gets the AerialAbility attribute: a goalkeeper's command of the high ball.</summary>
    public required int AerialAbility { get; init; }

    /// <summary>Gets the JumpingReach attribute: how high a goalkeeper stretches.</summary>
    public required int JumpingReach { get; init; }

    /// <summary>Gets the Heading attribute: winning a high ball with the head, which ranks a corner's targets and markers.</summary>
    public required int Heading { get; init; }

    /// <summary>Gets the SetPieces attribute: striking a dead ball, which picks the corner, free-kick and penalty takers.</summary>
    public required int SetPieces { get; init; }

    /// <summary>The pitch's average attribute, which the engine's skill curve is centred on.</summary>
    public const int CurveCentre = 13;

    /// <summary>
    /// How much of an attribute's distance from <see cref="CurveCentre"/> counts, in percent. The curve flattens the ends so that a side of stars
    /// is better than a side of journeymen without being a different sport: the possession engine's team ratings did the same by averaging, and
    /// the tick engine, which plays every player, has to do it player by player (Milestone 9 calibration).
    /// </summary>
    public const int CurvePercent = 38;

    /// <summary>Builds the skills a player brings to a match: his attributes pulled towards the average by the skill curve.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static TickPlayerSkills Rated(PlayerAttributesV1 attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = new int[MatchAttributeNames.Count];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = CurveCentre + (((attributes.Values[index]) - CurveCentre) * CurvePercent / 100);
        }

        return From(PlayerAttributesV1.From(values));
    }

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
            Reflexes = attributes.ValueOf(MatchAttributeName.Reflexes),
            Handling = attributes.ValueOf(MatchAttributeName.Handling),
            OneOnOnes = attributes.ValueOf(MatchAttributeName.OneOnOnes),
            AerialAbility = attributes.ValueOf(MatchAttributeName.AerialAbility),
            JumpingReach = attributes.ValueOf(MatchAttributeName.JumpingReach),
            Heading = attributes.ValueOf(MatchAttributeName.Heading),
            SetPieces = attributes.ValueOf(MatchAttributeName.SetPieces),
        };
    }
}
