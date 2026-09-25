using Microsoft.Extensions.Options;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Competition;
using TouchlineManager.Application.Abstractions.Match;
using TouchlineManager.Application.Abstractions.World;
using TouchlineManager.Domain.Match;
using TouchlineManager.MatchEngine;
using TouchlineManager.MatchEngine.Configuration;
using TouchlineManager.MatchEngine.Model;
using TouchlineManager.MatchEngine.Randomness;
using TouchlineManager.MatchEngine.Serialization;

namespace TouchlineManager.Application.Match;

/// <summary>What freezing a fixture produced.</summary>
/// <param name="Snapshot">The stored snapshot.</param>
/// <param name="Repairs">The repairs that were made, empty when it had already been frozen.</param>
/// <param name="Built">Whether this call built it rather than finding one already frozen.</param>
public sealed record FrozenMatchSnapshot(InputSnapshot Snapshot, IReadOnlyList<SnapshotRepair> Repairs, bool Built);

/// <summary>
/// Freezes a fixture's immutable input, once (`MAT-1`, `MAT-9`, master plan §7.3).
/// </summary>
/// <remarks>
/// <para>
/// The one place a snapshot is built, shared by the lock workflow and by a resolution that finds the lock
/// never ran: §7.3 is explicit that a delayed lock blocks kickoff rather than letting the match be
/// simulated from live tables, and the honest way to honour that is for the resolver to take the snapshot
/// itself rather than to simulate something else.
/// </para>
/// <para>
/// Freezing is idempotent by inspection rather than by hope: the fixture's existing snapshot is read
/// first, and a second call returns it untouched with no repairs. That is what makes a retried lock job
/// harmless, which every handler must be (§7.1, ADR-0003).
/// </para>
/// <para>
/// The seed is derived here, from the world's secret and the snapshot's own content, and attached
/// afterwards. Nothing upstream can influence it, and the commitment stored beside it is what the world
/// can later verify it against (`MAT-10`, `MAT-11`).
/// </para>
/// </remarks>
public sealed class MatchSnapshotFactory
{
    private readonly IMatchRepository _matches;
    private readonly IClock _clock;
    private readonly WorldOptions _world;

    /// <summary>Initializes the factory.</summary>
    public MatchSnapshotFactory(
        IMatchRepository matches,
        IClock clock,
        IOptions<WorldOptions> world)
    {
        ArgumentNullException.ThrowIfNull(world);

        _matches = matches;
        _clock = clock;
        _world = world.Value;
    }

    /// <summary>
    /// Freezes a fixture's input, or returns the one already frozen.
    /// </summary>
    /// <param name="sides">Both clubs, with everything their sides are built from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What freezing produced.</returns>
    /// <exception cref="UnplayableSquadException">When a club cannot field a legal side.</exception>
    public async Task<FrozenMatchSnapshot> FreezeAsync(
        FixtureSidesSnapshot sides,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sides);

        var existing = await _matches.FindSnapshotAsync(sides.FixtureId, cancellationToken);

        if (existing is not null)
        {
            return new FrozenMatchSnapshot(existing, [], false);
        }

        var rules = EngineRulesV1.Default;
        var built = MatchSnapshotBuilder.Build(sides, rules);

        // Two hashes, deliberately: the content hash covers the facts and derives the seed, and the input
        // hash covers the facts and the seed. Hashing the seed into its own derivation would be circular.
        var contentHash = CanonicalMatchSerializer.ContentHash(built.Input);
        var seed = MatchSeed.Derive(
            _world.SeedSecret,
            sides.FixtureId,
            contentHash,
            EngineVersions.EngineLabel);

        var input = built.Input with { Seed = seed };

        var snapshot = InputSnapshot.Freeze(
            Guid.CreateVersion7(),
            sides.FixtureId,
            EngineVersions.EngineLabel,
            EngineVersions.RuleSetLabel,
            seed,
            MatchSeed.CommitmentOf(seed, EngineVersions.EngineLabel),
            MatchSnapshotDocument.Write(input, built.Repairs),
            CanonicalMatchSerializer.InputHash(input),
            _clock.UtcNow);

        _matches.AddSnapshot(snapshot);

        return new FrozenMatchSnapshot(snapshot, built.Repairs, true);
    }

    /// <summary>
    /// Reads a frozen snapshot back into the engine's input, verifying that it still is what was frozen
    /// (`MAT-9`).
    /// </summary>
    /// <param name="snapshot">The stored snapshot.</param>
    /// <returns>The engine's input.</returns>
    /// <exception cref="InvalidMatchInputException">
    /// When the stored document does not reproduce the hash it was stored with, or does not carry the seed
    /// and commitment the row records.
    /// </exception>
    /// <remarks>
    /// The verification is the point of storing a hash at all. A snapshot that does not round-trip would
    /// otherwise simulate into a plausible result whose recorded provenance is a lie, and the failure would
    /// only surface when somebody tried to re-derive the match years later.
    /// </remarks>
    public static MatchInputV1 ReadVerified(InputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var content = MatchSnapshotDocument.Read(snapshot.SnapshotJson);
        var input = content.Input;

        if (input.Seed != snapshot.Seed)
        {
            throw new InvalidMatchInputException(
                $"The frozen snapshot of fixture {snapshot.FixtureId:D} carries a different seed than its row.");
        }

        if (!string.Equals(
            MatchSeed.CommitmentOf(input.Seed, snapshot.EngineVersion),
            snapshot.SeedCommitment,
            StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The frozen snapshot of fixture {snapshot.FixtureId:D} does not match its seed commitment (MAT-10).");
        }

        var hash = CanonicalMatchSerializer.InputHash(input);

        if (!string.Equals(hash, snapshot.SnapshotHash, StringComparison.Ordinal))
        {
            throw new InvalidMatchInputException(
                $"The frozen snapshot of fixture {snapshot.FixtureId:D} does not reproduce its own hash (MAT-9).");
        }

        return input;
    }
}
