using FluentAssertions;
using Npgsql;
using Tactical11Manager.Infrastructure.Jobs;

namespace Tactical11Manager.Infrastructure.Tests.Ops;

/// <summary>
/// Which failures the worker runs again at once: a transaction PostgreSQL rolled back to keep two of them consistent, however it was wrapped.
/// </summary>
public sealed class SerializationConflictTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void A_serialization_failure_or_a_deadlock_is_a_conflict(string sqlState)
    {
        SerializationConflict.IsConflict(Postgres(sqlState)).Should().BeTrue();
    }

    [Fact]
    public void A_conflict_wrapped_by_ef_core_is_still_found()
    {
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new Microsoft.EntityFrameworkCore.DbUpdateException("An error occurred while saving the entity changes.", Postgres("40001")));

        SerializationConflict.IsConflict(wrapped).Should().BeTrue();
        SerializationConflict.IsConflict(new AggregateException(new TimeoutException(), wrapped)).Should().BeTrue();
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("57014")]
    public void Any_other_database_failure_is_not(string sqlState)
    {
        // A unique violation or a cancelled statement would fail the same way again; those stay with the queue's backoff.
        SerializationConflict.IsConflict(new InvalidOperationException("wrapped", Postgres(sqlState))).Should().BeFalse();
    }

    [Fact]
    public void A_failure_that_is_not_from_the_database_is_not()
    {
        SerializationConflict.IsConflict(new InvalidOperationException("Matchday is not fully staged")).Should().BeFalse();
        SerializationConflict.IsConflict(null).Should().BeFalse();
    }

    private static PostgresException Postgres(string sqlState) => new("a database failure", "ERROR", "ERROR", sqlState);
}
