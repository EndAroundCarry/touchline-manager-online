namespace TouchlineManager.MatchEngine.Randomness;

/// <summary>
/// The match engine's pseudo-random generator: PCG32, the XSH-RR variant.
/// </summary>
/// <remarks>
/// <para>
/// The engine ships its own copy rather than sharing <c>TouchlineManager.Domain</c>'s, and that
/// duplication is deliberate (ADR-0004). A generated world's reproducibility and a played match's
/// reproducibility are separate versioned contracts: this type's output is pinned by the engine's golden
/// hash tests, so a refactor of world generation must not be able to move a historical scoreline. The
/// dependency rules also forbid the sharing — the engine may not depend on <c>Domain</c> (DEP-2).
/// </para>
/// <para>
/// <see cref="System.Random"/> is not used anywhere in the engine. Its algorithm is an implementation
/// detail of the runtime, so a runtime upgrade could silently change every historical replay.
/// </para>
/// <para>
/// Arithmetic is unchecked and 64-bit throughout, which is what makes the sequence identical on every
/// platform and architecture a .NET runtime runs on. Nothing in this type reads a clock, culture, or
/// global state.
/// </para>
/// </remarks>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    /// <summary>The default stream selector. Any odd value produces a distinct sequence.</summary>
    public const ulong DefaultStream = 1442695040888963407UL;

    /// <summary>The number of distinct values <see cref="NextBasisPoints"/> can return.</summary>
    public const int BasisPointsScale = 10_000;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Initializes the generator at a seed, on a named stream.</summary>
    /// <param name="seed">The seed. The same seed and stream always produce the same sequence.</param>
    /// <param name="stream">The stream selector. One seed with different streams produces different sequences.</param>
    public Pcg32(ulong seed, ulong stream = DefaultStream)
    {
        // The increment must be odd for the congruential step to be full-period.
        _increment = (stream << 1) | 1UL;
        _state = 0UL;

        NextUInt32();

        unchecked
        {
            _state += seed;
        }

        NextUInt32();
    }

    /// <summary>Advances the generator and returns the next 32-bit value.</summary>
    public uint NextUInt32()
    {
        var previous = _state;

        unchecked
        {
            _state = (previous * Multiplier) + _increment;
        }

        return Permute(previous);
    }

    /// <summary>
    /// Returns a value in <c>[0, exclusiveUpperBound)</c>.
    /// </summary>
    /// <remarks>
    /// The multiply-and-shift reduction rather than modulo: modulo biases the low candidates whenever the
    /// bound does not divide 2^32, and a biased generator would make some draws more likely than others
    /// for no reason anyone could explain later.
    /// </remarks>
    /// <param name="exclusiveUpperBound">The exclusive upper bound. Must be positive.</param>
    public int NextInt(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        return (int)(((ulong)NextUInt32() * (ulong)exclusiveUpperBound) >> 32);
    }

    /// <summary>Returns the next 64-bit value, assembled from two draws in a fixed order.</summary>
    public ulong NextUInt64() => ((ulong)NextUInt32() << 32) | NextUInt32();

    /// <summary>Returns a value in <c>[minInclusive, maxInclusive]</c>.</summary>
    /// <param name="minInclusive">The inclusive lower bound.</param>
    /// <param name="maxInclusive">The inclusive upper bound. Must not be below the lower bound.</param>
    public int NextRange(int minInclusive, int maxInclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minInclusive, maxInclusive);

        return minInclusive + NextInt(maxInclusive - minInclusive + 1);
    }

    /// <summary>
    /// Returns a value in <c>[0, 10_000)</c>, the engine's probability scale.
    /// </summary>
    /// <remarks>
    /// The engine resolves every probabilistic decision as a comparison against integer basis points
    /// rather than against a floating-point probability. Two integer probabilities combine exactly
    /// (<c>a * b / 10_000</c>), so a decision at the end of a possession chain depends only on the
    /// values that produced it and not on the rounding behaviour of any intermediate representation.
    /// </remarks>
    public int NextBasisPoints() => NextInt(BasisPointsScale);

    /// <summary>
    /// Resolves a probabilistic decision: returns <see langword="true"/> when the draw falls inside the
    /// given probability.
    /// </summary>
    /// <param name="probabilityBasisPoints">
    /// The probability in basis points. Clamped to <c>[0, 10_000]</c>, so a caller that miscomputes a
    /// probability beyond certainty cannot invert the decision.
    /// </param>
    public bool RollBasisPoints(int probabilityBasisPoints)
    {
        var bounded = int.Clamp(probabilityBasisPoints, 0, BasisPointsScale);

        return NextBasisPoints() < bounded;
    }

    /// <summary>Gets the transformation from the previous state to the output (XSH-RR).</summary>
    private static uint Permute(ulong state)
    {
        unchecked
        {
            var xorshifted = (uint)(((state >> 18) ^ state) >> 27);
            var rotation = (int)(state >> 59);

            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }
    }
}
