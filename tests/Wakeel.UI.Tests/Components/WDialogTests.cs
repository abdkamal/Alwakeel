using Bunit;
using Wakeel.UI.Components;

namespace Wakeel.UI.Tests.Components;

/// <summary>WDialog: normal vs danger confirm styling, button order (confirm right/first, cancel left/second per DESIGN-GUIDE.md), and dismissal paths.</summary>
public class WDialogTests : WakeelTestContext
{
    [Fact]
    public void Closed_RendersNothing()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, false)
            .Add(x => x.Title, "عنوان"));

        Assert.Empty(cut.FindAll(".w-dialog"));
    }

    [Fact]
    public void Open_RendersTitleAndDesc()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "حذف نهائيًا؟")
            .Add(x => x.Desc, "هذا الإجراء لا يمكن التراجع عنه."));

        Assert.Contains("حذف نهائيًا؟", cut.Markup);
        Assert.Contains("هذا الإجراء لا يمكن التراجع عنه.", cut.Markup);
    }

    [Fact]
    public void DangerKind_RendersDangerConfirmButton()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Kind, WDialogKind.Danger)
            .Add(x => x.Title, "حذف نهائيًا؟"));

        var confirmButton = cut.Find(".w-dialog-actions button");
        Assert.Contains("w-btn--danger", confirmButton.ClassList);

        // The Danger confirm icon defaults to Lucide "ban" (a circle-with-slash), matching the W92
        // reference dialog, rather than "x-circle".
        Assert.Contains("m4.9 4.9 14.2 14.2", confirmButton.InnerHtml);
    }

    [Fact]
    public void NormalKind_RendersPrimaryConfirmButton()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Kind, WDialogKind.Normal)
            .Add(x => x.Title, "تحديث العرض؟"));

        var confirmButton = cut.Find(".w-dialog-actions button");
        Assert.Contains("w-btn--primary", confirmButton.ClassList);
    }

    [Fact]
    public void ConfirmButton_RendersBeforeCancelButton_SoItSitsOnTheRightUnderRtl()
    {
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "عنوان")
            .Add(x => x.ConfirmLabel, "تأكيد")
            .Add(x => x.CancelLabel, "إلغاء"));

        var buttons = cut.FindAll(".w-dialog-actions button");
        Assert.Equal("تأكيد", buttons[0].TextContent.Trim());
        Assert.Equal("إلغاء", buttons[1].TextContent.Trim());
    }

    [Fact]
    public void ConfirmClick_RaisesOnConfirm()
    {
        var confirmed = false;
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "عنوان")
            .Add(x => x.OnConfirm, () => confirmed = true));

        cut.FindAll(".w-dialog-actions button")[0].Click();

        Assert.True(confirmed);
    }

    [Fact]
    public void CancelClick_RaisesOnCancel()
    {
        var cancelled = false;
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "عنوان")
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.FindAll(".w-dialog-actions button")[1].Click();

        Assert.True(cancelled);
    }

    [Fact]
    public void CloseButton_RaisesOnCancel()
    {
        var cancelled = false;
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "عنوان")
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find(".w-dialog-close").Click();

        Assert.True(cancelled);
    }

    [Fact]
    public void ScrimClick_RaisesOnCancel_WhenCloseOnScrimClickIsTrue()
    {
        var cancelled = false;
        var cut = Render<WDialog>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Title, "عنوان")
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find(".w-dialog-scrim").Click();

        Assert.True(cancelled);
    }
}
