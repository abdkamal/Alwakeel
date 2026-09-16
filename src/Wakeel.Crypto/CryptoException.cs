namespace Wakeel.Crypto;

/// <summary>
/// Machine readable reason a cryptographic operation failed.
/// User facing text is produced by the presentation layer, never here.
/// </summary>
public enum ErrorCode
{
    /// <summary>A digital signature did not match the signed bytes or the signer key.</summary>
    BadSignature,

    /// <summary>Authenticated data failed its integrity check.</summary>
    Tampered,

    /// <summary>A password, recovery code or protector secret did not open a wrap.</summary>
    WrongPassword,

    /// <summary>The device certificate appears on a signed revocation list.</summary>
    Revoked,

    /// <summary>A certificate, container or pairing session is outside its validity window.</summary>
    Expired,

    /// <summary>The container kind or format token is not one this build understands.</summary>
    UnknownKind,

    /// <summary>The structure could not be parsed at all.</summary>
    Corrupt,

    /// <summary>A setup file was prepared for a different device than the one installed here.</summary>
    OtherDevice,

    /// <summary>A setup file carries an export sequence older than the one already installed.</summary>
    Older,

    /// <summary>A setup file is dated further into the future than the clock allows.</summary>
    FutureDate,
}

/// <summary>
/// The only exception type this project throws for expected cryptographic failures.
/// </summary>
public sealed class CryptoException : Exception
{
    public CryptoException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public CryptoException(ErrorCode code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
