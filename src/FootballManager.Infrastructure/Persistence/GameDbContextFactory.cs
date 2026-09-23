using FootballManager.Application.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FootballManager.Infrastructure.Persistence;

public sealed class GameDbContextFactory : IDesignTimeDbContextFactory<GameDbContext>
{
    public const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=55432;Database=footballmanager;Username=footballmanager;Password=localdev-only-change-me";

    public GameDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(LocalDevelopmentConnectionString)
            .Options;
        return new GameDbContext(options, new SystemClock());
    }
}
