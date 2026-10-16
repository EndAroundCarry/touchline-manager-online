using Tactical11Manager.Application.Abstractions;

namespace Tactical11Manager.Infrastructure.Time;

/// <summary>
/// The production clock. Always UTC, never server local time (ADR-0009).
/// </summary>
internal sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
