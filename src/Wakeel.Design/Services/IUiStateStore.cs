namespace Wakeel.Design.Services;

/// <summary>
/// Small persisted key/value store for per-user UI state (theme choice, sidebar group
/// open/closed state, and similar preferences that are not part of the application data model).
/// Implementations must be safe to call from Blazor render code (synchronous reads) and should
/// persist writes durably enough to survive an application restart.
/// </summary>
public interface IUiStateStore
{
    /// <summary>Reads a previously stored string value, or <paramref name="defaultValue"/> when absent.</summary>
    /// <param name="key">Stable identifier for the value (e.g. "theme", "sidebar.group.gW2Ih").</param>
    /// <param name="defaultValue">Value returned when the key has never been set.</param>
    string? Get(string key, string? defaultValue = null);

    /// <summary>Stores a string value under <paramref name="key"/>, overwriting any previous value.</summary>
    void Set(string key, string? value);

    /// <summary>Reads a previously stored boolean value, or <paramref name="defaultValue"/> when absent.</summary>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>Stores a boolean value under <paramref name="key"/>.</summary>
    void SetBool(string key, bool value);
}
