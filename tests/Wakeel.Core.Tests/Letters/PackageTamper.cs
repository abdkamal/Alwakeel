using System.IO.Compression;
using System.Text;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// Turns a perfectly ordinary Word file into one of the files a template must be refused for: one
/// carrying stored commands, one that points at something outside itself, one that is not really
/// a plain document at all.
/// </summary>
internal static class PackageTamper
{
    /// <summary>The same file with one more entry inside it.</summary>
    /// <param name="docx">The file.</param>
    /// <param name="entryName">What to call the new entry.</param>
    /// <param name="content">What it holds.</param>
    public static byte[] WithExtraEntry(byte[] docx, string entryName, string content)
    {
        using var buffer = new MemoryStream();
        buffer.Write(docx);
        buffer.Position = 0;

        using (var package = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var existing = package.GetEntry(entryName);
            existing?.Delete();

            var entry = package.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }

        return buffer.ToArray();
    }

    /// <summary>The same file with one entry's text put through <paramref name="change"/>.</summary>
    /// <param name="docx">The file.</param>
    /// <param name="entryName">The entry to rewrite.</param>
    /// <param name="change">What to do to its text.</param>
    public static byte[] Rewrite(byte[] docx, string entryName, Func<string, string> change)
    {
        string text;
        using (var read = new MemoryStream(docx, writable: false))
        using (var package = new ZipArchive(read, ZipArchiveMode.Read))
        {
            var entry = package.GetEntry(entryName)
                ?? throw new InvalidOperationException($"No entry called {entryName}.");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }

        return WithExtraEntry(docx, entryName, change(text));
    }
}
