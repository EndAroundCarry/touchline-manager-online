using Microsoft.Extensions.DependencyInjection;
using TouchlineManager.Application.Abstractions.Jobs;
using TouchlineManager.Application.Jobs;

namespace TouchlineManager.Application;

/// <summary>
/// Composition for the application layer: use cases, handlers, and policies.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application use cases, job handlers, and the handler registry.
    /// </summary>
    /// <remarks>
    /// Everything here is scoped, because use cases and job handlers work through the per-request or
    /// per-job unit of work. Registering them as singletons would capture a scoped
    /// <c>DbContext</c> and fail validation at startup.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IJobHandler, NoOpJobHandler>();
        services.AddScoped<JobHandlerRegistry>();
        services.AddScoped<EnqueueNoOpJob>();

        return services;
    }
}
