using Wakeel.Crypto;
using Wakeel.Design.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// Turns the machine-readable reason a setup file was refused into the one Arabic sentence the
/// person reads (AGREEMENT item 15: no codes, no technical words). <c>Wakeel.Crypto</c> never
/// produces a sentence; it produces an <see cref="ErrorCode"/>, and this is where that stops being
/// a code.
/// </summary>
public static class SetupRefusals
{
    /// <summary>The sentence for a refusal, taking the failing check into account.</summary>
    public static string Describe(SetupCheckItem item, ErrorCode? error) => (item, error) switch
    {
        // The one case whose wording depends on where it failed: a perfectly well made file, for
        // somebody else's organisation, looks exactly like a tampered one to the signature check.
        (SetupCheckItem.Organisation, ErrorCode.Tampered) => Ar.FirstRun.Refusal.OtherOrganisation,
        _ => Describe(error),
    };

    /// <summary>The sentence for a refusal reason on its own.</summary>
    public static string Describe(ErrorCode? error) => error switch
    {
        ErrorCode.BadSignature => Ar.FirstRun.Refusal.BadSignature,
        ErrorCode.Tampered => Ar.FirstRun.Refusal.Tampered,
        ErrorCode.WrongPassword => Ar.FirstRun.Refusal.WrongPassword,
        ErrorCode.Revoked => Ar.FirstRun.Refusal.Revoked,
        ErrorCode.Expired => Ar.FirstRun.Refusal.Expired,
        ErrorCode.UnknownKind => Ar.FirstRun.Refusal.UnknownKind,
        ErrorCode.Corrupt => Ar.FirstRun.Refusal.Corrupt,
        ErrorCode.OtherDevice => Ar.FirstRun.Refusal.OtherDevice,
        ErrorCode.Older => Ar.FirstRun.Refusal.Older,
        ErrorCode.FutureDate => Ar.FirstRun.Refusal.FutureDate,
        _ => Ar.FirstRun.Refusal.Unknown,
    };

    /// <summary>The Arabic name of one line of the check list on W03.</summary>
    public static string LabelOf(SetupCheckItem item) => item switch
    {
        SetupCheckItem.Package => Ar.FirstRun.Check.ItemPackage,
        SetupCheckItem.Signature => Ar.FirstRun.Check.ItemSignature,
        SetupCheckItem.Organisation => Ar.FirstRun.Check.ItemOrganisation,
        SetupCheckItem.Office => Ar.FirstRun.Check.ItemOffice,
        SetupCheckItem.Device => Ar.FirstRun.Check.ItemDevice,
        SetupCheckItem.Employee => Ar.FirstRun.Check.ItemEmployee,
        SetupCheckItem.OfficeKey => Ar.FirstRun.Check.ItemOfficeKey,
        SetupCheckItem.Logo => Ar.FirstRun.Check.ItemLogo,
        SetupCheckItem.Guide => Ar.FirstRun.Check.ItemGuide,
        SetupCheckItem.ReportTemplate => Ar.FirstRun.Check.ItemReportTemplate,
        SetupCheckItem.LetterTemplate => Ar.FirstRun.Check.ItemLetterTemplate,
        _ => Ar.FirstRun.Check.ItemRevocation,
    };

    /// <summary>The Arabic name of one of the three working roles.</summary>
    public static string RoleOf(string? role) => role switch
    {
        SetupRoles.Manager => Ar.FirstRun.Check.RoleManager,
        SetupRoles.Secretary => Ar.FirstRun.Check.RoleSecretary,
        _ => Ar.FirstRun.Check.RoleCustodian,
    };

    /// <summary>
    /// The Arabic name of the role as the database stores it. The setup file calls the first role
    /// «manager»; the database enumeration inherited the name <c>Director</c> for the same thing.
    /// </summary>
    public static string RoleOf(Core.Data.InstallationRole role) => role switch
    {
        Core.Data.InstallationRole.Secretary => Ar.FirstRun.Check.RoleSecretary,
        Core.Data.InstallationRole.Custodian => Ar.FirstRun.Check.RoleCustodian,
        _ => Ar.FirstRun.Check.RoleManager,
    };

    /// <summary>The Arabic description of how much of the office's work this device receives.</summary>
    public static string ScopeOf(string? scope) =>
        scope == SetupSyncScopes.Custody ? Ar.FirstRun.Check.ScopeCustody : Ar.FirstRun.Check.ScopeFull;
}
