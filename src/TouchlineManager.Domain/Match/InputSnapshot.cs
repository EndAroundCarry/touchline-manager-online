namespace TouchlineManager.Domain.Match;

/// <summary>
/// The frozen facts one fixture was prepared from: the only thing its match may be simulated from
/// (`MAT-1`, master plan §6.6, §8.3).
/// </summary>
/// <remarks>
/// <para>
/// This row is written once, at the team-sheet lock, and never changes. That is the whole point: the
/// simulation reads this and nothing else, so a squad that trains, an injury that lands, or a plan that
/// is rewritten after the deadline cannot alter a match that has not kicked off yet. A delayed lock blocks
/// kickoff rather than simulating from whatever the live tables happen to hold (§7.3, ADR-0004).
/// </para>
/// <para>
/// The type has no mutators at all, which is how "immutable after the fixture enters
/// <see cref="Competition.FixtureStatus.Locked"/>" is enforced rather than promised. The database
/// carries the matching guarantee through a unique constraint on the fixture.
/// </para>
/// <para>
/// <see cref="SnapshotJson"/> is the frozen engine input as a versioned document, and
/// <see cref="SnapshotHash"/> is the canonical hash of that input. Re-reading the document and hashing it
/// again must reproduce the hash exactly, and the workflow verifies that before it simulates: a
/// snapshot that does not round-trip is a defect worth refusing rather than a result worth publishing
/// (`MAT-9`).
/// </para>
/// <para>
/// <see cref="Seed"/> is the derivation material and is never serialized to a manager (`MAT-11`); what
/// the world can see is <see cref="SeedCommitment"/>. Nothing in the domain decides the seed — it is
/// derived by the caller from the world's secret, so no manager and no client can influence it
/// (master plan §8.2).
/// </para>
/// </remarks>
public sealed class InputSnapshot
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private InputSnapshot()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the fixture the snapshot was taken for. Unique, because a fixture is frozen once.</summary>
    public Guid FixtureId { get; private set; }

    /// <summary>Gets the engine version the input was frozen for.</summary>
    public string EngineVersion { get; private set; } = string.Empty;

    /// <summary>Gets the engine rules version the input was frozen for.</summary>
    public string RuleSetVersion { get; private set; } = string.Empty;

    /// <summary>Gets the derived match seed. Server-only: never serialized to a manager (`MAT-11`).</summary>
    public ulong Seed { get; private set; }

    /// <summary>Gets the publishable commitment to <see cref="Seed"/> (`MAT-10`).</summary>
    public string SeedCommitment { get; private set; } = string.Empty;

    /// <summary>Gets the frozen engine input, as the versioned document the hash is taken over.</summary>
    public string SnapshotJson { get; private set; } = string.Empty;

    /// <summary>Gets the canonical hash of the complete input, seed included (`MAT-9`).</summary>
    public string SnapshotHash { get; private set; } = string.Empty;

    /// <summary>Gets when the snapshot was frozen.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Freezes a fixture's input.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="fixtureId">The fixture being frozen.</param>
    /// <param name="engineVersion">The engine version label the input was built for.</param>
    /// <param name="ruleSetVersion">The engine rules version label the input was built for.</param>
    /// <param name="seed">The derived seed.</param>
    /// <param name="seedCommitment">The publishable commitment to the seed.</param>
    /// <param name="snapshotJson">The frozen input document.</param>
    /// <param name="snapshotHash">The canonical hash of the input.</param>
    /// <param name="now">The current instant.</param>
    public static InputSnapshot Freeze(
        Guid id,
        Guid fixtureId,
        string engineVersion,
        string ruleSetVersion,
        ulong seed,
        string seedCommitment,
        string snapshotJson,
        string snapshotHash,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleSetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedCommitment);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotHash);

        if (fixtureId == Guid.Empty)
        {
            throw new ArgumentException("A snapshot belongs to a fixture.", nameof(fixtureId));
        }

        return new InputSnapshot
        {
            Id = id,
            FixtureId = fixtureId,
            EngineVersion = engineVersion,
            RuleSetVersion = ruleSetVersion,
            Seed = seed,
            SeedCommitment = seedCommitment,
            SnapshotJson = snapshotJson,
            SnapshotHash = snapshotHash,
            CreatedAt = now,
        };
    }
}
