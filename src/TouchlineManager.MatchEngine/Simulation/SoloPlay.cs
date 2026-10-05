using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Ratings;
using TouchlineManager.MatchEngine.Spatial;

namespace TouchlineManager.MatchEngine.Simulation;

/// <summary>What the player on the ball does at a leg of the approach (`engine-v10`).</summary>
internal enum LegAction
{
    /// <summary>He plays the ball to a teammate.</summary>
    Pass = 0,

    /// <summary>He keeps it and carries it on.</summary>
    Dribble = 1,

    /// <summary>He shoots from where he stands.</summary>
    Shoot = 2,
}

/// <summary>
/// What each thing the holder could do is worth to him, 0…10,000, or null for one that is not open to him
/// (`engine-v10`).
/// </summary>
/// <param name="Pass">What the best-placed teammate he sees is worth, or null when he sees nobody he can give it to.</param>
/// <param name="Dribble">What carrying the ball on is worth; always open to him.</param>
/// <param name="Shoot">What a shot from where he is is worth, or null when he is too far out or it is not the moment.</param>
internal readonly record struct SoloUtilities(int? Pass, int Dribble, int? Shoot);

/// <summary>
/// How the player on the ball chooses between passing, dribbling on and shooting, for one leg of the approach
/// (`engine-v10`).
/// </summary>
/// <remarks>
/// <para>
/// Each option has a small integer utility on the same 0…10,000 scale as a receiver's score: the best receiver's score
/// for the pass; Dribbling and the space ahead for the dribble; Finishing, Composure and how near the goal he is for
/// the shot. The holder takes the option worth most to him with a chance that rises with his Decisions, and otherwise he
/// takes one of the others, in proportion to what they are worth. A holder who sees nobody to give it to, or whose
/// teammates are all marked, has a pass worth little and so dribbles or shoots: that is the one logic behind the
/// holder who keeps the ball because he did not see anybody and the one who does because nobody was free.
/// </para>
/// <para>
/// Pure and integer-only. The two draws it reads (one for the choice, one for which other option) are taken by the
/// caller on every leg, whether or not they are used, so the stream's shape never depends on what was decided.
/// </para>
/// </remarks>
internal static class SoloPlay
{
    private const int LowestHundredths = MatchAttributeNames.Min * EffectiveSkill.Scale;
    private const int HighestHundredths = MatchAttributeNames.Max * EffectiveSkill.Scale;

    /// <summary>Gets the chance, in basis points, that a holder takes the option worth most to him: his Decisions as an accuracy.</summary>
    /// <param name="holder">The player on the ball.</param>
    /// <param name="rules">The rules in force.</param>
    public static int BestChoiceChance(ActiveSlot holder, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(rules);

        return Interpolate(
            EffectiveSkill.Hundredths(holder, MatchAttributeName.Decisions, rules),
            rules.SoloBestChoiceLowestBasisPoints,
            rules.SoloBestChoiceHighestBasisPoints);
    }

    /// <summary>
    /// Gets what carrying the ball on is worth to a holder: his Dribbling, and how open the point ahead of him is,
    /// by the rules' weights, taken as a share of a pass's worth.
    /// </summary>
    /// <param name="holder">The player on the ball.</param>
    /// <param name="from">Where he is, in pitch coordinates.</param>
    /// <param name="defenders">The defending side, placed for the ball where it is.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static int DribbleUtility(
        ActiveSlot holder,
        SpatialPoint from,
        IReadOnlyList<OffBallPlayer> defenders,
        bool isHome,
        EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(rules);

        var ahead = PassagePlanner.FromAttack(
            Math.Min(PassagePlanner.AttackingX(from.X, isHome) + rules.SoloDribbleStep, SpatialPitch.PitchLength),
            PassagePlanner.AttackingY(from.Y, isHome),
            isHome);

        var space = OffBallModel.Openness(ahead, holder, defenders, rules);
        var skill = SkillScore(EffectiveSkill.Hundredths(holder, MatchAttributeName.Dribbling, rules));

        var weights = rules.SoloDribbleSkillWeight + rules.SoloDribbleSpaceWeight;
        var mix = (((long)skill * rules.SoloDribbleSkillWeight) + ((long)space * rules.SoloDribbleSpaceWeight)) / Math.Max(1, weights);

        return (int)(mix * rules.SoloDribbleUtilityBasisPoints / EngineRulesV2.Certain);
    }

    /// <summary>Gets whether a holder is in the band, near enough the goal and still outside the box, where a shot from distance is an option.</summary>
    /// <param name="from">Where he is, in pitch coordinates.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static bool CanShoot(SpatialPoint from, bool isHome, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var x = PassagePlanner.AttackingX(from.X, isHome);

        return x >= rules.LongShotMinX && x < rules.LongShotMaxX;
    }

    /// <summary>
    /// Gets what a shot from where the holder stands is worth to him: his Finishing and Composure, and how near the
    /// goal he is, by the rules' weights, taken as a share of a pass's worth.
    /// </summary>
    /// <param name="holder">The player on the ball.</param>
    /// <param name="from">Where he is, in pitch coordinates.</param>
    /// <param name="isHome">Whether the side attacks towards the high end of the pitch.</param>
    /// <param name="rules">The rules in force.</param>
    public static int ShootUtility(ActiveSlot holder, SpatialPoint from, bool isHome, EngineRulesV2 rules)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(rules);

        var skill = SkillScore(
            (EffectiveSkill.Hundredths(holder, MatchAttributeName.Finishing, rules)
                + EffectiveSkill.Hundredths(holder, MatchAttributeName.Composure, rules)) / 2);

        var reach = rules.LongShotMaxX - rules.LongShotMinX;
        var range = (int)Math.Clamp(
            (long)(PassagePlanner.AttackingX(from.X, isHome) - rules.LongShotMinX) * EngineRulesV2.Certain / Math.Max(1, reach),
            0,
            EngineRulesV2.Certain);

        var weights = rules.LongShotSkillWeight + rules.LongShotRangeWeight;
        var mix = (((long)skill * rules.LongShotSkillWeight) + ((long)range * rules.LongShotRangeWeight)) / Math.Max(1, weights);

        return (int)(mix * rules.LongShotUtilityBasisPoints / EngineRulesV2.Certain);
    }

    /// <summary>
    /// Chooses what the holder does: the option worth most to him with the chance his Decisions give, otherwise one of
    /// the others in proportion to what they are worth.
    /// </summary>
    /// <remarks>
    /// Ties go to the pass, then the dribble, so a holder with nothing to choose between plays the ball.
    /// </remarks>
    /// <param name="utilities">What each option is worth.</param>
    /// <param name="bestChance">The chance, in basis points, that he takes the best.</param>
    /// <param name="choiceDraw">A basis-point draw that decides whether he takes the best.</param>
    /// <param name="alternativeDraw">A basis-point draw that decides which of the others he takes instead.</param>
    public static LegAction Decide(SoloUtilities utilities, int bestChance, int choiceDraw, int alternativeDraw)
    {
        var options = new List<(LegAction Action, int Utility)>(3);

        if (utilities.Pass is int pass)
        {
            options.Add((LegAction.Pass, pass));
        }

        options.Add((LegAction.Dribble, utilities.Dribble));

        if (utilities.Shoot is int shoot)
        {
            options.Add((LegAction.Shoot, shoot));
        }

        var best = options[0];

        foreach (var option in options)
        {
            if (option.Utility > best.Utility)
            {
                best = option;
            }
        }

        if (choiceDraw < bestChance)
        {
            return best.Action;
        }

        var others = options.Where(option => option.Action != best.Action).ToList();

        if (others.Count == 0)
        {
            return best.Action;
        }

        // Each of the others is worth at least one, so a holder never has nothing to take instead.
        long total = others.Sum(option => (long)option.Utility + 1);
        var pick = (long)alternativeDraw * total / EngineRulesV2.Certain;

        foreach (var option in others)
        {
            pick -= (long)option.Utility + 1;

            if (pick < 0)
            {
                return option.Action;
            }
        }

        return others[^1].Action;
    }

    /// <summary>Places an effective skill, in hundredths, on a 0…10,000 line from the lowest skill to the highest.</summary>
    private static int SkillScore(int hundredths) =>
        (int)((long)(int.Clamp(hundredths, LowestHundredths, HighestHundredths) - LowestHundredths)
            * EngineRulesV2.Certain / (HighestHundredths - LowestHundredths));

    private static int Interpolate(int hundredths, int low, int high)
    {
        var bounded = int.Clamp(hundredths, LowestHundredths, HighestHundredths) - LowestHundredths;

        return low + (int)((long)(high - low) * bounded / (HighestHundredths - LowestHundredths));
    }
}
