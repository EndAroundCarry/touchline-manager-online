namespace TouchlineManager.Domain.World.Generation;

/// <summary>
/// A small, stable pseudo-random generator used by world generation.
/// </summary>
/// <remarks>
/// <para>
/// Permuted Congruential Generator (PCG32, the XSH-RR variant). The point of implementing it here
/// rather than using <see cref="Random"/> is reproducibility: <see cref="Random"/>'s algorithm is an
/// implementation detail of the runtime and may change between releases, so a world seeded today could
/// not be regenerated tomorrow. This type's output is fixed by its own source, and the golden-sequence
/// tests pin it (`FIC-7`, `PYR-14`).
/// </para>
/// <para>
/// The match engine ships its own copy for the same reason and does <em>not</em> share this one. A
/// generated world's reproducibility and a played match's reproducibility are separate versioned
/// contracts: the engine's output hashes are pinned per engine version (ADR-0004), so refactoring world
/// generation must not be able to move a historical scoreline. The dependency rules also forbid the
/// sharing — <c>Domain</c> depends on nothing (DEP-1) and the engine may not depend on it (DEP-2).
/// </para>
/// <para>
/// Arithmetic is deliberately unchecked and uses a 64-bit state, which is what makes the sequence
/// identical on every platform a .NET runtime runs on.
/// </para>
/// </remarks>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    /// <summary>The default stream selector. Any odd value produces a distinct sequence.</summary>
    private const ulong DefaultIncrement = 1442695040888963407UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Initializes the generator at a seed, on a named stream.</summary>
    /// <param name="seed">The seed. The same seed and stream always produce the same sequence.</param>
    /// <param name="stream">The stream selector. Two generators with one seed and different streams produce different sequences.</param>
    public Pcg32(ulong seed, ulong stream = DefaultIncrement)
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
    /// Uses the multiply-and-shift reduction rather than modulo. Modulo would bias the low
    /// candidates whenever the bound does not divide 2^32, and a biased generator would make some
    /// generated identities more likely than others for no reason anyone could explain later.
    /// </remarks>
    /// <param name="exclusiveUpperBound">The exclusive upper bound. Must be positive.</param>
    public int NextInt(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

        return (int)(((ulong)NextUInt32() * (ulong)exclusiveUpperBound) >> 32);
    }

    /// <summary>Returns the next 64-bit value, assembled from two draws in a fixed order.</summary>
    public ulong NextUInt64() => ((ulong)NextUInt32() << 32) | NextUInt32();

    /// <summary>Returns a double in <c>[0, 1)</c> with 53 bits of precision.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

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
