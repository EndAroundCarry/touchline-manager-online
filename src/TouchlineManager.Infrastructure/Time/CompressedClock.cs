using TouchlineManager.Application.Abstractions;

namespace TouchlineManager.Infrastructure.Time;

/// <summary>
/// A clock whose game time advances at a configured multiple of real time (ADR-0009, `TIME-6`).
/// </summary>
/// <remarks>
/// <para>
/// Game time is an affine function of real time: <c>virtual = VirtualAnchor + (real - RealAnchor) × Rate</c>.
/// The anchors are configuration rather than process state, so the API and the worker compute the same
/// instant from the same real instant and can never disagree about whether a deadline has passed — which
/// is the whole reason this exists rather than each process counting its own elapsed ticks.
/// </para>
/// <para>
/// It is a test tool for non-production environments only. It is never registered by default, and the
/// composition roots refuse to build a host that asks for it in Production (`TIME-6`), so it cannot
/// travel to a live world by a configuration accident.
/// </para>
/// <para>
/// One consequence is deliberate and documented rather than a defect: a browser computes its countdowns
/// from its own clock, so a compressed world's screens show a countdown that does not correspond to the
/// server's deadline. The server's own "is this locked" answer remains authoritative, and the client is
/// refused after the lock regardless (ADR-0015).
/// </para>
/// </remarks>
public sealed class CompressedClock : IClock
{
    /// <summary>
    /// The furthest game time may run from the anchor, so an over-long run cannot overflow the
    /// arithmetic or leave <see cref="DateTimeOffset"/>'s range. A thousand years is far beyond any test
    /// and keeps the result representable for any plausible anchor.
    /// </summary>
    private static readonly long MaxScaledTicks = TimeSpan.FromDays(365_000).Ticks;

    private readonly DateTimeOffset _realAnchor;
    private readonly DateTimeOffset _virtualAnchor;
    private readonly double _rate;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the clock from validated configuration.</summary>
    /// <param name="options">The clock configuration; it must select the compressed mode.</param>
    /// <param name="timeProvider">
    /// The source of real time. Defaults to <see cref="TimeProvider.System"/>; tests supply their own so
    /// the mapping is asserted without waiting.
    /// </param>
    public CompressedClock(ClockOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsCompressed)
        {
            throw new InvalidOperationException(
                "CompressedClock needs Clock:Mode=Compressed (ADR-0009, TIME-6).");
        }

        var realAnchor = options.RealAnchorUtc
            ?? throw new InvalidOperationException(
                "Clock:RealAnchorUtc must be set for a compressed clock (ADR-0009, TIME-6).");

        _realAnchor = realAnchor.ToUniversalTime();
        _virtualAnchor = (options.EffectiveVirtualAnchorUtc ?? realAnchor).ToUniversalTime();
        _rate = options.Rate;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            var elapsedTicks = (_timeProvider.GetUtcNow() - _realAnchor).Ticks;
            var scaledTicks = Math.Clamp(elapsedTicks * _rate, -MaxScaledTicks, MaxScaledTicks);

            return _virtualAnchor.AddTicks((long)scaledTicks);
        }
    }
}
