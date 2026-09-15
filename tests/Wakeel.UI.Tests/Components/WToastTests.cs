using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Wakeel.UI.Components;
using Wakeel.UI.Services;

namespace Wakeel.UI.Tests.Components;

public class WToastTests : WakeelTestContext
{
    [Theory]
    [InlineData(WSemanticVariant.Success, "w-toast--success")]
    [InlineData(WSemanticVariant.Danger, "w-toast--danger")]
    [InlineData(WSemanticVariant.Warning, "w-toast--warning")]
    public void WToast_Renders_VariantClass_AndText(WSemanticVariant variant, string expectedClass)
    {
        var cut = Render<WToast>(p => p
            .Add(x => x.Text, "تم الحفظ بنجاح")
            .Add(x => x.Variant, variant));

        Assert.Contains(expectedClass, cut.Find(".w-toast").ClassList);
        Assert.Contains("تم الحفظ بنجاح", cut.Markup);
    }

    [Fact]
    public void WToast_CloseButton_RaisesOnClose()
    {
        var closed = false;
        var cut = Render<WToast>(p => p
            .Add(x => x.Text, "رسالة")
            .Add(x => x.OnClose, () => closed = true));

        cut.Find(".w-toast-close").Click();

        Assert.True(closed);
    }

    [Fact]
    public void WToastHost_RendersToast_QueuedThroughService()
    {
        var toastService = new ToastService();
        Services.AddSingleton<IToastService>(toastService);

        var cut = Render<WToastHost>(p => p.Add(x => x.AutoDismissMs, 0));
        toastService.Show("تم حفظ التغييرات", WSemanticVariant.Success);
        cut.Render();

        Assert.Contains("تم حفظ التغييرات", cut.Markup);
    }

    [Fact]
    public void WToastHost_RemovesToast_WhenDismissed()
    {
        var toastService = new ToastService();
        Services.AddSingleton<IToastService>(toastService);
        toastService.Show("رسالة مؤقتة", WSemanticVariant.Info);

        var cut = Render<WToastHost>(p => p.Add(x => x.AutoDismissMs, 0));
        Assert.Contains("رسالة مؤقتة", cut.Markup);

        var id = toastService.Toasts[0].Id;
        toastService.Dismiss(id);
        cut.Render();

        Assert.DoesNotContain("رسالة مؤقتة", cut.Markup);
    }
}
