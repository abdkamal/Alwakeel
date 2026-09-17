using System.IO.Compression;
using System.Xml.Linq;
using Wakeel.Core.Data;

namespace Wakeel.Core.Services.Correspondence;

/// <inheritdoc cref="ITemplateService"/>
/// <remarks>
/// <para>
/// Three copies of the letter template can exist, and they are consulted in this order
/// (AGREEMENT item 57):
/// </para>
/// <list type="number">
///   <item>the office's own <c>templates\letter-template.docx</c>, written the first time
///     «فتح قالب المراسلة في Word» is pressed and edited by the office afterwards;</item>
///   <item>the organisation's template as the setup file delivered it, kept in the vault and
///     pointed at by <see cref="AccountSettingKeys.LetterTemplateDocumentId"/>;</item>
///   <item>the template built into الوكيل, which is the reference template of
///     <c>docs/templates/correspondence</c> — so a brand-new installation can write a letter
///     before any organisation letterhead has arrived.</item>
/// </list>
/// <para>
/// The local copy deliberately wins over the setup file's: it is what the office edited, and the
/// warning beside the button (<see cref="CoreAr.Letter.LocalCopyWarning"/>) says plainly that a new
/// setup file replaces it. The vault is reached through <see cref="ILetterDocumentStore"/>,
/// which may be absent — an installation whose vault is unavailable still writes letters, on the
/// built-in template.
/// </para>
/// </remarks>
public sealed class TemplateService : ITemplateService
{
    /// <summary>What the template file is called wherever it is stored.</summary>
    public const string FileName = "letter-template.docx";

    /// <summary>The folder inside the installation root that holds the office's own copy.</summary>
    public const string FolderName = "templates";

    /// <summary>The largest template file that will be opened.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private readonly ISettingsService _settings;
    private readonly ILetterTemplateInspector _inspector;
    private readonly IBuiltInLetterTemplate _builtIn;
    private readonly ILetterDocumentStore? _documents;

    /// <summary>Creates the service.</summary>
    /// <param name="paths">The installation layout; the local copy lives under its root.</param>
    /// <param name="settings">Where the setup file recorded its template's document id.</param>
    /// <param name="inspector">Reads a template's marks.</param>
    /// <param name="builtIn">The template shipped with الوكيل.</param>
    /// <param name="documents">The vault, when one is available.</param>
    public TemplateService(
        WakeelPaths paths,
        ISettingsService settings,
        ILetterTemplateInspector inspector,
        IBuiltInLetterTemplate builtIn,
        ILetterDocumentStore? documents = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _builtIn = builtIn ?? throw new ArgumentNullException(nameof(builtIn));
        _documents = documents;
        LocalCopyPath = Path.Combine(paths.Root, FolderName, FileName);
    }

    /// <inheritdoc />
    public string LocalCopyPath { get; }

    /// <inheritdoc />
    public async Task<LetterTemplateSource> GetAsync(CancellationToken cancellationToken = default)
    {
        var local = ReadLocalCopy();
        if (local is not null)
        {
            return new LetterTemplateSource(local, LetterTemplateOrigin.LocalCopy, FileName);
        }

        var setup = await ReadSetupCopyAsync(cancellationToken).ConfigureAwait(false);
        if (setup is not null)
        {
            return new LetterTemplateSource(setup, LetterTemplateOrigin.Setup, FileName);
        }

        return new LetterTemplateSource(_builtIn.Read(), LetterTemplateOrigin.BuiltIn, FileName);
    }

    /// <inheritdoc />
    public Task<LetterTemplateCheck> CheckAsync(byte[] templateDocx, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (templateDocx is null || templateDocx.Length == 0 || templateDocx.Length > MaxBytes)
        {
            return Task.FromResult(LetterTemplateCheck.Unreadable);
        }

        if (!IsPlainLetterPackage(templateDocx))
        {
            return Task.FromResult(LetterTemplateCheck.Unreadable);
        }

        return Task.FromResult(_inspector.Check(templateDocx));
    }

    /// <inheritdoc />
    public async Task<string> PrepareLocalCopyAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(LocalCopyPath))
        {
            return LocalCopyPath;
        }

        var source = await GetAsync(cancellationToken).ConfigureAwait(false);
        await WriteLocalCopyAsync(source.Content, cancellationToken).ConfigureAwait(false);
        return LocalCopyPath;
    }

    /// <inheritdoc />
    public async Task<LetterTemplateCheck> SaveLocalCopyAsync(
        byte[] templateDocx,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateDocx);

        var check = await CheckAsync(templateDocx, cancellationToken).ConfigureAwait(false);
        if (!check.IsReadable)
        {
            // Nothing is written: the office keeps the template it had, and the screen has the
            // check to explain why.
            return check;
        }

        await WriteLocalCopyAsync(templateDocx, cancellationToken).ConfigureAwait(false);
        return check;
    }

    /// <inheritdoc />
    public Task RemoveLocalCopyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(LocalCopyPath))
            {
                File.Delete(LocalCopyPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The office keeps the copy it has; the next attempt will try again.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether the file is a plain Word letter and nothing more. A letter template is copied
    /// whole into every letter the office writes, and the letter is then opened in Word by the
    /// sender and by whoever receives it, so three things are refused here before a template is
    /// ever accepted (the B3 security check):
    /// <list type="bullet">
    ///   <item>a main part that is not an ordinary Word document — a macro-enabled document saved
    ///     under a <c>.docx</c> name declares itself in <c>[Content_Types].xml</c>;</item>
    ///   <item>a stored <c>vbaProject.bin</c>, which is where Word keeps commands that run by
    ///     themselves when the file opens;</item>
    ///   <item>a relationship that points outside the file — an attached template on a share, a
    ///     linked object, a remote reference — which Word would follow when the letter opens.</item>
    /// </list>
    /// A file this refuses is reported with the same sentence as one that cannot be read at all
    /// (<see cref="CoreAr.Letter.TemplateUnreadable"/>): the person is being told to choose a
    /// different Word file, and nothing here is theirs to fix.
    /// </summary>
    /// <param name="content">The candidate template.</param>
    internal static bool IsPlainLetterPackage(byte[] content)
    {
        try
        {
            using var buffer = new MemoryStream(content, writable: false);
            using var package = new ZipArchive(buffer, ZipArchiveMode.Read);

            foreach (var entry in package.Entries)
            {
                var name = entry.FullName.Replace('\\', '/').TrimStart('/');

                if (name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("vbaData.xml", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("_rels/", StringComparison.OrdinalIgnoreCase)
                    && HasFollowedExternalTarget(entry))
                {
                    return false;
                }

                if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                    && !DeclaresAnOrdinaryDocument(entry))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                              or IOException
                                              or System.Xml.XmlException
                                              or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether any relationship of this part points outside the file at something Word fetches by
    /// itself when the document opens — an attached template, a linked picture, an embedded object,
    /// a sub-document, a frame. A plain hyperlink is the one external target that is left alone:
    /// it is followed only when a person clicks it, and an address or a website in the letterhead
    /// is written as one.
    /// </summary>
    /// <param name="entry">A <c>.rels</c> entry.</param>
    private static bool HasFollowedExternalTarget(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var relationships = XDocument.Load(stream);
        return relationships.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Relationship", StringComparison.Ordinal))
            .Where(element => string.Equals(
                (string?)element.Attribute("TargetMode"),
                "External",
                StringComparison.OrdinalIgnoreCase))
            .Any(element =>
            {
                var type = (string?)element.Attribute("Type") ?? string.Empty;
                return !type.EndsWith("/hyperlink", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>
    /// Whether the package's main part is an ordinary Word document rather than a macro-enabled
    /// one or a template.
    /// </summary>
    /// <param name="entry">The <c>[Content_Types].xml</c> entry.</param>
    private static bool DeclaresAnOrdinaryDocument(ZipArchiveEntry entry)
    {
        const string Ordinary =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

        using var stream = entry.Open();
        var types = XDocument.Load(stream);

        // A package declares its parts either one by one ("Override") or by extension ("Default"),
        // and either way the main document says what kind of document this is.
        var mainParts = types.Descendants()
            .Where(element => element.Name.LocalName is "Override" or "Default")
            .Select(element => (string?)element.Attribute("ContentType"))
            .Where(type => type is not null && type.EndsWith(".main+xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return mainParts.Count > 0
            && mainParts.All(type => string.Equals(type, Ordinary, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Writes the file beside itself and then moves it into place, so a template is never half
    /// written — Word may be opening this very file while a new setup package is being applied.
    /// </summary>
    private async Task WriteLocalCopyAsync(byte[] content, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(LocalCopyPath)!;
        Directory.CreateDirectory(folder);
        var temporary = LocalCopyPath + ".new";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, LocalCopyPath, overwrite: true);
    }

    private byte[]? ReadLocalCopy()
    {
        try
        {
            if (!File.Exists(LocalCopyPath))
            {
                return null;
            }

            var content = File.ReadAllBytes(LocalCopyPath);
            if (content.Length is 0 or > MaxBytes)
            {
                return null;
            }

            // The screen on upload is not enough: this very file is what Word was handed to edit
            // in place, so an office that attached a template on a share or linked a picture while
            // editing would otherwise turn that into the template in force. A file that no longer
            // passes the screen is simply not the template any more, and the next source — the
            // setup file's copy, else the built-in one — is used instead.
            return IsPlainLetterPackage(content) ? content : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<byte[]?> ReadSetupCopyAsync(CancellationToken cancellationToken)
    {
        if (_documents is null)
        {
            return null;
        }

        var documentId = await _settings
            .GetAsync<Guid?>(AccountSettingKeys.LetterTemplateDocumentId, null, cancellationToken)
            .ConfigureAwait(false);
        if (documentId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        var content = await _documents.ReadAsync(id, cancellationToken).ConfigureAwait(false);
        if (content is not ({ Length: > 0 } and { Length: <= MaxBytes }))
        {
            return null;
        }

        // Screened on the way out as well as on the way in, for the same reason as the local copy:
        // a setup file delivered before this screen existed must not become the template in force.
        return IsPlainLetterPackage(content) ? content : null;
    }
}
