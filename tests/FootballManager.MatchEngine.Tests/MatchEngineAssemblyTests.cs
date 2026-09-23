using System.Reflection;
using FluentAssertions;
using Xunit;

namespace FootballManager.MatchEngine.Tests;

public class MatchEngineAssemblyTests
{
    [Fact]
    public void MatchEngine_assembly_loads_with_stable_name()
    {
        var assembly = Assembly.Load("FootballManager.MatchEngine");
        assembly.GetName().Name.Should().Be("FootballManager.MatchEngine");
    }
}
