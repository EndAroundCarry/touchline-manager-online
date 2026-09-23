using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TouchlineManager.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling.
/// </summary>
/// <remarks>
/// Migrations are created and scripted against this factory rather than through the API's
/// startup path, so migration generation never depends on application configuration, secrets, or
/// a running host. Production migrations run as a controlled pre-deploy job using the migration
/// database role (ADR-0008, MIG-5).
/// </remarks>
public sealed class TouchlineManagerDbContextFactory : IDesignTimeDbContextFactory<TouchlineManagerDbContext>
{
    private const string LocalFallbackConnectionString =
        "Host=localhost;Port=55432;Database=touchline;Username=touchline_app;Password=local_dev_password_change_me";

    /// <inheritdoc />
    public TouchlineManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? LocalFallbackConnectionString;

        var options = new DbContextOptionsBuilder<TouchlineManagerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TouchlineManagerDbContext(options);
    }
}
