using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad;

/// <summary>What the retirement rule decides for one player at rollover (`CON-6`).</summary>
public enum RetirementDecision
{
    /// <summary>The player plays on.</summary>
    Continue = 0,

    /// <summary>The player announces that the coming season is their last; they retire next rollover.</summary>
    Announce = 1,

    /// <summary>The player retires now.</summary>
    Retire = 2,
}

/// <summary>Everything the retirement rule weighs about one player (`CON-6`).</summary>
/// <param name="Age">The age the player would enter the coming season at.</param>
/// <param name="IsGoalkeeper">Whether the player is a goalkeeper, who retires later than an outfielder.</param>
/// <param name="Ability">The player's ability on the 1–20 scale.</param>
/// <param name="ConditionBp">The player's condition in basis points, read as fitness (`TRN-5`).</param>
/// <param name="AlreadyAnnounced">Whether the player has already announced a retirement.</param>
public sealed record RetirementInput(
    int Age,
    bool IsGoalkeeper,
    int Ability,
    int ConditionBp,
    bool AlreadyAnnounced);

/// <summary>
/// The hidden announce-then-play retirement rule (`CON-6`).
/// </summary>
/// <remarks>
/// <para>
/// A pure, versioned function of the player's identity and the season, so the same player retires at the
/// same rollover every time and a world can be replayed. It reads no clock, repository, culture, or global
/// random source.
/// </para>
/// <para>
/// An announced player always retires at the next rollover — that is the "announce then play one season"
/// contract. Otherwise a player may announce from the start age, with a chance that grows each season and is
/// gated by ability and fitness, so only very good, very fit players reach the forced cap while the rest
/// retire in their early thirties. The start age, the growth, the gate, and the forced caps are all
/// <see cref="WorldRuleSet"/> values and are never exposed to a manager; only the announcement itself is a
/// fact about the coming season.
/// </para>
/// </remarks>
public static class RetirementPolicy
{
    /// <summary>The policy version, folded into the draw so a rule change reseeds the decision (`FIC-8`).</summary>
    public const string Version = "retirement-v1";

    /// <summary>The age an outfielder is forced to announce a retirement at.</summary>
    public static int ForcedAnnouncementAge(bool isGoalkeeper) => isGoalkeeper
        ? WorldRuleSet.RetirementForcedAnnouncementAgeGoalkeeper
        : WorldRuleSet.RetirementForcedAnnouncementAgeOutfield;

    /// <summary>The age a player is forced out of the game at.</summary>
    public static int ForcedAge(bool isGoalkeeper) => isGoalkeeper
        ? WorldRuleSet.RetirementForcedAgeGoalkeeper
        : WorldRuleSet.RetirementForcedAgeOutfield;

    /// <summary>Decides what the retirement rule does with one player this rollover.</summary>
    /// <param name="seasonId">The closing season, part of the draw's seed.</param>
    /// <param name="playerId">The player, the rest of the draw's seed.</param>
    /// <param name="input">What is known about the player.</param>
    public static RetirementDecision Decide(Guid seasonId, Guid playerId, RetirementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.AlreadyAnnounced)
        {
            return RetirementDecision.Retire;
        }

        // A player past the cap with no announcement on record is a defensive stop: the forced announcement
        // runs a season earlier, so this should never be reached in normal play.
        if (input.Age >= ForcedAge(input.IsGoalkeeper))
        {
            return RetirementDecision.Retire;
        }

        if (input.Age >= ForcedAnnouncementAge(input.IsGoalkeeper))
        {
            return RetirementDecision.Announce;
        }

        if (input.Age < WorldRuleSet.RetirementAnnouncementStartAge)
        {
            return RetirementDecision.Continue;
        }

        var chance = WorldRuleSet.RetirementAnnouncementChancePerMille(
            input.Age,
            input.Ability,
            input.ConditionBp);

        if (chance <= 0)
        {
            return RetirementDecision.Continue;
        }

        // One draw per player, seeded by the season and the player, so the decision is independent of the
        // order players are visited in.
        var draws = new Pcg32(DeterministicDigest.SeedOf(
            Version,
            seasonId.ToString("D"),
            playerId.ToString("D")));

        return draws.NextInt(1_000) < chance ? RetirementDecision.Announce : RetirementDecision.Continue;
    }
}
