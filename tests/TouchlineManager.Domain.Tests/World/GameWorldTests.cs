using FluentAssertions;
using TouchlineManager.Domain.Rules;
using TouchlineManager.Domain.World;

namespace TouchlineManager.Domain.Tests.World;

/// <summary>
/// The world's season pointer and the one transition that moves it (`TIME-3`, master plan §7.5).
/// </summary>
public sealed class GameWorldTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_world_starts_at_season_one()
    {
        var world = Create();

        world.CurrentSeasonNumber.Should().Be(1);
        world.Status.Should().Be(GameWorldStatus.Active);
        world.RuleSetVersion.Should().Be(WorldRuleSet.Version);
    }

    [Fact]
    public void Advancing_moves_the_season_pointer_once_and_bumps_the_version()
    {
        var world = Create();
        var version = world.Version;

        world.AdvanceToNextSeason(Now.AddDays(1));

        world.CurrentSeasonNumber.Should().Be(2);
        world.Version.Should().Be(version + 1);
        world.UpdatedAt.Should().Be(Now.AddDays(1));

        world.AdvanceToNextSeason(Now.AddDays(2));

        world.CurrentSeasonNumber.Should().Be(3);
    }

    [Fact]
    public void A_frozen_world_still_closes_its_season()
    {
        // Freezing stops onboarding, not the calendar (§13): the season that is closing still rolls over.
        var world = Create();
        world.Freeze(Now.AddHours(1));

        world.AdvanceToNextSeason(Now.AddDays(1));

        world.CurrentSeasonNumber.Should().Be(2);
    }

    private static GameWorld Create() => GameWorld.Create(Guid.CreateVersion7(), "Test World", Now);
}
