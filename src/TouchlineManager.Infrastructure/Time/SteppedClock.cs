using TouchlineManager.Application.Abstractions;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Infrastructure.Time;

/// <summary>
/// A clock frozen at a stored instant that only moves when an operator advances it (ADR-0049, `TIME-6`).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the compressed clock, this one does not follow real time at all: the value is whatever
/// <c>ops.game_clock</c> holds, so a world stays exactly where it was left until the next step. That is
/// what makes a season reproducible press by press — the same week with the same clock produces the same
/// deadlines every time.
/// </para>
/// <para>
/// It is a test tool for non-production environments only. It is never registered by default, and the
/// composition roots refuse to build a host that asks for it in Production (`TIME-6`).
/// </para>
/// </remarks>
internal sealed class SteppedClock : IClock
{
    private readonly IGameClockStore _store;

    /// <summary>Initializes the clock from the store.</summary>
    /// <param name="store">The persisted instant the clock reads.</param>
    public SteppedClock(IGameClockStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _store.Current;
}
