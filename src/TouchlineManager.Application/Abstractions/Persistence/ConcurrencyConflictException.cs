namespace TouchlineManager.Application.Abstractions.Persistence;

/// <summary>
/// The optimistic concurrency check on a write failed: the row changed after it was read.
/// </summary>
/// <remarks>
/// Infrastructure translates the persistence provider's own concurrency exception into this type, so
/// the application layer can answer <c>412 Precondition Failed</c> without depending on EF Core
/// (ADR-0009, CONC-1).
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public ConcurrencyConflictException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an inner cause.</summary>
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
