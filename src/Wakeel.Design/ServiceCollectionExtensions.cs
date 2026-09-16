using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wakeel.Design.Services;

namespace Wakeel.Design;

/// <summary>
/// DI registration for the shared design system's UI services (ARCHITECTURE.md §12 decision
/// 2026-09-16), shared between Wakeel.Desktop (the الوكيل app) and Wakeel.Admin (أداة المدير).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ThemeService"/> and <see cref="IToastService"/> (scoped, one per hosted
    /// BlazorWebView), plus a default in-memory <see cref="IUiStateStore"/>. Every registration uses
    /// <c>TryAdd</c>, so a host that needs durable UI-state persistence (Wakeel.Desktop's
    /// <c>FileUiStateStore</c>) should register its own <see cref="IUiStateStore"/> BEFORE calling
    /// this method; this method then leaves that registration in place instead of overriding it.
    /// </summary>
    public static IServiceCollection AddWakeelDesign(this IServiceCollection services)
    {
        services.TryAddSingleton<IUiStateStore, InMemoryUiStateStore>();
        services.TryAddScoped<ThemeService>();
        services.TryAddScoped<IToastService, ToastService>();
        return services;
    }
}
