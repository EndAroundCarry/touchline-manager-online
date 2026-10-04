using System.Globalization;
using TouchlineManager.Domain.World.Generation;

namespace TouchlineManager.Domain.Squad.Training;

/// <summary>
/// A player's hidden training aptitude: how quickly they turn training into improvement (`TRN-15`).
/// </summary>
/// <remarks>
/// <para>
/// A pure derivation from the player's identity, so it is stored nowhere: there is no migration, no
/// player-generator draw-order change, and every existing world has an aptitude from the first day. Like
/// <c>Potential</c> it is never sent to a client.
/// </para>
/// <para>
/// The range is triangular around neutral, so most players are close to average and a few are clearly
/// quick or slow learners. A high-potential player with a low aptitude may never reach their potential
/// before the age curve flattens, which is how a hidden star or a late disappointment emerges.
/// </para>
/// </remarks>
public static class TrainingAptitude
{
    /// <summary>The version of the derivation, which is part of its seed.</summary>
    public const string Version = "aptitude-v1";

    /// <summary>The neutral aptitude, in permille.</summary>
    public const int Neutral = 1_000;

    /// <summary>The slowest learner's aptitude, in permille.</summary>
    public const int MinPermille = 550;

    /// <summary>The quickest learner's aptitude, in permille.</summary>
    public const int MaxPermille = 1_450;

    private const int HalfSpan = 451;

    /// <summary>Derives a player's aptitude, in permille of a neutral learner.</summary>
    /// <param name="playerId">The player.</param>
    public static int For(Guid playerId)
    {
        var rng = new Pcg32(DeterministicDigest.SeedOf(
            playerId.ToString("D", CultureInfo.InvariantCulture),
            "training-aptitude",
            Version));

        return MinPermille + rng.NextInt(HalfSpan) + rng.NextInt(HalfSpan);
    }
}
