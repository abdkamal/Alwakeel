using System.IO;
using System.Text.Json;
using Wakeel.Design.Services;

namespace Wakeel.Admin.Services;

/// <summary>
/// File-backed <see cref="IUiStateStore"/> for the administration host: keeps the handful of UI
/// preferences the tool remembers (today, the light/dark choice) in one JSON file under
/// <c>%LocalAppData%\WakeelAdmin</c>, so they survive a restart.
/// </summary>
/// <remarks>
/// Nothing about the organisation goes in here — it is a preferences file, plainly readable, and
/// everything that matters lives in the encrypted database instead.
/// </remarks>
public sealed class AdminFileUiStateStore : IUiStateStore
{
    private readonly string _filePath;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?> _values;

    /// <summary>Creates the store, loading whatever a previous run left behind.</summary>
    public AdminFileUiStateStore(string filePath)
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

            return JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(filePath))
                   ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable preferences file starts fresh rather than stopping the tool.
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a failed write only means the preference does not survive this restart.
        }
    }
}
