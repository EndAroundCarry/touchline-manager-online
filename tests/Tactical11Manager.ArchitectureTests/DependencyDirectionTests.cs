using System.Reflection;
using FluentAssertions;

namespace Tactical11Manager.ArchitectureTests;

/// <summary>
/// Enforces the dependency rules in <c>docs/architecture/modules.md</c> §2 and ADR-0001.
/// </summary>
/// <remarks>
/// Module boundaries decay unless something fails the build when they are crossed. These tests are
/// the enforcement mechanism, so a violation is a compile-adjacent failure rather than a review
/// comment.
/// </remarks>
public sealed class DependencyDirectionTests
{
    private const string DomainAssembly = "Tactical11Manager.Domain";
    private const string ApplicationAssembly = "Tactical11Manager.Application";
    private const string InfrastructureAssembly = "Tactical11Manager.Infrastructure";
    private const string ContractsAssembly = "Tactical11Manager.Contracts";
    private const string MatchEngineAssembly = "Tactical11Manager.MatchEngine";
    private const string ApiAssembly = "Tactical11Manager.Api";
    private const string WorkerAssembly = "Tactical11Manager.Worker";

    private static readonly string[] PersistenceAndHosting =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.DependencyInjection",
    ];

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_class_library()
    {
        // DEP-1
        ReferencedNames(DomainAssembly)
            .Where(name =>
                name.StartsWith("Tactical11Manager.", StringComparison.Ordinal)
                || PersistenceAndHosting.Any(forbidden => name.StartsWith(forbidden, StringComparison.Ordinal)))
            .Should()
            .BeEmpty("the domain layer is the innermost layer and may not depend on any other layer (DEP-1)");
    }

    [Fact]
    public void MatchEngine_is_pure()
    {
        // DEP-2 and DEP-7
        ReferencedNames(MatchEngineAssembly)
            .Where(name =>
                name.StartsWith("Tactical11Manager.Infrastructure", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Api", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Worker", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Application", StringComparison.Ordinal)
                || PersistenceAndHosting.Any(forbidden => name.StartsWith(forbidden, StringComparison.Ordinal)))
            .Should()
            .BeEmpty(
                "the match engine must stay a pure, deterministic library with no persistence, "
                + "clock, hosting, or application dependency (DEP-2, DEP-7, ADR-0004)");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_composition_roots()
    {
        // DEP-3
        ReferencedNames(ApplicationAssembly)
            .Where(name =>
                name.StartsWith("Tactical11Manager.Infrastructure", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Api", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Worker", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Should()
            .BeEmpty("application defines ports that infrastructure implements, never the reverse (DEP-3)");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_composition_roots()
    {
        // DEP-4 and DEP-8
        ReferencedNames(InfrastructureAssembly)
            .Where(name =>
                name.StartsWith("Tactical11Manager.Api", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Worker", StringComparison.Ordinal))
            .Should()
            .BeEmpty("composition roots depend on infrastructure, never the reverse (DEP-4, DEP-8)");
    }

    [Fact]
    public void Contracts_stays_a_transport_contract_assembly()
    {
        // DEP-6
        ReferencedNames(ContractsAssembly)
            .Where(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.StartsWith("Tactical11Manager.Infrastructure", StringComparison.Ordinal))
            .Should()
            .BeEmpty("contracts hold versioned transport DTOs and error codes, never entities (DEP-6)");
    }

    [Fact]
    public void Composition_roots_do_not_reference_each_other()
    {
        // DEP-5: the API and the worker are independent deployables that share layers, not code.
        ReferencedNames(ApiAssembly)
            .Should()
            .NotContain(name => name.StartsWith("Tactical11Manager.Worker", StringComparison.Ordinal));

        ReferencedNames(WorkerAssembly)
            .Should()
            .NotContain(name => name.StartsWith("Tactical11Manager.Api", StringComparison.Ordinal));
    }

    [Fact]
    public void No_source_project_references_an_application_project()
    {
        // DEP-8
        foreach (var assembly in new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ContractsAssembly, MatchEngineAssembly })
        {
            ReferencedNames(assembly)
                .Where(name => name.StartsWith("Tactical11Manager.Api", StringComparison.Ordinal)
                    || name.StartsWith("Tactical11Manager.Worker", StringComparison.Ordinal))
                .Should()
                .BeEmpty($"'{assembly}' is a library and must not reference a composition root (DEP-8)");
        }
    }

    private static List<string> ReferencedNames(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();
    }
}
