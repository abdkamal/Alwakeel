using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// What the letter tests share: where the owner's reference files are, a letter worth composing,
/// and a vault that lives in memory.
/// </summary>
internal static class LetterWorld
{
    /// <summary>The repository root, found by walking up from the test assembly.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The folder holding the owner's template and his filled example.</summary>
    public static string TemplateFolder { get; } =
        Path.Combine(RepoRoot, "docs", "templates", "correspondence");

    /// <summary>The approved reference template.</summary>
    public static string ReferenceTemplatePath { get; } =
        Path.Combine(TemplateFolder, "نموذج-المراسلة.docx");

    /// <summary>The owner's filled example, which the conformance test compares against.</summary>
    public static string OwnerExamplePath { get; } =
        Path.Combine(TemplateFolder, "مثال-مراسلة-المالك.docx");

    /// <summary>The reference template's bytes.</summary>
    public static byte[] ReferenceTemplate() => File.ReadAllBytes(ReferenceTemplatePath);

    /// <summary>A letter with every mark's value different, so a swap would be visible.</summary>
    public static LetterData Sample(LetterPageSize size = LetterPageSize.A4) => new()
    {
        Date = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Unspecified),
        NumberAr = "و/م/1448/214",
        RecipientHeadNameAr = "عبدالله بن محمد",
        RecipientOfficeNameAr = "مكتب الشؤون الإدارية",
        SubjectAr = "ترشيح موظف لدورة تدريبية",
        SenderNameAr = "سعد بن ناصر",
        SenderOfficeNameAr = "مكتب المدير التنفيذي",
        PageSize = size,
        Body = LetterBody.FromPlainText("نأمل الموافقة على ترشيح الموظف المذكور أعلاه."),
    };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "docs", "templates", "correspondence")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be found from the test assembly.");
    }
}

/// <summary>A vault that keeps documents in memory, for tests that have no installation.</summary>
internal sealed class MemoryDocumentStore : ILetterDocumentStore
{
    private readonly Dictionary<Guid, byte[]> _documents = [];

    /// <summary>What was saved, newest last.</summary>
    public List<(Guid Id, string FileName, string MediaType)> Saved { get; } = [];

    /// <summary>Puts a document in without going through <see cref="SaveAsync"/>.</summary>
    /// <param name="content">The bytes.</param>
    public Guid Add(byte[] content)
    {
        var id = Guid.NewGuid();
        _documents[id] = content;
        return id;
    }

    /// <inheritdoc />
    public Task<byte[]?> ReadAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(documentId, out var content) ? content : null);

    /// <inheritdoc />
    public Task<Guid> SaveAsync(
        byte[] content,
        string fileName,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        _documents[id] = content;
        Saved.Add((id, fileName, mediaType));
        return Task.FromResult(id);
    }

    /// <summary>Reads back what was stored, for a test that wants to look inside.</summary>
    /// <param name="documentId">The document.</param>
    public byte[] Content(Guid documentId) => _documents[documentId];
}
