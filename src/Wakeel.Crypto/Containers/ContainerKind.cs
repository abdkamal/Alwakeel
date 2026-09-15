namespace Wakeel.Crypto;

/// <summary>The seven signed container kinds the product exchanges.</summary>
public enum ContainerKind
{
    /// <summary>Administrator tool → first run of the agent.</summary>
    Setup,

    /// <summary>Agent ↔ agent inside the same office.</summary>
    Sync,

    /// <summary>One approved correspondence addressed to another office.</summary>
    Msg,

    /// <summary>A signed asset handover between custodians.</summary>
    Transfer,

    /// <summary>Signed inventory or payroll results travelling up the structure.</summary>
    Inventory,

    /// <summary>Small packets exchanged with the phone over the cable.</summary>
    Phone,

    /// <summary>A full backup of the database, the vault and the settings.</summary>
    Backup,
}

/// <summary>File extensions and tokens of the container kinds.</summary>
public static class ContainerKinds
{
    public static string Token(ContainerKind kind) => kind switch
    {
        ContainerKind.Setup => "setup",
        ContainerKind.Sync => "sync",
        ContainerKind.Msg => "msg",
        ContainerKind.Transfer => "transfer",
        ContainerKind.Inventory => "inventory",
        ContainerKind.Phone => "phone",
        ContainerKind.Backup => "backup",
        _ => throw new CryptoException(ErrorCode.UnknownKind, "This container kind is not known to this version."),
    };

    public static string Extension(ContainerKind kind) => ".wakeel-" + Token(kind);

    /// <summary>
    /// Whether the administrator must be able to open this kind of file for maintenance.
    /// These are the kinds whose payload is otherwise locked by a password the administrator
    /// never sees, or sealed to a device that is not the administrator's.
    /// </summary>
    public static bool RequiresAdminCopy(ContainerKind kind) => kind switch
    {
        ContainerKind.Backup => true,
        ContainerKind.Msg => true,
        ContainerKind.Transfer => true,
        ContainerKind.Inventory => true,
        _ => false,
    };

    public static ContainerKind FromToken(string? token) => token switch
    {
        "setup" => ContainerKind.Setup,
        "sync" => ContainerKind.Sync,
        "msg" => ContainerKind.Msg,
        "transfer" => ContainerKind.Transfer,
        "inventory" => ContainerKind.Inventory,
        "phone" => ContainerKind.Phone,
        "backup" => ContainerKind.Backup,
        _ => throw new CryptoException(ErrorCode.UnknownKind, "This container kind is not known to this version."),
    };

    /// <summary>Reads the kind out of a file name or a bare extension.</summary>
    public static ContainerKind FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var extension = path.StartsWith('.') && !path.Contains('/', StringComparison.Ordinal) && !path.Contains('\\', StringComparison.Ordinal)
            ? path
            : Path.GetExtension(path);

        if (string.IsNullOrEmpty(extension) || !extension.StartsWith(".wakeel-", StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptoException(ErrorCode.UnknownKind, "This file is not one of the product's own files.");
        }

        return FromToken(extension[".wakeel-".Length..].ToLowerInvariant());
    }

    public static IReadOnlyList<ContainerKind> All { get; } =
    [
        ContainerKind.Setup,
        ContainerKind.Sync,
        ContainerKind.Msg,
        ContainerKind.Transfer,
        ContainerKind.Inventory,
        ContainerKind.Phone,
        ContainerKind.Backup,
    ];
}
