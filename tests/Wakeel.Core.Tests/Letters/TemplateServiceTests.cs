using Wakeel.Core.Data;
using Wakeel.Core.Services;
using Wakeel.Core.Services.Correspondence;
using Wakeel.Reports.Letters;

namespace Wakeel.Core.Tests.Letters;

/// <summary>
/// Which of the three copies of the letter template is in force, and the office's own copy that
/// «فتح قالب المراسلة في Word» edits (AGREEMENT item 57).
/// </summary>
public sealed class TemplateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "wakeel-letter-template-" + Guid.NewGuid().ToString("N"));

    private readonly MemoryDocumentStore _documents = new();
    private readonly FakeSettings _settings = new();

    private TemplateService Service() => new(
        WakeelPaths.ForRoot(_root),
        _settings,
        new LetterTemplateInspector(),
        new BuiltInLetterTemplate(),
        _documents);

    [Fact]
    public async Task A_fresh_installation_writes_letters_on_the_built_in_template()
    {
        var source = await Service().GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.BuiltIn, source.Origin);
        Assert.Equal(TemplateService.FileName, source.FileName);

        // The built-in template is the approved reference file itself, so a letter can be written
        // before any organisation letterhead has arrived.
        Assert.True(new LetterTemplateInspector().Check(source.Content).CanCompose);
    }

    [Fact]
    public async Task The_setup_file_s_template_wins_over_the_built_in_one()
    {
        var organisation = TemplateBuilder.WithLines("ترويسة الهيئة", "@نص المراسلة");
        var id = _documents.Add(organisation);
        _settings.Set(AccountSettingKeys.LetterTemplateDocumentId, id);

        var source = await Service().GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.Setup, source.Origin);
        Assert.Equal(organisation, source.Content);
    }

    [Fact]
    public async Task The_office_s_own_copy_wins_over_the_setup_file_s()
    {
        _settings.Set(AccountSettingKeys.LetterTemplateDocumentId, _documents.Add(TemplateBuilder.WithLines("من ملف الإعداد", "@نص المراسلة")));
        var edited = TemplateBuilder.WithLines("نسخة المكتب", "@نص المراسلة");

        var service = Service();
        await service.SaveLocalCopyAsync(edited, CancellationToken.None);
        var source = await service.GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.LocalCopy, source.Origin);
        Assert.Equal(edited, source.Content);
    }

    [Fact]
    public async Task Removing_the_office_s_copy_puts_the_setup_file_s_back_in_force()
    {
        var organisation = TemplateBuilder.WithLines("من ملف الإعداد", "@نص المراسلة");
        _settings.Set(AccountSettingKeys.LetterTemplateDocumentId, _documents.Add(organisation));

        var service = Service();
        await service.SaveLocalCopyAsync(
            TemplateBuilder.WithLines("نسخة المكتب", "@نص المراسلة"),
            CancellationToken.None);
        await service.RemoveLocalCopyAsync(CancellationToken.None);

        var source = await service.GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.Setup, source.Origin);
        Assert.Equal(organisation, source.Content);
    }

    [Fact]
    public async Task Opening_the_template_in_word_writes_the_office_s_copy_the_first_time()
    {
        var service = Service();

        var path = await service.PrepareLocalCopyAsync(CancellationToken.None);

        Assert.Equal(service.LocalCopyPath, path);
        Assert.True(File.Exists(path));
        Assert.EndsWith(Path.Combine("templates", TemplateService.FileName), path, StringComparison.Ordinal);

        // The warning beside the button says plainly that a new setup file replaces this copy.
        Assert.Contains("ملف إعداد جديد", CoreAr.Letter.LocalCopyWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_it_a_second_time_keeps_what_the_office_edited()
    {
        var service = Service();
        await service.PrepareLocalCopyAsync(CancellationToken.None);
        var edited = TemplateBuilder.WithLines("بعد التعديل", "@نص المراسلة");
        await File.WriteAllBytesAsync(service.LocalCopyPath, edited, CancellationToken.None);

        await service.PrepareLocalCopyAsync(CancellationToken.None);

        Assert.Equal(edited, await File.ReadAllBytesAsync(service.LocalCopyPath, CancellationToken.None));
    }

    [Fact]
    public async Task The_check_on_upload_lists_the_unknown_and_the_missing_marks()
    {
        var uploaded = TemplateBuilder.WithLines("الموضوع / @اسم الموضع", "@نص المراسلة");

        var check = await Service().CheckAsync(uploaded, CancellationToken.None);

        Assert.True(check.IsReadable);
        Assert.Equal("@اسم الموضع", Assert.Single(check.Unknown).Name);
        Assert.Contains(LetterMarks.Subject, check.Missing);
        Assert.True(check.CanCompose);
    }

    [Fact]
    public async Task An_empty_or_oversized_file_is_refused_before_it_is_opened()
    {
        var service = Service();

        Assert.False((await service.CheckAsync([], CancellationToken.None)).IsReadable);
        Assert.False((await service.CheckAsync(
            new byte[TemplateService.MaxBytes + 1],
            CancellationToken.None)).IsReadable);
    }

    [Fact]
    public async Task A_template_carrying_stored_commands_is_refused()
    {
        var service = Service();
        var withMacros = PackageTamper.WithExtraEntry(
            TemplateBuilder.WithLines("ترويسة", "@نص المراسلة"),
            "word/vbaProject.bin",
            "not really a macro project, but named like one");

        var check = await service.CheckAsync(withMacros, CancellationToken.None);

        // Refused with the same sentence a file that cannot be read gets: the person is being told
        // to choose another Word file, and nothing here is theirs to fix (AGREEMENT item 15).
        Assert.False(check.IsReadable);
        Assert.False(check.CanCompose);
        Assert.Contains("اختر ملف Word", CoreAr.Letter.TemplateUnreadable, StringComparison.Ordinal);

        // And it is not written: the office keeps the template it had.
        Assert.False((await service.SaveLocalCopyAsync(withMacros, CancellationToken.None)).IsReadable);
        Assert.False(File.Exists(service.LocalCopyPath));
    }

    [Fact]
    public async Task A_template_that_asks_word_to_fetch_something_when_it_opens_is_refused()
    {
        // An attached template on a share: Word loads it — and whatever it carries — the moment
        // any letter written on this template is opened.
        var attached = PackageTamper.WithExtraEntry(
            TemplateBuilder.WithLines("ترويسة", "@نص المراسلة"),
            "word/_rels/settings.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file://server/share/office.dotm" TargetMode="External"/>
            </Relationships>
            """);

        Assert.False((await Service().CheckAsync(attached, CancellationToken.None)).IsReadable);
    }

    [Fact]
    public async Task A_letterhead_with_a_website_in_it_is_still_accepted()
    {
        // A hyperlink is external too, and is the one external target a letterhead legitimately
        // carries: Word follows it when a person clicks it, never when the letter opens.
        var withLink = PackageTamper.WithExtraEntry(
            TemplateBuilder.WithLines("ترويسة", "@نص المراسلة"),
            "word/_rels/document.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="mailto:info@example.gov" TargetMode="External"/>
            </Relationships>
            """);

        var check = await Service().CheckAsync(withLink, CancellationToken.None);

        Assert.True(check.IsReadable);
        Assert.True(check.CanCompose);
    }

    [Fact]
    public async Task A_copy_word_turned_into_something_else_while_editing_it_is_not_the_template_in_force()
    {
        // The office's own copy is the very file «فتح قالب المراسلة في Word» hands to Word to edit
        // and save over in place, so the screen on upload is not enough: an office that attaches a
        // template on a share while editing would otherwise make that the template every letter is
        // written on, and every recipient's Word would follow it. The file is therefore screened
        // where it is read as well.
        var service = Service();
        await service.PrepareLocalCopyAsync(CancellationToken.None);
        var tampered = PackageTamper.WithExtraEntry(
            await File.ReadAllBytesAsync(service.LocalCopyPath, CancellationToken.None),
            "word/_rels/settings.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file://server/share/office.dotm" TargetMode="External"/>
            </Relationships>
            """);
        await File.WriteAllBytesAsync(service.LocalCopyPath, tampered, CancellationToken.None);

        var source = await service.GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.BuiltIn, source.Origin);
        Assert.NotEqual(tampered, source.Content);
    }

    [Fact]
    public async Task A_setup_file_s_template_that_fetches_something_when_it_opens_is_not_used_either()
    {
        var attached = PackageTamper.WithExtraEntry(
            TemplateBuilder.WithLines("من ملف الإعداد", "@نص المراسلة"),
            "word/_rels/settings.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file://server/share/office.dotm" TargetMode="External"/>
            </Relationships>
            """);
        _settings.Set(AccountSettingKeys.LetterTemplateDocumentId, _documents.Add(attached));

        var source = await Service().GetAsync(CancellationToken.None);

        Assert.Equal(LetterTemplateOrigin.BuiltIn, source.Origin);
    }

    [Fact]
    public async Task A_macro_enabled_document_saved_under_a_docx_name_is_refused()
    {
        var disguised = PackageTamper.Rewrite(
            TemplateBuilder.WithLines("ترويسة", "@نص المراسلة"),
            "[Content_Types].xml",
            text => text.Replace(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                "application/vnd.ms-word.document.macroEnabled.main+xml",
                StringComparison.Ordinal));

        Assert.False((await Service().CheckAsync(disguised, CancellationToken.None)).IsReadable);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary folder left behind is the operating system's to clean up.
        }
    }

    /// <summary>Settings without a database behind them.</summary>
    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = [];

        public void Set(string key, object? value) => _values[key] = value;

        public Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue);

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<int> GetAttentionLateDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int> GetAttentionNearDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(3);

        public Task<int> GetAttentionStaleDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(7);

        public Task<int> GetReportReminderDaysAsync(CancellationToken cancellationToken = default) => Task.FromResult(3);

        public Task<int> GetMeetingReminderMinutesAsync(CancellationToken cancellationToken = default) => Task.FromResult(15);

        public Task<bool> GetPhoneRemindersEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> GetSoundsEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string> GetThemeAsync(CancellationToken cancellationToken = default) => Task.FromResult("system");
    }
}
