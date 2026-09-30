using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure.Tests.Time;

/// <summary>
/// The stepped test clock: the stored instant it reads, and the guard that keeps it out of Production
/// (ADR-0049, `TIME-6`).
/// </summary>
public sealed class SteppedClockTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 10, 6, 18, 25, 0, TimeSpan.Zero);

    [Fact]
    public void Production_refuses_to_build_a_stepped_clock()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();

        // Registration is where the refusal happens, so a production host fails at startup rather than
        // serving a world whose clock an operator can move (TIME-6).
        var act = () => services.AddGameClock(configuration, Environment(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*never permitted in Production*");
    }

    [Fact]
    public void A_non_production_environment_replaces_the_real_clock_with_a_stepped_one()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);
        var options = services.AddGameClock(configuration, Environment(Environments.Development));

        options.IsStepped.Should().BeTrue();
        options.IsNonProduction.Should().BeTrue();

        using var provider = services.BuildServiceProvider();

        // The stepped clock is frozen at the configured instant until an operator advances it, and it does not
        // read the database until asked, so a short-lived tool sees the starting instant without a query.
        provider.GetRequiredService<IClock>().UtcNow.Should().Be(InitialNow);
    }

    [Fact]
    public void Real_time_is_the_default_and_is_left_alone()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=clock;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        var options = services.AddGameClock(configuration, Environment(Environments.Production));

        options.IsNonProduction.Should().BeFalse();

        using var provider = services.BuildServiceProvider();

        // Real time is left alone: the resolved clock is the wall clock, not one frozen at the anchor.
        provider.GetRequiredService<IClock>().UtcNow.Should()
            .BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=clock;Username=u;Password=p",
                ["Clock:Mode"] = "Stepped",
                ["Clock:InitialNowUtc"] = InitialNow.ToString("O"),
            })
            .Build();

    private static StubEnvironment Environment(string name) => new(name);

    /// <summary>A host environment with a chosen name.</summary>
    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TouchlineManager.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
