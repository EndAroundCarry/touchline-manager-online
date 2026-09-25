using System.Reflection;
using FluentAssertions;
using TouchlineManager.Contracts.Squad;

namespace TouchlineManager.ArchitectureTests;

/// <summary>
/// The C2 guards <c>data-classification.md</c> §2.1 and §7 require: a hidden player value must not be
/// reachable from a manager-facing DTO, and the storage unit of a state measure must not be either.
/// </summary>
/// <remarks>
/// <para>
/// §2.1's rule is that a C2 value has a test that fails when it appears in a manager-facing response, and
/// that relying on a mapper being correct is not sufficient. This is that test at the strongest boundary
/// available without a running database: if a hidden value ever gains a public property on a squad DTO,
/// the build fails here before the mapper is even written.
/// </para>
/// <para>
/// The scan is scoped to the squad namespace rather than the whole contracts assembly, because a club's
/// public reputation is a legitimate <c>Reputation</c> property on a world DTO and is C0. A deliberately
/// scoped rule that holds is worth more than a broad one that has to be relaxed.
/// </para>
/// </remarks>
public sealed class DataClassificationTests
{
    /// <summary>The class C2 player values <c>data-classification.md</c> §1 names as never exposed.</summary>
    private static readonly string[] HiddenPlayerValues = ["Potential", "Reputation"];

    private static IEnumerable<Type> SquadContracts =>
        typeof(SquadErrorCodes).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "TouchlineManager.Contracts.Squad");

    [Fact]
    public void Every_squad_dto_type_is_reachable()
    {
        // A guard on the guard: if the namespace is renamed or the types become internal, the other two
        // assertions would pass vacuously.
        SquadContracts.Should().NotBeEmpty("the squad contracts are where the DTOs live");
    }

    [Fact]
    public void No_squad_dto_exposes_a_hidden_player_value()
    {
        var exposed = SquadContracts
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => HiddenPlayerValues.Contains(property.Name, StringComparer.Ordinal))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        exposed.Should().BeEmpty(
            "hidden potential and reputation are class C2 and never reach a manager-facing response (data-classification.md §2.1)");
    }

    [Fact]
    public void No_squad_dto_exposes_a_basis_point_state_measure()
    {
        // TRN-8: the API converts basis points into user-facing values. A DTO field named for the storage
        // unit means a conversion was skipped somewhere.
        var exposed = SquadContracts
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name.EndsWith("Bp", StringComparison.Ordinal))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        exposed.Should().BeEmpty("basis points are the storage unit, not the transport unit (TRN-8)");
    }
}
