using Microsoft.Extensions.Options;
using Tactical11Manager.Application.Abstractions.Calendar;

namespace Tactical11Manager.Infrastructure.Finance;

/// <summary>
/// Tells the calendar whether the weekly settlement will run, from the same option the scheduler reads.
/// </summary>
internal sealed class WeeklyFinanceRunSettings : IWeeklyFinanceRunSettings
{
    private readonly IOptionsMonitor<FinanceOptions> _options;

    /// <summary>Initializes the settings.</summary>
    public WeeklyFinanceRunSettings(IOptionsMonitor<FinanceOptions> options) => _options = options;

    /// <inheritdoc />
    public bool Enabled => _options.CurrentValue.EnableWeeklyRun;
}
