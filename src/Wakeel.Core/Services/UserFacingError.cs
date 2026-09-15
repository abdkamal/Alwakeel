namespace Wakeel.Core.Services;

/// <summary>
/// An error translated for the user: a plain-Arabic title, explanation and suggested action,
/// with no technical terms or error codes (AGREEMENT item 15; ARCHITECTURE.md §10, §12). A
/// short opaque reference id may be shown separately (e.g. under "copy details for support"),
/// but never inside the Arabic text itself.
/// </summary>
public sealed record UserFacingError(string TitleAr, string MessageAr, string ActionAr, string Reference);
