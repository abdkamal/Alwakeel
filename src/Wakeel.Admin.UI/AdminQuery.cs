namespace Wakeel.Admin.UI;

/// <summary>
/// The one thing the administration screens ask of an address: the value behind a name in its query
/// string. A05 hands A06 the office it was looking at, and A06 hands A07 the same; nothing else in
/// the tool passes anything through the address bar, so a small reader is cheaper than a dependency.
/// </summary>
public static class AdminQuery
{
    /// <summary>The value of <paramref name="name"/> in the address, or null when it is not there.</summary>
    public static string? Value(string uri, string name)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var query = parsed.Query;
        if (query.Length <= 1)
        {
            return null;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            if (!string.Equals(Uri.UnescapeDataString(pair[..split]), name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(split + 1)..]);
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}
