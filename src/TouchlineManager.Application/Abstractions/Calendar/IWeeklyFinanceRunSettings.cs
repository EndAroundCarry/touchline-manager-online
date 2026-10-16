namespace TouchlineManager.Application.Abstractions.Calendar;

/// <summary>
/// Whether the weekly finance run is switched on, so the calendar does not promise a settlement that will
/// not happen.
/// </summary>
public interface IWeeklyFinanceRunSettings
{
    /// <summary>Gets whether the worker runs the weekly settlement.</summary>
    bool Enabled { get; }
}
