namespace TouchlineManager.Application.Abstractions.Jobs;

/// <summary>
/// Signals that a job failed for a reason retrying cannot fix — a violated invariant, a missing
/// referenced aggregate, an unsupported engine or rule-set version.
/// </summary>
/// <remarks>
/// Handlers throw this to be dead-lettered immediately and surfaced to operations, instead of
/// consuming the retry budget on an error that will fail identically every time (ADR-0003).
/// </remarks>
public sealed class PermanentJobFailureException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public PermanentJobFailureException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    public PermanentJobFailureException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an inner cause.</summary>
    public PermanentJobFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
