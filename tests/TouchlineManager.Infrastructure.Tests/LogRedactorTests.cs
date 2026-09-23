using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchlineManager.Infrastructure.Logging;

namespace TouchlineManager.Infrastructure.Tests;

/// <summary>
/// The redaction list in <c>docs/security/data-classification.md</c> §4 is a security control, so it
/// is asserted rather than assumed.
/// </summary>
public sealed class LogRedactorTests
{
    [Theory]
    [InlineData("Password=local_dev_password_change_me")]
    [InlineData("pwd: hunter2")]
    [InlineData("refresh_token=abc123def456")]
    [InlineData("accessToken=abc123def456")]
    [InlineData("Authorization: Bearer abcdef0123456789")]
    [InlineData("security_stamp=stampvalue")]
    [InlineData("api_key=key-12345")]
    [InlineData("Api_Key: key-12345")]
    public void Named_secrets_are_redacted(string message)
    {
        var redacted = LogRedactor.Redact(message);

        redacted.Should().Contain(LogRedactor.Mask);
        redacted.Should().NotContain("local_dev_password_change_me");
        redacted.Should().NotContain("hunter2");
        redacted.Should().NotContain("abc123def456");
        redacted.Should().NotContain("stampvalue");
        redacted.Should().NotContain("key-12345");
    }

    [Fact]
    public void Connection_string_passwords_are_redacted()
    {
        const string ConnectionString =
            "Host=db.internal;Port=5432;Database=touchline;Username=app;Password=s3cr3t-value;Pooling=true";

        var redacted = LogRedactor.Redact(ConnectionString);

        redacted.Should().NotContain("s3cr3t-value");
        redacted.Should().Contain("Host=db.internal");
        redacted.Should().Contain("Pooling=true");
    }

    [Fact]
    public void Json_web_tokens_are_redacted()
    {
        const string Token =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        LogRedactor.Redact($"Bearer {Token}").Should().NotContain(Token);
    }

    [Fact]
    public void Email_addresses_are_redacted()
    {
        LogRedactor.Redact("Login failed for manager@example.com")
            .Should()
            .Be($"Login failed for {LogRedactor.Mask}");
    }

    [Fact]
    public void Raw_ip_addresses_are_redacted()
    {
        LogRedactor.Redact("Rate limit exceeded for 203.0.113.42 on route /auth/login")
            .Should()
            .NotContain("203.0.113.42");
    }

    [Fact]
    public void Ordinary_messages_are_left_intact()
    {
        const string Message = "Claimed 4 job(s) for fixture 018f4c1e locking in 30 minutes.";

        LogRedactor.Redact(Message).Should().Be(Message);
    }

    [Fact]
    public void Empty_input_is_tolerated()
    {
        LogRedactor.Redact(null).Should().BeEmpty();
        LogRedactor.Redact(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Logger_provider_decorator_redacts_what_handlers_write()
    {
        var capturing = new CapturingLoggerProvider();

        using var provider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.AddProvider(capturing);
                builder.AddLogRedaction();
            })
            .BuildServiceProvider();

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        logger.LogInformation(
            "Connecting with Password=top-secret to 198.51.100.7 as manager@example.com");

        capturing.Messages.Should().ContainSingle();
        capturing.Messages[0].Should().NotContain("top-secret");
        capturing.Messages[0].Should().NotContain("198.51.100.7");
        capturing.Messages[0].Should().NotContain("manager@example.com");
    }

    [Fact]
    public void Logger_provider_decorator_redacts_exception_text()
    {
        var capturing = new CapturingLoggerProvider();

        using var provider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.AddProvider(capturing);
                builder.AddLogRedaction();
            })
            .BuildServiceProvider();

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        logger.LogError(
            new InvalidOperationException("Login failed for manager@example.com using Password=leaked"),
            "Request failed");

        capturing.Messages.Should().ContainSingle();
        capturing.Messages[0].Should().NotContain("leaked");
        capturing.Messages[0].Should().NotContain("manager@example.com");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
            // Nothing to release in a test double.
        }

        private sealed class CapturingLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add(formatter(state, exception));
        }
    }
}
