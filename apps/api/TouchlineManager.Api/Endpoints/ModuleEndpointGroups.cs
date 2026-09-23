namespace TouchlineManager.Api.Endpoints;

/// <summary>
/// The single declaration of the module route prefixes.
/// </summary>
/// <remarks>
/// <para>
/// Every manager-facing endpoint in the product lives under <c>/api/v1/{module}</c>, where
/// <c>module</c> is one of the bounded modules from <c>docs/architecture/modules.md</c> §1. Keeping
/// the prefixes here means a module cannot quietly invent its own address space.
/// </para>
/// <para>
/// Groups are created at startup so that a module attaches its endpoints to an already-named
/// group instead of re-declaring a prefix. A group with no endpoints is inert, which is correct:
/// feature-incomplete modules are unreachable until their stage lands.
/// </para>
/// </remarks>
internal static class ModuleEndpointGroups
{
    /// <summary>The versioned API prefix (master plan §10).</summary>
    public const string VersionPrefix = "/api/v1";

    /// <summary>Module names, in the order they appear in the module map.</summary>
    public static readonly string[] ModuleNames =
    [
        "auth",
        "world",
        "squad",
        "competition",
        "match",
        "market",
        "finance",
        "comms",
        "ops",
    ];

    /// <summary>
    /// Creates one route group per bounded module under the versioned prefix.
    /// </summary>
    /// <returns>The groups keyed by module name.</returns>
    public static IReadOnlyDictionary<string, RouteGroupBuilder> MapModuleGroups(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var groups = new Dictionary<string, RouteGroupBuilder>(StringComparer.Ordinal);

        foreach (var module in ModuleNames)
        {
            groups[module] = endpoints.MapGroup($"{VersionPrefix}/{module}").WithTags(module);
        }

        return groups;
    }
}
