using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Infrastructure.Time;

namespace TouchlineManager.Infrastructure.Tests.Time;

/// <summary>
/// The compressed test clock: its map from real time to game time, and the guard that keeps it out of
/// Production (ADR-0009, ADR-0015, `TIME-2`, `TIME-6`).
/// </summary>
public sealed class CompressedClockTests
{
    private static readonly DateTimeOffset RealAnchor = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Game_time_scales_from_the_anchor_by_the_rate()
    {
        var clock = Compressed(new ClockOptions { Rate = 60 }, RealAnchor.AddHours(1));

        // One real hour at sixty times is sixty game hours.
        clock.UtcNow.Should().Be(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_unset_virtual_anchor_makes_game_time_equal_real_time_at_the_anchor()
    {
        var clock = Compressed(new ClockOptions { Rate = 100 }, RealAnchor);

        clock.UtcNow.Should().Be(RealAnchor, "with no virtual anchor the two clocks agree at the anchor (TIME-6)");
    }

    [Fact]
    public void The_virtual_anchor_offsets_and_then_scales()
    {
        var gameStart = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.Zero);

        var clock = Compressed(
            new ClockOptions { Rate = 10, VirtualAnchorUtc = gameStart },
            RealAnchor.AddHours(1));

        // Ten real minutes become one game hour, said the other way: one real hour is ten, from gameStart.
        clock.UtcNow.Should().Be(gameStart.AddHours(10));
    }

    [Fact]
    public void A_compressed_clock_always_reports_utc()
    {
        var clock = Compressed(
            new ClockOptions { Rate = 60, VirtualAnchorUtc = new DateTimeOffset(2026, 10, 6, 18, 0, 0, TimeSpan.FromHours(2)) },
            RealAnchor.AddMinutes(5));

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero, "every instant the application stores is UTC (TIME-1)");
    }

    [Fact]
    public void An_extreme_run_is_bounded_rather_than_overflowing()
    {
        var clock = Compressed(new ClockOptions { Rate = 100_000 }, RealAnchor.AddYears(5000));

        var act = () => clock.UtcNow;

        act.Should().NotThrow("a very long run must degrade rather than crash a live process (TIME-6)");
    }

    [Fact]
    public void A_clock_that_is_not_compressed_is_refused()
    {
        var act = () => new CompressedClock(new ClockOptions { Mode = ClockMode.System });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Clock:Mode=Compressed*");
    }

    [Fact]
    public void A_compressed_clock_needs_a_real_anchor()
    {
        var act = () => new CompressedClock(new ClockOptions { Mode = ClockMode.Compressed, Rate = 60 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Clock:RealAnchorUtc*");
    }

    [Fact]
    public void Production_refuses_to_build_a_compressed_clock()
    {
        var configuration = Configuration(compressed: true);
        var services = new ServiceCollection();

        // Registration is where the refusal happens, so a production host fails at startup rather than
        // serving a season whose clock nobody meant to accelerate (TIME-6).
        var act = () => services.AddGameClock(configuration, Environment(Environments.Production));

        act.Should().Throw<InvalidOperationException>().WithMessage("*never permitted in Production*");
    }

    [Fact]
    public void A_non_production_environment_replaces_the_real_clock()
    {
        var configuration = Configuration(compressed: true);
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        var options = services.AddGameClock(configuration, Environment(Environments.Development));

        options.IsCompressed.Should().BeTrue();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IClock>().Should().BeOfType<CompressedClock>();
    }

    [Fact]
    public void Real_time_is_the_default_and_is_left_alone()
    {
        var configuration = Configuration(compressed: false);
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        var options = services.AddGameClock(configuration, Environment(Environments.Production));

        options.IsCompressed.Should().BeFalse();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IClock>().Should().NotBeOfType<CompressedClock>();
    }

    private static CompressedClock Compressed(ClockOptions options, DateTimeOffset realNow)
    {
        options.Mode = ClockMode.Compressed;
        options.RealAnchorUtc ??= RealAnchor;

        return new CompressedClock(options, new FixedTimeProvider(realNow));
    }

    private static IConfiguration Configuration(bool compressed) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=localhost;Database=clock;Username=u;Password=p",
                ["Clock:Mode"] = compressed ? "Compressed" : "System",
                ["Clock:Rate"] = "60",
                ["Clock:RealAnchorUtc"] = RealAnchor.ToString("O"),
            })
            .Build();

    private static StubEnvironment Environment(string name) => new(name);

    /// <summary>Real time the test controls, so the map is asserted without waiting.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    /// <summary>A host environment with a chosen name.</summary>
    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TouchlineManager.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
