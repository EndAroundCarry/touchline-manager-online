using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TouchlineManager.Application.Abstractions.Ops;

namespace TouchlineManager.Infrastructure.Ops;

/// <summary>
/// Reads the read-only incident flag from <c>ops.feature_flags</c> (master plan §13, `F-51`).
/// </summary>
/// <remarks>
/// <para>
/// The flag is read through the same store the operator writes it with (`IFeatureFlagStore`), so there is
/// one row and one contract. It is read on every manager command, so the parsed state is cached briefly: a
/// short absolute expiry keeps a toggle from being a database read per request while still letting a change
/// land within a few seconds on every instance, which is the only cross-process invalidation available
/// because the API and the worker do not share memory.
/// </para>
/// <para>
/// The parse is deliberately lenient. An unset flag, an object without a boolean <c>enabled</c>, or a value
/// that is not JSON all mean "not read-only": only an operator's explicit opt-in stops manager writes.
/// </para>
/// </remarks>
internal sealed class ReadOnlyModeReader : IReadOnlyMode
{
    private const string CacheKey = "incident.read_only";
    private const int MessageMaxLength = 200;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(5);
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = CacheLifetime,
    };

    private readonly IFeatureFlagStore _store;
    private readonly IMemoryCache _cache;

    /// <summary>Initializes the reader.</summary>
    public ReadOnlyModeReader(IFeatureFlagStore store, IMemoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyModeState> GetStateAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out ReadOnlyModeState? cached) && cached is not null)
        {
            return cached;
        }

        var flag = await _store.FindAsync(
            IncidentFlags.WorldScope,
            IncidentFlags.ReadOnly,
            cancellationToken);

        var state = Interpret(flag?.ValueJson);

        _cache.Set(CacheKey, state, CacheOptions);

        return state;
    }

    /// <inheritdoc />
    public void Invalidate() => _cache.Remove(CacheKey);

    /// <summary>Turns a stored value into the state, treating anything unrecognised as "not read-only".</summary>
    private static ReadOnlyModeState Interpret(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return ReadOnlyModeState.Off;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(valueJson);
        }
        catch (JsonException)
        {
            return ReadOnlyModeState.Off;
        }

        using (document)
        {
            var root = document.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.True => new ReadOnlyModeState(true, null),
                JsonValueKind.String when bool.TryParse(root.GetString(), out var enabled) =>
                    new ReadOnlyModeState(enabled, null),
                JsonValueKind.Object when TryReadObject(root, out var state) => state,
                _ => ReadOnlyModeState.Off,
            };
        }
    }

    private static bool TryReadObject(JsonElement root, out ReadOnlyModeState state)
    {
        state = ReadOnlyModeState.Off;

        if (!root.TryGetProperty("enabled", out var enabled)
            || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        if (enabled.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        string? message = null;

        if (root.TryGetProperty("message", out var messageElement)
            && messageElement.ValueKind == JsonValueKind.String)
        {
            var text = messageElement.GetString();

            if (!string.IsNullOrWhiteSpace(text))
            {
                message = text.Length <= MessageMaxLength ? text : text[..MessageMaxLength];
            }
        }

        state = new ReadOnlyModeState(true, message);

        return true;
    }
}
