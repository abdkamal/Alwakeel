using System.IO;
using System.Text.Json;
using Wakeel.UI.Services;

namespace Wakeel.Desktop.Services;

/// <summary>
/// File-backed <see cref="IUiStateStore"/> for the desktop host: persists key/value UI preferences
/// (theme choice, sidebar group open/closed state) as one JSON file under
/// <c>%LocalAppData%\Wakeel</c> so they survive an application restart.
/// </summary>
public sealed class FileUiStateStore : IUiStateStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?> _values;

    /// <summary>Creates the store, loading any previously persisted values from <paramref name="filePath"/> (missing or unreadable files start empty rather than throwing).</summary>
    public FileUiStateStore(string filePath)
    {
        _filePath = filePath;
        _values = Load(filePath);
    }

    /// <inheritdoc />
    public string? Get(string key, string? defaultValue = null)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var value) ? value : defaultValue;
        }
    }

    /// <inheritdoc />
    public void Set(string key, string? value)
    {
        lock (_lock)
        {
            _values[key] = value;
            Save();
        }
    }

    /// <inheritdoc />
    public bool GetBool(string key, bool defaultValue = false)
    {
        var raw = Get(key);
        return raw is null ? defaultValue : bool.TryParse(raw, out var parsed) && parsed;
    }

    /// <inheritdoc />
    public void SetBool(string key, bool value) => Set(key, value ? "true" : "false");

    private static Dictionary<string, string?> Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new Dictionary<string, string?>(StringComparer.Ordinal);
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                   ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or inaccessible state file: start fresh rather than block application startup.
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence: a failed write just means preferences won't survive this restart.
        }
    }
}
