namespace TouchlineManager.Application.Abstractions;

/// <summary>
/// The only source of the current time in application and domain code.
/// </summary>
/// <remarks>
/// Server local time is never used or stored (ADR-0009, TIME-1, TIME-2). Tests substitute a
/// fake clock so that deadline behaviour is deterministic and testable at all.
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
