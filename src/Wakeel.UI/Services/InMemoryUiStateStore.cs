using System.Collections.Concurrent;

namespace Wakeel.UI.Services;

/// <summary>
/// Process-memory implementation of <see cref="IUiStateStore"/>. Used by the component gallery
/// and by tests; the desktop host registers a file-based implementation instead so preferences
/// survive across runs (see Wakeel.Desktop.Services.FileUiStateStore).
/// </summary>
public sealed class InMemoryUiStateStore : IUiStateStore
{
    private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Get(string key, string? defaultValue = null)
        => _values.TryGetValue(key, out var value) ? value : defaultValue;

    /// <inheritdoc />
    public void Set(string key, string? value) => _values[key] = value;

    /// <inheritdoc />
    public bool GetBool(string key, bool defaultValue = false)
    {
        var raw = Get(key);
        return raw is null ? defaultValue : bool.TryParse(raw, out var parsed) && parsed;
    }

    /// <inheritdoc />
    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");
}
