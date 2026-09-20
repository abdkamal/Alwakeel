using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wakeel.Core.Data;
using Wakeel.Core.Services.Documents;

namespace Wakeel.Ocr;

/// <summary>Dependency-injection registration for Wakeel.Ocr (B3-2).</summary>
public static class OcrServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reader, the page binder and the background queue on top of
    /// <c>AddWakeelCore</c>.
    /// </summary>
    /// <remarks>
    /// The reader is a singleton because it holds one Tesseract engine, which costs about a second
    /// to build and must not be built per document; the queue is Scoped because it writes through
    /// the session's database. A machine with no model files still gets both — the reader simply
    /// answers that it is not available, and the documents list shows «قراءة النصوص غير مهيّأة»
    /// instead of pretending the text was read.
    /// </remarks>
    public static IServiceCollection AddWakeelOcr(this IServiceCollection services)
    {
        services.TryAddSingleton<OcrService>(provider => new OcrService(provider.GetService<WakeelPaths>()));
        services.TryAddSingleton<IOcrEngine>(provider => provider.GetRequiredService<OcrService>());

        // The binder that makes a multi-page scan one PDF. Registered here because this is where
        // the imaging library lives; Core asks for it optionally and works without it.
        services.TryAddSingleton<IScanPdfWriter>(_ => new ScanPdfWriter());

        services.TryAddScoped<IOcrQueue, OcrQueue>();
        return services;
    }
}
