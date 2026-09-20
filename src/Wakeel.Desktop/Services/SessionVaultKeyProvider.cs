using Wakeel.Core.Services.Documents;
using Wakeel.UI.Services.Account;

namespace Wakeel.Desktop.Services;

/// <summary>
/// Hands Core's vault the key of the open session (<see cref="IVaultKeyProvider"/>).
/// </summary>
/// <remarks>
/// Core cannot own the vault key: it is unwrapped from the installation key file when the person
/// signs in, and wiped the moment the session locks (AGREEMENT item 7). This asks
/// <see cref="AccountSession"/> for it at the moment of use and keeps no copy of its own, so
/// locking the session really does take the key out of reach of everything that reads documents.
/// A read attempted while signed out throws from the session itself, which is correct: it is a
/// defect in the caller, not a state to draw.
/// </remarks>
/// <param name="session">The account session that holds the key while it is open.</param>
public sealed class SessionVaultKeyProvider(AccountSession session) : IVaultKeyProvider
{
    private readonly AccountSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public ReadOnlySpan<byte> VaultKey => _session.VaultKey;
}
