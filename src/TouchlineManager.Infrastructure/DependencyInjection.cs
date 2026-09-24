using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Infrastructure.Email;
using TouchlineManager.Infrastructure.Jobs;
using TouchlineManager.Infrastructure.Persistence;
using TouchlineManager.Infrastructure.Persistence.Repositories;
using TouchlineManager.Infrastructure.Security;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure;

/// <summary>
/// Composition for the infrastructure layer: persistence, jobs, time, security, email, and external
/// providers.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers persistence, the clock, the durable job queue, and the auth providers.</summary>
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

        AddAuthInfrastructure(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the auth module's persistence and providers.
    /// </summary>
    /// <remarks>
    /// The hasher, the token service, and the token issuer are singletons because they hold no
    /// per-request state — the hasher in particular precomputes a throwaway hash once, and rebuilding
    /// it per request would both waste that work and change its timing profile.
    /// </remarks>
    private static void AddAuthInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshSessionRepository, RefreshSessionRepository>();
        services.AddScoped<IEmailTokenRepository, EmailTokenRepository>();
        services.AddScoped<IAuditWriter, EfAuditWriter>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
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
