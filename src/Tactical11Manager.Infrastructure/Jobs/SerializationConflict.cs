using Npgsql;

namespace Tactical11Manager.Infrastructure.Jobs;

/// <summary>
/// Recognises the failures PostgreSQL raises when two transactions collide and one is rolled back to keep them consistent.
/// </summary>
/// <remarks>
/// A serializable transaction that would break the serial order is aborted with <c>40001</c>, and a deadlock victim with <c>40P01</c>. Neither
/// means the work was wrong: the transaction was rolled back whole, and running it again is the documented remedy. They reach the worker wrapped,
/// by EF Core's execution strategy or its update pipeline, so the whole inner chain is searched.
/// </remarks>
public static class SerializationConflict
{
    /// <summary>The SQLSTATE of a serialization failure.</summary>
    public const string SerializationFailure = "40001";

    /// <summary>The SQLSTATE of a deadlock victim.</summary>
    public const string DeadlockDetected = "40P01";

    /// <summary>Reports whether a failure, or anything it wraps, is a serialization failure or a deadlock.</summary>
    /// <param name="exception">The failure.</param>
    public static bool IsConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: SerializationFailure or DeadlockDetected })
            {
                return true;
            }

            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(IsConflict))
            {
                return true;
            }
        }

        return false;
    }
}
