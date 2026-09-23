using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TouchlineManager.Infrastructure.Logging;

/// <summary>
/// Wraps another logger so its output passes through <see cref="LogRedactor"/>.
/// </summary>
internal sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;

    public RedactingLogger(ILogger inner) => _inner = inner;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!_inner.IsEnabled(logLevel))
        {
            return;
        }

        var message = LogRedactor.Redact(formatter(state, exception));

        if (exception is not null)
        {
            // The stack trace is useful and not sensitive, but an exception message can carry a
            // connection string or a token. Redact it, then hand the pre-formatted text to the
            // inner logger with no exception so the inner logger cannot print the raw message.
            message = message + Environment.NewLine + LogRedactor.Redact(exception.ToString());
        }

        _inner.Log(logLevel, eventId, message, null, static (m, _) => m);
    }
}

/// <summary>Wraps an inner provider so every logger it creates is redacting.</summary>
internal sealed class RedactingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;

    public RedactingLoggerProvider(ILoggerProvider inner) => _inner = inner;

    public ILogger CreateLogger(string categoryName) => new RedactingLogger(_inner.CreateLogger(categoryName));

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Registers the redaction filter over every provider configured so far.
/// </summary>
public static class LogRedactionExtensions
{
    /// <summary>
    /// Wraps each <see cref="ILoggerProvider"/> already registered on the builder in a redacting
    /// decorator. Call this after the sink providers are added.
    /// </summary>
    /// <remarks>
    /// Only providers registered as a service are wrapped, which is how every provider added
    /// through <see cref="LoggingBuilderExtensions"/> is registered. A provider constructed outside
    /// the container would bypass redaction, so sinks must be added through the builder.
    /// </remarks>
    public static ILoggingBuilder AddLogRedaction(this ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var providers = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider))
            .ToList();

        foreach (var descriptor in providers)
        {
            builder.Services.Remove(descriptor);
            builder.Services.Add(ServiceDescriptor.Singleton<ILoggerProvider>(
                serviceProvider => new RedactingLoggerProvider(Resolve(serviceProvider, descriptor))));
        }

        return builder;
    }

    private static ILoggerProvider Resolve(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is ILoggerProvider instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (ILoggerProvider)descriptor.ImplementationFactory(serviceProvider);
        }

        var implementationType = descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                $"Cannot wrap logger provider descriptor '{descriptor}' because it declares neither a type, a factory, nor an instance.");

        return (ILoggerProvider)ActivatorUtilities.CreateInstance(serviceProvider, implementationType);
    }
}
