namespace TouchlineManager.Domain.Match;

/// <summary>How a simulation attempt ended (`MAT-7`, master plan §6.6).</summary>
public enum SimulationAttemptStatus
{
    /// <summary>The attempt produced a result, which is now staged.</summary>
    Succeeded = 0,

    /// <summary>The attempt failed and produced nothing.</summary>
    Failed = 1,
}

/// <summary>Stable codes for <see cref="SimulationAttemptStatus"/>.</summary>
public static class SimulationAttemptStatuses
{
    /// <summary>The code for <see cref="SimulationAttemptStatus.Succeeded"/>.</summary>
    public const string SucceededCode = "succeeded";

    /// <summary>The code for <see cref="SimulationAttemptStatus.Failed"/>.</summary>
    public const string FailedCode = "failed";

    /// <summary>The longest code, used to size the storage column.</summary>
    public const int MaxCodeLength = 9;

    /// <summary>Converts a status to its stable code.</summary>
    /// <param name="status">The attempt status.</param>
    public static string ToCode(this SimulationAttemptStatus status) => status switch
    {
        SimulationAttemptStatus.Succeeded => SucceededCode,
        SimulationAttemptStatus.Failed => FailedCode,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown attempt status."),
    };

    /// <summary>Parses a stable code back to its status.</summary>
    /// <param name="code">The stable code.</param>
    public static SimulationAttemptStatus FromCode(string code) => code switch
    {
        SucceededCode => SimulationAttemptStatus.Succeeded,
        FailedCode => SimulationAttemptStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown attempt status code."),
    };
}

/// <summary>
/// One try at simulating one fixture (master plan §6.6, §7.4).
/// </summary>
/// <remarks>
/// <para>
/// Attempts exist so a failure is a durable fact rather than a log line. A matchday that cannot simulate
/// one of its nine fixtures must not publish the other eight (`MAT-7`), and the operations answer to
/// "why is this round stuck?" is the failed attempt rows, which name the engine version, the input hash,
/// and the error category without any partial result having been written.
/// </para>
/// <para>
/// A successful attempt is retained as well as recorded on the match: re-running a completed attempt
/// must reproduce the same output hash (`MAT-9`), and the pair of attempt rows — the original and the
/// re-derived one — is the evidence that it did.
/// </para>
/// </remarks>
public sealed class SimulationAttempt
{
    /// <summary>Initializes an empty instance for materialization by the persistence layer.</summary>
    private SimulationAttempt()
    {
    }

    /// <summary>Gets the identity (UUIDv7, server-generated).</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the fixture that was simulated.</summary>
    public Guid FixtureId { get; private set; }

    /// <summary>Gets the durable job that drove the attempt, when a job did.</summary>
    public Guid? JobId { get; private set; }

    /// <summary>Gets the 1-based attempt number for the fixture.</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>Gets the engine version the attempt ran.</summary>
    public string EngineVersion { get; private set; } = string.Empty;

    /// <summary>Gets how the attempt ended.</summary>
    public SimulationAttemptStatus Status { get; private set; }

    /// <summary>Gets the input snapshot's hash.</summary>
    public string? InputHash { get; private set; }

    /// <summary>Gets the result's hash, when the attempt produced one.</summary>
    public string? OutputHash { get; private set; }

    /// <summary>Gets the category a failure was classified under, when it failed.</summary>
    public string? ErrorCategory { get; private set; }

    /// <summary>Gets the failure's message, when it failed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Gets when the attempt started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when the attempt finished.</summary>
    public DateTimeOffset CompletedAt { get; private set; }

    /// <summary>Gets how long the attempt took, in milliseconds.</summary>
    public long DurationMilliseconds { get; private set; }

    /// <summary>Records a successful attempt.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="fixtureId">The simulated fixture.</param>
    /// <param name="jobId">The job that drove the attempt, when one did.</param>
    /// <param name="attemptNumber">The 1-based attempt number for the fixture.</param>
    /// <param name="engineVersion">The engine version the attempt ran.</param>
    /// <param name="inputHash">The input snapshot's hash.</param>
    /// <param name="outputHash">The result's hash.</param>
    /// <param name="startedAt">When the attempt started.</param>
    /// <param name="completedAt">When the attempt finished.</param>
    public static SimulationAttempt Succeeded(
        Guid id,
        Guid fixtureId,
        Guid? jobId,
        int attemptNumber,
        string engineVersion,
        string inputHash,
        string outputHash,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var attempt = Start(
            id,
            fixtureId,
            jobId,
            attemptNumber,
            engineVersion,
            startedAt,
            completedAt);

        attempt.Status = SimulationAttemptStatus.Succeeded;
        attempt.InputHash = Require(inputHash, nameof(inputHash));
        attempt.OutputHash = Require(outputHash, nameof(outputHash));

        return attempt;
    }

    /// <summary>Records a failed attempt, which produced no result.</summary>
    /// <param name="id">A server-generated identity.</param>
    /// <param name="fixtureId">The fixture that could not be simulated.</param>
    /// <param name="jobId">The job that drove the attempt, when one did.</param>
    /// <param name="attemptNumber">The 1-based attempt number for the fixture.</param>
    /// <param name="engineVersion">The engine version the attempt ran.</param>
    /// <param name="errorCategory">The category the failure was classified under.</param>
    /// <param name="errorMessage">What went wrong.</param>
    /// <param name="startedAt">When the attempt started.</param>
    /// <param name="completedAt">When the attempt finished.</param>
    public static SimulationAttempt Failed(
        Guid id,
        Guid fixtureId,
        Guid? jobId,
        int attemptNumber,
        string engineVersion,
        string errorCategory,
        string errorMessage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var attempt = Start(
            id,
            fixtureId,
            jobId,
            attemptNumber,
            engineVersion,
            startedAt,
            completedAt);

        attempt.Status = SimulationAttemptStatus.Failed;
        attempt.ErrorCategory = Require(errorCategory, nameof(errorCategory));
        attempt.ErrorMessage = Require(errorMessage, nameof(errorMessage));

        return attempt;
    }

    private static SimulationAttempt Start(
        Guid id,
        Guid fixtureId,
        Guid? jobId,
        int attemptNumber,
        string engineVersion,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);

        if (fixtureId == Guid.Empty)
        {
            throw new ArgumentException("An attempt belongs to a fixture.", nameof(fixtureId));
        }

        if (completedAt < startedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAt),
                completedAt,
                "An attempt cannot finish before it started.");
        }

        return new SimulationAttempt
        {
            Id = id,
            FixtureId = fixtureId,
            JobId = jobId,
            AttemptNumber = attemptNumber,
            EngineVersion = engineVersion,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            DurationMilliseconds = (long)(completedAt - startedAt).TotalMilliseconds,
        };
    }

    private static string Require(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        return value;
    }
}
