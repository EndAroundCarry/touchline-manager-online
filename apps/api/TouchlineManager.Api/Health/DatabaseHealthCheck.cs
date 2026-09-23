using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TouchlineManager.Infrastructure.Persistence;

namespace TouchlineManager.Api.Health;

/// <summary>
/// Reports whether the database is actually reachable, not merely configured.
/// </summary>
/// <remarks>
/// Readiness must mean "can serve a request", so this check opens a connection and issues a
/// statement. A configured-but-unreachable database is the failure that otherwise looks healthy
/// right up to the first real request.
/// </remarks>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly TouchlineManagerDbContext _dbContext;

    /// <summary>Initializes the check.</summary>
    public DatabaseHealthCheck(TouchlineManagerDbContext dbContext) => _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _dbContext.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "select 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}
