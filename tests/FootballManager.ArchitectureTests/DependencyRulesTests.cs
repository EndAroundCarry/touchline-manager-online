using System.Reflection;
using FluentAssertions;
using Xunit;

namespace FootballManager.ArchitectureTests;

public sealed class DependencyRulesTests
{
    private static readonly string[] ProductAssemblies =
    [
        "FootballManager.Domain",
        "FootballManager.Contracts",
        "FootballManager.MatchEngine",
        "FootballManager.Application",
        "FootballManager.Infrastructure",
        "FootballManager.Api",
        "FootballManager.Worker",
    ];

    public static IEnumerable<object[]> ProductAssemblyNames() =>
        ProductAssemblies.Select(name => new object[] { name });

    private static string[] References(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

    private static IEnumerable<Type> Types(string assemblyName) =>
        Assembly.Load(assemblyName).GetTypes();

    [Theory]
    [MemberData(nameof(ProductAssemblyNames))]
    public void Product_code_never_references_test_frameworks(string assemblyName)
    {
        References(assemblyName).Should().NotContain(reference =>
            reference == "xunit" || reference == "xunit.core" || reference == "FluentAssertions");
    }

    [Theory]
    [MemberData(nameof(ProductAssemblyNames))]
    public void Product_types_live_in_their_own_namespace(string assemblyName)
    {
        var prefix = assemblyName + ".";
        Types(assemblyName)
            .Where(type => !string.IsNullOrEmpty(type.Namespace))
            .Where(type => !type.Namespace!.StartsWith(prefix, StringComparison.Ordinal))
            .Should().BeEmpty("every type in {0} must live under namespace {1}", assemblyName, prefix);
    }

    [Fact]
    public void Domain_depends_on_nothing()
    {
        var references = References("FootballManager.Domain");
        references.Should().NotContain(reference => reference.StartsWith("FootballManager.", StringComparison.Ordinal));
        AssertNoInfrastructureStack(references);
    }

    [Fact]
    public void Contracts_depends_on_nothing()
    {
        var references = References("FootballManager.Contracts");
        references.Should().NotContain(reference => reference.StartsWith("FootballManager.", StringComparison.Ordinal));
        AssertNoInfrastructureStack(references);
    }

    [Fact]
    public void MatchEngine_is_pure_except_for_contracts()
    {
        var references = References("FootballManager.MatchEngine");
        references
            .Where(reference => reference.StartsWith("FootballManager.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["FootballManager.Contracts"]);
        AssertNoInfrastructureStack(references);
    }

    [Fact]
    public void Application_depends_on_domain_contracts_and_engine_only()
    {
        var references = References("FootballManager.Application");
        references
            .Where(reference => reference.StartsWith("FootballManager.", StringComparison.Ordinal))
            .Should().BeSubsetOf(
            [
                "FootballManager.Domain",
                "FootballManager.Contracts",
                "FootballManager.MatchEngine",
            ]);
        AssertNoInfrastructureStack(references);
    }

    [Fact]
    public void Infrastructure_never_depends_on_composition_roots()
    {
        var references = References("FootballManager.Infrastructure");
        references.Should().NotContain(reference =>
            reference == "FootballManager.Api" || reference == "FootballManager.Worker");
        references.Should().NotContain(reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_never_depends_on_worker_or_match_engine()
    {
        References("FootballManager.Api").Should().NotContain(reference =>
            reference == "FootballManager.Worker" || reference == "FootballManager.MatchEngine");
    }

    [Fact]
    public void Worker_never_depends_on_api()
    {
        References("FootballManager.Worker").Should().NotContain("FootballManager.Api");
    }

    [Fact]
    public void MatchEngine_source_contains_no_infrastructure_dependencies()
    {
        var repoRoot = FindRepoRoot();
        var engineDirectory = Path.Combine(repoRoot, "src", "FootballManager.MatchEngine");
        Directory.Exists(engineDirectory).Should().BeTrue($"expected {engineDirectory} to exist");

        var sources = Directory.EnumerateFiles(engineDirectory, "*.cs", SearchOption.AllDirectories);
        var bannedTokens = new[]
        {
            "DateTime.Now",
            "DateTime.UtcNow",
            "Random.Shared",
            "new Random(",
            "HttpClient",
            "Npgsql",
            "File.ReadAll",
            "File.WriteAllText",
            "Directory.EnumerateFiles",
        };

        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            foreach (var token in bannedTokens)
            {
                text.Should().NotContain(token, $"{Path.GetFileName(file)} must stay deterministic and infrastructure-free");
            }
        }
    }

    private static void AssertNoInfrastructureStack(string[] references)
    {
        references.Should().NotContain(reference => reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        references.Should().NotContain(reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        references.Should().NotContain(reference => reference.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FootballManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate FootballManager.slnx above {AppContext.BaseDirectory}.");
    }
}
