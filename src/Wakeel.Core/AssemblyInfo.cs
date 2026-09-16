using System.Runtime.CompilerServices;

// Lets Wakeel.Core.Tests reach OfficialNumberService's internal test-only constructor (see
// OfficialNumberService.maxConcurrencyAttemptsOverride), so a test can drive its bounded retry to
// exhaustion deterministically without needing InvalidOperationException-hidden production seams.
[assembly: InternalsVisibleTo("Wakeel.Core.Tests")]
