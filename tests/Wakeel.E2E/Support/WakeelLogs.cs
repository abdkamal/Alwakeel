namespace Wakeel.E2E.Support;

/// <summary>Reads the tail of Wakeel.Desktop's current Serilog file (ARCHITECTURE.md's
/// <c>C:\ProgramData\Wakeel\logs\wakeel-&lt;date&gt;.log</c>, falling back to the per-user
/// LocalAppData copy Wakeel.Desktop.Services.WakeelPaths.ResolveLogFolder writes on a machine where
/// ProgramData isn't writable), so a launch-timeout failure message can show what the app itself
/// logged instead of just "it never came up".</summary>
internal static class WakeelLogs
{
    public static string TailOrPlaceholder(int maxLines = 80)
    {
        string[] candidateDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wakeel", "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wakeel", "logs"),
        ];

        foreach (var dir in candidateDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var latest = new DirectoryInfo(dir)
                .GetFiles("wakeel-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is null)
            {
                continue;
            }

            try
            {
                // FileShare.ReadWrite: Serilog still has the file open for writing.
                using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    lines.Add(line);
                }

                return lines.Count == 0
                    ? $"(log file empty: {latest.FullName})"
                    : string.Join(Environment.NewLine, lines.TakeLast(maxLines));
            }
            catch (IOException ex)
            {
                return $"(could not read log file {latest.FullName}: {ex.Message})";
            }
        }

        return "(no Wakeel log file found under ProgramData or LocalAppData)";
    }
}
