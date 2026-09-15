using System.Text.Json;
using Wakeel.Core.Data;
using Wakeel.Core.Services;

namespace Wakeel.Core.Tests;

public sealed class AuditServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DbSession _session;
    private readonly TestClock _clock = new() { UtcNow = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc) };
    private readonly IAuditService _service;

    public AuditServiceTests()
    {
        _session = TestHelpers.OpenNewSession(out _root, out _, _clock);
        _service = new AuditService(_session.Db, _clock);
    }

    public void Dispose()
    {
        _session.Dispose();
        TestHelpers.DeleteRootQuietly(_root);
    }

    [Fact]
    public async Task LogAsync_WritesRowToAuditLog_WithArabicSummary_AndClockTimestamp()
    {
        await _service.LogAsync("موظف تجريبي", "unlock_account", "فتح قفل الحساب بعد إدخال كلمة المرور الصحيحة");

        var entry = Assert.Single(_session.Db.AuditLog);
        Assert.Equal("موظف تجريبي", entry.Actor);
        Assert.Equal("unlock_account", entry.Action);
        Assert.Equal("فتح قفل الحساب بعد إدخال كلمة المرور الصحيحة", entry.SummaryAr);
        Assert.Equal(_clock.UtcNow, entry.At);
        Assert.Null(entry.Details);
    }

    [Fact]
    public async Task LogAsync_SerializesDetails_AsJson_WithoutSecrets()
    {
        var entityId = Guid.CreateVersion7();
        await _service.LogAsync(
            "موظف تجريبي",
            "change_setting",
            "تغيير إعداد المهلة",
            entityType: "setting",
            entityId: entityId,
            details: new { key = "attention.late_days", oldValue = 1, newValue = 2 });

        var entry = Assert.Single(_session.Db.AuditLog);
        Assert.Equal("setting", entry.EntityType);
        Assert.Equal(entityId, entry.EntityId);
        Assert.NotNull(entry.Details);

        using var document = JsonDocument.Parse(entry.Details!);
        Assert.Equal("attention.late_days", document.RootElement.GetProperty("key").GetString());

        // ARCHITECTURE.md §12: audit_log details must never contain secrets.
        Assert.DoesNotContain("password", entry.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", entry.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", entry.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogAsync_MultipleCalls_EachWriteASeparateRow()
    {
        await _service.LogAsync("a", "action1", "ملخص أول");
        await _service.LogAsync("b", "action2", "ملخص ثانٍ");

        Assert.Equal(2, _session.Db.AuditLog.Count());
    }
}
