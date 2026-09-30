using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions.Auth;
using TouchlineManager.Application.Abstractions.Ops;
using TouchlineManager.Application.Abstractions.Persistence;
using TouchlineManager.Application.Auth;
using TouchlineManager.Domain.Auth;
using TouchlineManager.Infrastructure;

// The access-administration tool (master plan §10.8, ADR-0042).
//
// It grants and revokes the operator roles the game is administered with. It is an operator tool rather
// than an endpoint, because the two things it does are prerequisites for the console: a production path
// that grants `admin` would itself be a privilege-escalation surface, and the first admin cannot be
// granted by an admin. Every change is audited as a service action, so the trail starts at the CLI.
//
// The web console later adds a roles command for further grants; this tool remains the bootstrap and the
// break-glass path.

AccessArguments parsed;

try
{
    parsed = ParseArguments(args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);

    return 1;
}

if (parsed.ShowHelp)
{
    PrintUsage();

    return parsed.Command is null ? 1 : 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();

var user = await users.FindByNormalizedEmailAsync(User.NormalizeEmail(parsed.Email), CancellationToken.None);

if (user is null)
{
    Console.Error.WriteLine($"No account exists for '{parsed.Email}'.");

    return 1;
}

switch (parsed.Command)
{
    case "list":
        PrintUser(user);

        return 0;

    case "grant":
        {
            var grant = scope.ServiceProvider.GetRequiredService<GrantRole>();
            var result = await grant.ExecuteAsync(user.Id, parsed.Role, parsed.Reason, CancellationToken.None);

            return Report(result, "granted to", user);
        }

    case "revoke":
        {
            var revoke = scope.ServiceProvider.GetRequiredService<RevokeRole>();
            var result = await revoke.ExecuteAsync(user.Id, parsed.Role, parsed.Reason, CancellationToken.None);

            return Report(result, "revoked from", user);
        }

    case "reset-mfa":
        {
            var credentials = scope.ServiceProvider.GetRequiredService<IMfaCredentialRepository>();
            var credential = await credentials.FindByUserIdAsync(user.Id, CancellationToken.None);

            if (credential is null)
            {
                Console.WriteLine($"{user.Email} has no authenticator to reset.");

                return 0;
            }

            credentials.Remove(credential);

            scope.ServiceProvider.GetRequiredService<IAuditWriter>().Record(new AuditEntry(
                AdminAuditActions.MfaDisabled,
                AuditActorTypes.Service,
                ActorUserId: null,
                AuditTargetTypes.MfaCredential,
                user.Id,
                Guid.CreateVersion7().ToString(),
                IpHash: null,
                Reason: parsed.Reason));

            await scope.ServiceProvider
                .GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);

            Console.WriteLine(
                $"Authenticator reset for {user.Email}. The account must enrol again before using admin routes.");

            return 0;
        }

    default:
        Console.Error.WriteLine($"Unknown command '{parsed.Command}'. Run with --help to see the options.");

        return 1;
}

static int Report(RoleChangeResult result, string verb, User user)
{
    switch (result.Outcome)
    {
        case RoleChangeOutcome.Applied:
            Console.WriteLine($"Role '{result.Role}' {verb} {user.Email} ({user.Id}).");

            return 0;

        case RoleChangeOutcome.Unchanged:
            Console.WriteLine($"Nothing to do: '{result.Role}' is already in the requested state for {user.Email}.");

            return 0;

        case RoleChangeOutcome.BaseRoleNotRevocable:
            Console.Error.WriteLine("The base 'player' role cannot be revoked; every account is a manager.");

            return 1;

        case RoleChangeOutcome.InvalidRole:
            Console.Error.WriteLine(
                $"'{result.Role}' is not a known role. Known roles: {string.Join(", ", UserRoles.All)}.");

            return 1;

        default:
            Console.Error.WriteLine($"No account exists for {user.Email}.");

            return 1;
    }
}

static void PrintUser(User user) => Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"{user.Email} ({user.Id}) · status={user.Status.ToCode()} · "
        + $"verified={user.EmailVerifiedAt.HasValue.ToString(CultureInfo.InvariantCulture)} · "
        + $"roles=[{string.Join(", ", user.RoleNames())}]"));

static AccessArguments ParseArguments(string[] arguments)
{
    if (arguments.Length == 0 || arguments.Contains("--help") || arguments.Contains("-h"))
    {
        return new AccessArguments(null, string.Empty, string.Empty, "operator access tool", ShowHelp: true);
    }

    var command = arguments[0];
    var positionals = new List<string>();
    var reason = "operator access tool";

    for (var index = 1; index < arguments.Length; index++)
    {
        if (arguments[index] is "--reason")
        {
            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException("--reason needs a value.");
            }

            reason = arguments[++index];
        }
        else
        {
            positionals.Add(arguments[index]);
        }
    }

    var email = positionals.Count > 0 ? positionals[0] : string.Empty;
    var role = positionals.Count > 1 ? positionals[1] : string.Empty;

    if (string.IsNullOrWhiteSpace(email))
    {
        throw new ArgumentException($"'{command}' needs an email address. Run with --help to see the options.");
    }

    if (command is "grant" or "revoke" && string.IsNullOrWhiteSpace(role))
    {
        throw new ArgumentException($"'{command}' needs a role. Run with --help to see the options.");
    }

    return new AccessArguments(command, email, role, reason, ShowHelp: false);
}

static void PrintUsage() => Console.WriteLine(
    """
    Touchline Manager access administration.

      grant  <email> <role> [--reason <text>]   Grants operator, support, or admin to an account.
      revoke <email> <role> [--reason <text>]   Revokes a role (the base player role cannot be revoked).
      reset-mfa <email> [--reason <text>]       Removes an account's authenticator so it can enrol again.
      list   <email>                            Shows an account's status and roles.
      --help                                    Shows this text.

    Configuration comes from appsettings.json beside the tool and from environment
    variables, e.g. ConnectionStrings__Database.

    Every grant and revoke is recorded in ops.audit_log as a service action with the reason.
    """);

/// <summary>What the command line asked for.</summary>
internal sealed record AccessArguments(
    string? Command,
    string Email,
    string Role,
    string Reason,
    bool ShowHelp);
