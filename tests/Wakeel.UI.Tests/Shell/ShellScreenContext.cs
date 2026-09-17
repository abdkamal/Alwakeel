using Microsoft.Extensions.DependencyInjection;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.UI.Services.Shell;
using Wakeel.UI.Tests.FirstRun;

namespace Wakeel.UI.Tests.Shell;

/// <summary>
/// A bUnit context with a real, activated installation and an OPEN session behind it, so the eight
/// daily screens (W08–W12, W91, W92, W94) render against the same Wakeel.Core services the host
/// gives them rather than against a stub.
/// </summary>
/// <remarks>
/// The screens read <see cref="ShellServices.IsOpen"/> before anything else and draw «الجلسة مغلقة»
/// when it is false, so a test that wants the loaded state must actually open a session; and
/// because <see cref="ShellServices"/> holds one DI scope per open session, the services this
/// context exposes are resolved from that same scope — writing a row through
/// <see cref="Db"/> and reading it back through <see cref="Attention"/> therefore sees one database.
/// </remarks>
public abstract class ShellScreenContext : FirstRunScreenContext
{
    /// <summary>The instant every screen in these tests is rendered at.</summary>
    protected static readonly DateTime Now = new(2026, 9, 16, 10, 24, 0, DateTimeKind.Utc);

    private bool _opened;

    /// <summary>The shell's window onto the open session's services.</summary>
    protected ShellServices Shell => Services.GetRequiredService<ShellServices>();

    /// <summary>The open session's database.</summary>
    protected WakeelDb Db => Session.Db;

    protected IAttentionService Attention => Shell.Attention;

    protected INotificationService Notifications => Shell.Notifications;

    protected IIdGenerator Ids => Shell.Find<IIdGenerator>()!;

    /// <summary>Activates the installation and leaves the session open, as a sign-in does.</summary>
    protected async Task OpenSessionAsync()
    {
        if (_opened)
        {
            return;
        }

        await ActivateAsync();
        SaveProfile();
        _opened = true;

        // ActivationService opens the session itself; if a future change stops doing so, every
        // screen would silently render its closed state and the assertions below would be vacuous.
        Assert.True(Session.IsOpen);
        Assert.True(Shell.IsOpen);
    }

    /// <summary>
    /// Locks the shell and unlocks it again, which is the cycle a screen left open on the desk goes
    /// through: the session closes, its services go with it, and a new scope opens behind the same
    /// database. Every screen of the daily shell is expected to re-read what it shows afterwards.
    /// </summary>
    protected async Task ReopenSessionAsync()
    {
        Locks.LockNow();
        Assert.False(Shell.IsOpen);

        var result = await Locks.UnlockAsync(AccountPassword);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(Shell.IsOpen);
    }

    /// <summary>
    /// Writes rows with the <c>CreatedAt</c>/<c>UpdatedAt</c> they were given instead of letting the
    /// save re-stamp them to "now" — the only way to craft a record that has been untouched for a
    /// fortnight, which is what «راكد» means.
    /// </summary>
    protected void AddWithStamps(params SyncedEntity[] entities)
    {
        using (Db.SuppressAuditStamps())
        {
            foreach (var entity in entities)
            {
                if (entity.CreatedAt == default)
                {
                    entity.CreatedAt = Now.AddDays(-30);
                }

                if (entity.UpdatedAt == default)
                {
                    entity.UpdatedAt = Now;
                }

                Db.Add(entity);
            }

            Db.SaveChanges();
        }
    }

    /// <summary>An open task, optionally overdue, due soon, or left untouched.</summary>
    protected static TaskItem OpenTask(
        string title,
        DateTime? due = null,
        DateTime? updated = null,
        string? assignee = null) =>
        new()
        {
            Title = title,
            DueAt = due,
            UpdatedAt = updated ?? Now,
            CreatedAt = (updated ?? Now).AddDays(-1),
            Status = WorkTaskStatus.Open,
            Priority = TaskPriority.Normal,
            AssigneeName = assignee,
        };

    /// <summary>A meeting that starts later today.</summary>
    protected static Meeting Meeting(string title, DateTime startsAt, string? location = null) =>
        new()
        {
            Title = title,
            StartsAt = startsAt,
            DurationMin = 45,
            Location = location,
            Status = MeetingStatus.Planned,
        };

    /// <summary>A phone expense still waiting to be confirmed or rejected (AGREEMENT item 50).</summary>
    protected PhoneExpense PendingExpense(string purpose, long amount = 4250, string? note = null)
    {
        var device = Db.Devices.FirstOrDefault(d => d.Kind == DeviceKind.Phone);
        if (device is null)
        {
            device = new Device
            {
                DeviceNo = 2,
                EmployeeNo = 2,
                EmployeeName = SharedSetupFile.EmployeeName,
                Role = InstallationRole.Secretary,
                Kind = DeviceKind.Phone,
                IssuedAt = Now.AddYears(-1),
                PairedAt = Now.AddMonths(-1),
                SyncScope = SyncScope.Full,
            };
            Db.Devices.Add(device);
            Db.SaveChanges();
        }

        return new PhoneExpense
        {
            PhoneDeviceId = device.Id,
            Amount = amount,
            Purpose = purpose,
            At = Now.AddDays(-1),
            Note = note,
            Status = PhoneExpenseStatus.Pending,
        };
    }
}
