using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using TouchlineManager.MatchEngine.Randomness;

namespace TouchlineManager.MatchEngine.Tests;

/// <summary>
/// The engine is a pure, deterministic library (DEP-2, ADR-0004).
/// </summary>
/// <remarks>
/// The architecture tests already fail the build when a <em>project</em> boundary is crossed. These are the
/// finer rules a project reference cannot express: the engine must not reach for the clock, the filesystem,
/// the network, the console, or the runtime's own <c>Random</c>. Each of those would make a historical result
/// depend on when or where it was re-derived, which is the one thing the engine may never do.
/// </remarks>
public sealed class EnginePurityTests
{
    [Fact]
    public void The_engine_does_not_reference_hosting_persistence_or_io()
    {
        var referenced = typeof(Randomness.Pcg32).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        referenced.Should().NotContain(
            name => name.StartsWith("System.IO", StringComparison.Ordinal)
                || name.StartsWith("System.Net", StringComparison.Ordinal)
                || name.StartsWith("System.Diagnostics", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal),
            "the engine is a pure library (DEP-2, DEP-7)");
    }

    [Fact]
    public void No_type_in_the_engine_reaches_for_the_runtime_random_or_the_clock()
    {
        var forbidden = new[]
        {
            typeof(Random),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(Stopwatch),
            typeof(TimeProvider),
        };

        var assembly = typeof(Randomness.Pcg32).Assembly;
        var offences = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (forbidden.Contains(field.FieldType))
                {
                    offences.Add($"{type.FullName}.{field.Name} : {field.FieldType.Name}");
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (forbidden.Contains(property.PropertyType))
                {
                    offences.Add($"{type.FullName}.{property.Name} : {property.PropertyType.Name}");
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (forbidden.Contains(method.ReturnType))
                {
                    offences.Add($"{type.FullName}.{method.Name}() : {method.ReturnType.Name}");
                }

                foreach (var parameter in method.GetParameters())
                {
                    if (forbidden.Contains(parameter.ParameterType))
                    {
                        offences.Add($"{type.FullName}.{method.Name}({parameter.ParameterType.Name})");
                    }
                }
            }
        }

        offences.Should().BeEmpty(
            "no engine member may take or return a clock or the runtime's Random (ADR-0004)");
    }

    [Fact]
    public void The_engine_has_exactly_one_source_of_randomness()
    {
        var randomness = typeof(Randomness.Pcg32).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "TouchlineManager.MatchEngine.Randomness")
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        randomness.Should().BeEquivalentTo(["MatchSeed", "Pcg32"]);
    }

    [Fact]
    public void The_pcg32_does_not_inherit_from_the_runtime_random()
    {
        typeof(Random).IsAssignableFrom(typeof(Randomness.Pcg32))
            .Should().BeFalse("sharing the base class would invite sharing its behaviour");
    }

    [Fact]
    public void The_engine_assembly_names_no_platform_specific_dependency()
    {
        // A native or platform-specific dependency would break the cross-platform determinism claim before any
        // formula had a chance to.
        typeof(Randomness.Pcg32).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should().NotContain(name => name.Contains("Native", StringComparison.OrdinalIgnoreCase));
    }
}
