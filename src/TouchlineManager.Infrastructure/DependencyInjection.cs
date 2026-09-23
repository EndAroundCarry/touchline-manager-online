using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Infrastructure.Jobs;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure;

/// <summary>
/// Composition for the infrastructure layer: persistence, jobs, time, and external providers.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers persistence, the clock, and the durable job queue.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings__Database.");

        services.AddDbContext<TouchlineManagerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IJobQueue, PostgresJobQueue>();
        services.Configure<JobQueueOptions>(configuration.GetSection(JobQueueOptions.SectionName));

        return services;
    }

    /// <summary>
    /// Registers the job queue polling service.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="AddInfrastructure"/>: registering it there would make
    /// the API process execute deadlines, and the API must never advance a matchday (ADR-0001,
    /// ADR-0008). Only the worker composition root calls this.
    /// </remarks>
    public static IServiceCollection AddJobQueueWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<JobQueueWorker>();

        return services;
    }
}
