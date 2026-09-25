namespace TouchlineManager.Infrastructure.Time;

/// <summary>
/// Which clock the application reads time from (ADR-0009, `TIME-2`, `TIME-6`).
/// </summary>
public enum ClockMode
{
    /// <summary>Real UTC time. The only mode a production environment may use (`TIME-6`).</summary>
    System = 0,

    /// <summary>
    /// Test time, advancing at a configured multiple of real time. Refused in Production (`TIME-6`).
    /// </summary>
    Compressed = 1,
}

/// <summary>
/// Configuration for the clock, bound from the <c>Clock</c> section (ADR-0009, master plan §16 Stage 6).
/// </summary>
/// <remarks>
/// <para>
/// The default is real time. A non-production environment may opt into a <b>compressed</b> clock so a
/// season whose matchdays are days apart plays out in minutes, which is what lets staging and the
/// end-to-end suite observe real deadlines firing without waiting a real week between them.
/// </para>
/// <para>
/// Compression is an affine map from real time to game time, and it is deliberately expressed as an
/// anchor pair plus a rate rather than as a per-processed "started at" value: every process that reads
/// the same configuration — the API, the worker, the seeder — computes the same instant from the same
/// real instant, so the two processes that must agree about a deadline always do (ADR-0009).
/// </para>
/// <para>
/// It is a test tool, not a game feature. It has no HTTP control surface, it is never enabled by
/// default, and <see cref="ClockMode.Compressed"/> is refused outright in Production (`TIME-6`).
/// </para>
/// </remarks>
public sealed class ClockOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Clock";

    /// <summary>
    /// Gets or sets the clock mode. Real time unless a non-production environment opts in.
    /// </summary>
    public ClockMode Mode { get; set; } = ClockMode.System;

    /// <summary>
    /// Gets or sets how many virtual seconds pass per real second when <see cref="Mode"/> is
    /// <see cref="ClockMode.Compressed"/>.
    /// </summary>
    /// <remarks>
    /// A rate of 60 turns a real minute into a game hour; a rate of 1,800 turns one real hour into about
    /// eleven game weeks, which is a season. Ignored in <see cref="ClockMode.System"/>.
    /// </remarks>
    public double Rate { get; set; } = 1;

    /// <summary>
    /// Gets or sets the real instant the map is anchored at. Required when compressed.
    /// </summary>
    /// <remarks>
    /// The other half of the affine map: the real moment that <see cref="VirtualAnchorUtc"/> is measured
    /// from. Setting it to a fixed value rather than to "the moment this process started" is what makes
    /// the API's clock and the worker's clock identical.
    /// </remarks>
    public DateTimeOffset? RealAnchorUtc { get; set; }

    /// <summary>
    /// Gets or sets the game instant <see cref="RealAnchorUtc"/> maps to. Defaults to the real anchor.
    /// </summary>
    /// <remarks>
    /// Leaving it unset gives a pure speed-up — game time equals real time at the anchor and races ahead
    /// of it afterwards. Setting it ahead of the real anchor additionally offsets the world's clock, so a
    /// season can be pinned to a chosen date (typically just before its first kickoff) while real time
    /// carries on from today.
    /// </remarks>
    public DateTimeOffset? VirtualAnchorUtc { get; set; }

    /// <summary>Gets a value indicating whether the compressed clock is in force.</summary>
    public bool IsCompressed => Mode == ClockMode.Compressed;

    /// <summary>Gets the game instant the map is anchored at, defaulting to the real anchor.</summary>
    public DateTimeOffset? EffectiveVirtualAnchorUtc => VirtualAnchorUtc ?? RealAnchorUtc;
}
