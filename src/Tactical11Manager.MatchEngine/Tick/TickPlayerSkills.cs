using Tactical11Manager.MatchEngine.Model;

namespace Tactical11Manager.MatchEngine.Tick;

/// <summary>
/// The attributes the tick engine's decision code reads, taken once per player per match (`tick-engine-v1`).
/// </summary>
/// <remarks>
/// Every value is in hundredths of an attribute point on the canonical 1..20 scale (<see cref="Scale"/>), so that a drop of a few percent
/// is not lost to whole-point arithmetic. A fresh player's values are whole points times <see cref="Scale"/>, and every reader divides the
/// scale out where it needs a point count. Milestone 3 needs the defensive and duelling attributes, Milestone 4
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

    /// <summary>Gets the attributes these skills were curved from, or null when they were not curved (<see cref="Uncurved"/>).</summary>
    public PlayerAttributesV1? Natural { get; init; }

    /// <summary>Gets how far below <see cref="FreshCondition"/> the condition these skills were worked out at lies, in basis points (a struct cannot default a field to full).</summary>
    public int ConditionShortfall { get; init; }

    /// <summary>Gets the live condition these skills were worked out at, in basis points: 10,000 for a fresh player (<see cref="Tired"/>).</summary>
    public int Condition => FreshCondition - ConditionShortfall;

    /// <summary>
    /// Gets the experience bonus these skills were worked out with, in basis points of the skill (0..500), or 0 for none.
    /// </summary>
    /// <remarks>
    /// It multiplies the played value after the curve and the tiredness loss, for every skill except Pace and Acceleration, and it is never
    /// capped at the top of the scale: a sheet 20 at 500 plays as 21 where the engine reads the raw value (<see cref="Uncurved"/>) and as
    /// 15.75 where it reads the curved one. The movement physics (<see cref="TickPlayerProfile"/>) and the raw-attribute readers
    /// (<see cref="TickAttackEdge"/>, <see cref="Ratings.EffectiveSkill"/>) do not see it.
    /// </remarks>
    public int ExperienceBonusBasisPoints { get; init; }

    /// <summary>How many hundredths make one attribute point: the unit every value here is held in, as in <see cref="Ratings.EffectiveSkill"/>.</summary>
    public const int Scale = 100;

    /// <summary>A fresh player's condition, in basis points.</summary>
    public const int FreshCondition = 10_000;

    /// <summary>The pitch's average attribute, which the engine's skill curve is centred on.</summary>
    public const int CurveCentre = 13;

    /// <summary>
    /// How much of an attribute's distance from <see cref="CurveCentre"/> counts, in percent. The curve flattens the ends so that a side of stars
    /// is better than a side of journeymen without being a different sport: the possession engine's team ratings did the same by averaging, and
    /// the tick engine, which plays every player, has to do it player by player (Milestone 9 calibration).
    /// </summary>
    public const int CurvePercent = 38;

    /// <summary>Builds the skills a player brings to a match, fresh: his attributes pulled towards the average by the skill curve.</summary>
    /// <param name="attributes">The player's attributes.</param>
    /// <param name="experienceBonusBasisPoints">His experience bonus, 0..500 basis points of each skill.</param>
    public static TickPlayerSkills Rated(PlayerAttributesV1 attributes, int experienceBonusBasisPoints = 0) =>
        Tired(attributes, FreshCondition, experienceBonusBasisPoints);

    /// <summary>
    /// Builds a player's skills as the skill curve and his condition leave them: the share of each sheet value that tiredness takes
    /// (<see cref="TickSkillTiredness"/>) comes off, and the curve scales that loss like every other difference from the average.
    /// </summary>
    /// <remarks>
    /// Allocation-free, so the match can rebuild a seat's skills every minute. A fresh player's values are exactly the curved whole points.
    /// </remarks>
    /// <param name="attributes">The player's attributes.</param>
    /// <param name="conditionBasisPoints">His live condition, 0..10,000.</param>
    /// <param name="experienceBonusBasisPoints">His experience bonus, 0..500 basis points of each skill (<see cref="ExperienceBonusBasisPoints"/>).</param>
    public static TickPlayerSkills Tired(PlayerAttributesV1 attributes, int conditionBasisPoints, int experienceBonusBasisPoints = 0)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return Build(attributes, conditionBasisPoints, curved: true, experienceBonusBasisPoints);
    }

    /// <summary>
    /// Gets the player's attributes as he has them, before the skill curve, and as tired as he is: what a dead ball, where one man faces
    /// another with time to spare, is decided on. Skills not built by <see cref="Rated"/> have no curve to undo and are returned as they are.
    /// </summary>
    public TickPlayerSkills Uncurved() => Natural is null ? this : Build(Natural, Condition, curved: false, ExperienceBonusBasisPoints);

    /// <summary>Builds a player's skills from his frozen attributes, in hundredths of a point.</summary>
    /// <param name="attributes">The player's attributes.</param>
    public static TickPlayerSkills From(PlayerAttributesV1 attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        return Build(attributes, FreshCondition, curved: false) with { Natural = null };
    }

    private static TickPlayerSkills Build(PlayerAttributesV1 attributes, int conditionBasisPoints, bool curved, int bonus = 0)
    {
        var condition = Math.Clamp(conditionBasisPoints, 0, FreshCondition);
        var full = TickSkillTiredness.FullDropBasisPoints(condition);
        var mental = TickSkillTiredness.MentalDropBasisPoints(full);

        return new TickPlayerSkills
        {
            Tackling = Value(attributes, MatchAttributeName.Tackling, full, curved, bonus),
            Strength = Value(attributes, MatchAttributeName.Strength, full, curved, bonus),
            Aggression = Value(attributes, MatchAttributeName.Aggression, mental, curved, bonus),
            Marking = Value(attributes, MatchAttributeName.Marking, full, curved, bonus),
            Positioning = Value(attributes, MatchAttributeName.Positioning, mental, curved, bonus),
            Anticipation = Value(attributes, MatchAttributeName.Anticipation, mental, curved, bonus),
            Decisions = Value(attributes, MatchAttributeName.Decisions, mental, curved, bonus),
            Dribbling = Value(attributes, MatchAttributeName.Dribbling, full, curved, bonus),
            Agility = Value(attributes, MatchAttributeName.Agility, full, curved, bonus),
            Composure = Value(attributes, MatchAttributeName.Composure, mental, curved, bonus),
            Pace = Value(attributes, MatchAttributeName.Pace, 0, curved, 0),
            Acceleration = Value(attributes, MatchAttributeName.Acceleration, 0, curved, 0),
            WorkRate = Value(attributes, MatchAttributeName.WorkRate, mental, curved, bonus),
            Finishing = Value(attributes, MatchAttributeName.Finishing, full, curved, bonus),
            Passing = Value(attributes, MatchAttributeName.Passing, full, curved, bonus),
            Crossing = Value(attributes, MatchAttributeName.Crossing, full, curved, bonus),
            Technique = Value(attributes, MatchAttributeName.Technique, full, curved, bonus),
            Vision = Value(attributes, MatchAttributeName.Vision, mental, curved, bonus),
            Reflexes = Value(attributes, MatchAttributeName.Reflexes, full, curved, bonus),
            Handling = Value(attributes, MatchAttributeName.Handling, full, curved, bonus),
            OneOnOnes = Value(attributes, MatchAttributeName.OneOnOnes, full, curved, bonus),
            AerialAbility = Value(attributes, MatchAttributeName.AerialAbility, full, curved, bonus),
            JumpingReach = Value(attributes, MatchAttributeName.JumpingReach, full, curved, bonus),
            Heading = Value(attributes, MatchAttributeName.Heading, full, curved, bonus),
            SetPieces = Value(attributes, MatchAttributeName.SetPieces, full, curved, bonus),
            Natural = attributes,
            ConditionShortfall = FreshCondition - condition,
            ExperienceBonusBasisPoints = bonus,
        };
    }

    /// <summary>One attribute in hundredths: the sheet value less its drop, curved or not, plus the experience bonus (none for Pace and Acceleration).</summary>
    private static int Value(PlayerAttributesV1 attributes, MatchAttributeName name, int dropBasisPoints, bool curved, int bonusBasisPoints)
    {
        var sheet = attributes.ValueOf(name);
        int played;

        if (!curved)
        {
            played = ((sheet * (TickSkillTiredness.BasisPoints - dropBasisPoints)) + (TickSkillTiredness.BasisPoints / 200)) / (TickSkillTiredness.BasisPoints / Scale);
        }
        else
        {
            var fresh = (CurveCentre + ((sheet - CurveCentre) * CurvePercent / 100)) * Scale;
            var lost = ((sheet * dropBasisPoints * CurvePercent) + (TickSkillTiredness.BasisPoints / 2)) / TickSkillTiredness.BasisPoints;

            played = fresh - lost;
        }

        // Deliberately not clamped to the scale: the bonus is a plain percentage of what the engine plays with, and the formulas that read
        // a skill protect their own results. No bonus leaves every value exactly as it was.
        return bonusBasisPoints == 0 ? played : ((played * (TickSkillTiredness.BasisPoints + bonusBasisPoints)) + (TickSkillTiredness.BasisPoints / 2)) / TickSkillTiredness.BasisPoints;
    }
}
