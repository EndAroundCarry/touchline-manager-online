using System.Reflection;
using FluentAssertions;
using Xunit;

namespace FootballManager.Domain.Tests;

public class DomainAssemblyTests
{
    [Fact]
    public void Domain_assembly_loads_with_stable_name()
    {
        var assembly = Assembly.Load("FootballManager.Domain");
        assembly.GetName().Name.Should().Be("FootballManager.Domain");
    }
}
