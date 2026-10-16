using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tactical11Manager.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling.
/// </summary>
/// <remarks>
/// Migrations are created and scripted against this factory rather than through the API's
/// startup path, so migration generation never depends on application configuration, secrets, or
/// a running host. Production migrations run as a controlled pre-deploy job using the migration
/// database role (ADR-0008, MIG-5).
/// </remarks>
public sealed class Tactical11ManagerDbContextFactory : IDesignTimeDbContextFactory<Tactical11ManagerDbContext>
{
    private const string LocalFallbackConnectionString =
        "Host=localhost;Port=55432;Database=tactical11;Username=tactical11_app;Password=local_dev_password_change_me";

    /// <inheritdoc />
    public Tactical11ManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? LocalFallbackConnectionString;

        var options = new DbContextOptionsBuilder<Tactical11ManagerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new Tactical11ManagerDbContext(options);
    }
}
