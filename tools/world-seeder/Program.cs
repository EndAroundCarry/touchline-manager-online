using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchlineManager.Application;
using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.World;
using TouchlineManager.Infrastructure;

// The world seeder (master plan §16 Stage 3).
//
// It creates the one production world: six countries, one 18-club tier each, the first season, and a
// funded account per club. It is an operator tool rather than an endpoint, because nothing about it is
// request-shaped: it runs once before launch, from a command line, with the seed recorded in the
// output so the same world can be reproduced or explained later (PYR-14, FIC-7).
//
// It is idempotent. Running it against a world that already exists reports the existing world and
// changes nothing, which is what makes it safe to leave in a deployment script.

var options = ParseArguments(args);

if (options.ShowHelp)
{
    PrintUsage();

    return 0;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,

    // The content root is the binary's own directory rather than the process's working directory, so
    // the tool reads its appsettings.json whether it was started by `dotnet run`, by the published
    // executable, or from a deployment script with a different working directory.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var clock = scope.ServiceProvider.GetRequiredService<IClock>();
var seeder = scope.ServiceProvider.GetRequiredService<SeedWorld>();

Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Seeding from {(options.Seed is null ? "the configured default seed" : $"seed '{options.Seed}'")} "
        + $"at {clock.UtcNow:u}."));

var result = await seeder.ExecuteAsync(
    new SeedWorldRequest(options.Seed, options.FirstMatchday),
    CancellationToken.None);

if (result.Outcome == SeedWorldOutcome.AlreadySeeded)
{
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"World {result.WorldId} already exists; nothing was created (WORLD-1)."));

    return 0;
}

Console.WriteLine(
    string.Create(
        CultureInfo.InvariantCulture,
        $"Created world {result.WorldId} from seed '{result.Seed}': "
        + $"{result.CountriesCreated} countries, {result.ClubsCreated} clubs, "
        + $"{result.PlayersCreated} players, {result.AccountsCreated} club accounts."));

return 0;

// Parses the two things an operator may want to change. Everything else comes from configuration,
// because a world's shape is a product decision rather than a command-line one.
static SeedArguments ParseArguments(string[] arguments)
{
    string? seed = null;
    DateOnly? firstMatchday = null;

    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--help":
            case "-h":
                return new SeedArguments(true, null, null);

            case "--seed":
                seed = ValueFor(arguments, ref index);
                break;

            case "--first-matchday":
                var value = ValueFor(arguments, ref index);

                if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new ArgumentException($"'{value}' is not a date. Use yyyy-MM-dd.");
                }

                firstMatchday = date;
                break;

            default:
                throw new ArgumentException(
                    $"Unknown argument '{arguments[index]}'. Run with --help to see the options.");
        }
    }

    return new SeedArguments(false, seed, firstMatchday);
}

static string ValueFor(string[] arguments, ref int index)
{
    if (index + 1 >= arguments.Length)
    {
        throw new ArgumentException($"'{arguments[index]}' needs a value.");
    }

    index++;

    return arguments[index];
}

static void PrintUsage() => Console.WriteLine(
    """
    Touchline Manager world seeder.

      --seed <value>            The generation seed (PYR-14). Defaults to World:GenerationSeed.
      --first-matchday <date>   yyyy-MM-dd. The calendar rounds forward to the next
                                Tuesday, Thursday, or Sunday (CAL-2, CAL-5). Defaults to
                                World:FirstSeasonStartDate.
      --help                    Shows this text.

    Configuration comes from appsettings.json beside the tool and from environment
    variables, e.g. ConnectionStrings__Database and World__GenerationSeed.

    Running it against an existing world is a no-op that reports that world.
    """);

/// <summary>What the command line asked for.</summary>
internal sealed record SeedArguments(bool ShowHelp, string? Seed, DateOnly? FirstMatchday);
