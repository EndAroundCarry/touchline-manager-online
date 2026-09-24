namespace TouchlineManager.Domain.World;

/// <summary>What a generation run produced (`PYR-14`, master plan §6.3).</summary>
public enum GenerationRunKind
{
    /// <summary>The initial world: countries, tier 1 in each, and their clubs.</summary>
    WorldBootstrap = 0,

    /// <summary>A provisioned lower tier.</summary>
    DivisionProvisioning = 1,
}

/// <summary>How a generation run ended.</summary>
public enum GenerationRunStatus
{
    /// <summary>In progress.</summary>
    Running = 0,

    /// <summary>Every artifact was produced and validated.</summary>
    Succeeded = 1,

    /// <summary>Generation stopped. Diagnostics explain why.</summary>
    Failed = 2,
}

/// <summary>Storage and transport representation of generation kinds and statuses.</summary>
public static class GenerationRuns
{
    /// <summary>The code for <see cref="GenerationRunKind.WorldBootstrap"/>.</summary>
    public const string WorldBootstrapCode = "world_bootstrap";

    /// <summary>The code for <see cref="GenerationRunKind.DivisionProvisioning"/>.</summary>
    public const string DivisionProvisioningCode = "division_provisioning";

    /// <summary>The code for <see cref="GenerationRunStatus.Running"/>.</summary>
    public const string RunningCode = "running";

    /// <summary>The code for <see cref="GenerationRunStatus.Succeeded"/>.</summary>
    public const string SucceededCode = "succeeded";

    /// <summary>The code for <see cref="GenerationRunStatus.Failed"/>.</summary>
    public const string FailedCode = "failed";

    /// <summary>Converts a kind to its stable code.</summary>
    public static string ToCode(this GenerationRunKind kind) => kind switch
    {
        GenerationRunKind.WorldBootstrap => WorldBootstrapCode,
        GenerationRunKind.DivisionProvisioning => DivisionProvisioningCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown generation kind."),
    };

    /// <summary>Parses a stable code back to a kind.</summary>
    public static GenerationRunKind KindFromCode(string code) => code switch
    {
        WorldBootstrapCode => GenerationRunKind.WorldBootstrap,
        DivisionProvisioningCode => GenerationRunKind.DivisionProvisioning,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown generation kind code."),
    };

    /// <summary>Converts a status to its stable code.</summary>
    public static string ToCode(this GenerationRunStatus status) => status switch
    {
        GenerationRunStatus.Running => RunningCode,
        GenerationRunStatus.Succeeded => SucceededCode,
        GenerationRunStatus.Failed => FailedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown run status."),
    };

    /// <summary>Parses a stable code back to a status.</summary>
    public static GenerationRunStatus StatusFromCode(string code) => code switch
    {
        RunningCode => GenerationRunStatus.Running,
        SucceededCode => GenerationRunStatus.Succeeded,
        FailedCode => GenerationRunStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown run status code."),
    };
}

/// <summary>
/// An auditable record of one generation, so any generated club or player can be traced back to the
/// inputs that produced it (`PYR-14`).
/// </summary>
/// <remarks>
/// <para>
/// The seed and generator version are the reproducibility contract: the same pair must produce the
/// same logical output. The counts are stored as ordinary columns rather than a JSONB blob because
/// operations query them — "did the last three provisioning runs produce 18 clubs each?" is a question
/// a runbook asks, and the JSONB policy forbids putting a queryable field in a document (`JSN-4`).
/// </para>
/// <para>
/// <see cref="InputHash"/> covers the inputs that are not the seed: the existing world state the run
/// built on. Two runs with the same seed but different starting conditions are expected to differ,
/// and this is what makes that visible instead of looking like a reproducibility bug.
/// </para>
/// </remarks>
public sealed class GenerationRun
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private GenerationRun()
    {
    }

    /// <summary>Gets the run identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets what was being generated.</summary>
    public GenerationRunKind Kind { get; private set; }

    /// <summary>Gets the seed every deterministic decision was derived from.</summary>
    public string Seed { get; private set; } = string.Empty;

    /// <summary>Gets the generator version. A change to generation logic must bump this.</summary>
    public string GeneratorVersion { get; private set; } = string.Empty;

    /// <summary>Gets the hash of the non-seed inputs, so a differing rerun is explainable.</summary>
    public string InputHash { get; private set; } = string.Empty;

    /// <summary>Gets the lifecycle state.</summary>
    public GenerationRunStatus Status { get; private set; }

    /// <summary>Gets how many countries the run created.</summary>
    public int CountriesCreated { get; private set; }

    /// <summary>Gets how many clubs the run created.</summary>
    public int ClubsCreated { get; private set; }

    /// <summary>Gets how many players the run created.</summary>
    public int PlayersCreated { get; private set; }

    /// <summary>Gets how many club finance accounts the run created.</summary>
    public int AccountsCreated { get; private set; }

    /// <summary>Gets the failure diagnostics, when the run failed.</summary>
    public string? Diagnostics { get; private set; }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when the run finished, successfully or not.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Starts a run record.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="kind">What is being generated.</param>
    /// <param name="seed">The generation seed.</param>
    /// <param name="generatorVersion">The generator version.</param>
    /// <param name="inputHash">The hash of the non-seed inputs.</param>
    /// <param name="now">The current instant.</param>
    public static GenerationRun Start(
        Guid id,
        GenerationRunKind kind,
        string seed,
        string generatorVersion,
        string inputHash,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorVersion);

        return new GenerationRun
        {
            Id = id,
            Kind = kind,
            Seed = seed,
            GeneratorVersion = generatorVersion,
            InputHash = inputHash,
            Status = GenerationRunStatus.Running,
            StartedAt = now,
        };
    }

    /// <summary>Records what the run produced and marks it succeeded.</summary>
    /// <param name="counts">The output counts.</param>
    /// <param name="now">The current instant.</param>
    public void Succeed(GenerationRunCounts counts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counts);

        CountriesCreated = counts.Countries;
        ClubsCreated = counts.Clubs;
        PlayersCreated = counts.Players;
        AccountsCreated = counts.Accounts;
        Status = GenerationRunStatus.Succeeded;
        CompletedAt = now;
    }

    /// <summary>Marks the run failed with diagnostics.</summary>
    /// <param name="diagnostics">What went wrong.</param>
    /// <param name="now">The current instant.</param>
    public void Fail(string diagnostics, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostics);

        Status = GenerationRunStatus.Failed;
        Diagnostics = diagnostics;
        CompletedAt = now;
    }
}

/// <summary>What one generation run produced.</summary>
/// <param name="Countries">Countries created.</param>
/// <param name="Clubs">Clubs created.</param>
/// <param name="Players">Players created.</param>
/// <param name="Accounts">Club finance accounts created.</param>
public sealed record GenerationRunCounts(int Countries, int Clubs, int Players, int Accounts);
