using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Wakeel.Crypto;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IPlatformProtector"/>: the machine wrap of ARCHITECTURE.md §3,
/// bound to this computer by the operating system's own data protection with the local machine
/// scope, plus the fixed application entropy the caller supplies.
/// </summary>
/// <remarks>
/// <para>
/// Machine scope rather than user scope on purpose: the wrap exists so that a session which has
/// already been opened with the account password can be re-opened quickly after the automatic lock
/// (W06), and the installation belongs to the computer in the office, not to whichever Windows
/// profile happens to be signed in. It never opens anything on its own — <c>LockService</c> only
/// consults it once the password has already opened its own wrap in the same session — so a
/// machine-scoped blob on its own is worth nothing to somebody who carries the disk away.
/// </para>
/// <para>
/// It calls <c>crypt32.dll</c> directly rather than through a NuGet package, because the shipped
/// package list is fixed (ARCHITECTURE.md §1) and these are two entry points.
/// </para>
/// </remarks>
public sealed class DpapiPlatformProtector : IPlatformProtector
{
    /// <summary>Recorded next to every machine wrap this protector writes.</summary>
    public string Name => "windows-dpapi-machine";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> data, ReadOnlySpan<byte> entropy) =>
        Transform(data, entropy, protect: true);

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, ReadOnlySpan<byte> entropy) =>
        Transform(protectedData, entropy, protect: false);

    private static byte[] Transform(ReadOnlySpan<byte> input, ReadOnlySpan<byte> entropy, bool protect)
    {
        var inputBytes = input.ToArray();
        var entropyBytes = entropy.ToArray();

        var inputHandle = GCHandle.Alloc(inputBytes, GCHandleType.Pinned);
        var entropyHandle = GCHandle.Alloc(entropyBytes, GCHandleType.Pinned);
        var output = default(DataBlob);

        try
        {
            var inputBlob = new DataBlob
            {
                DataLength = inputBytes.Length,
                DataPointer = inputHandle.AddrOfPinnedObject(),
            };
            var entropyBlob = new DataBlob
            {
                DataLength = entropyBytes.Length,
                DataPointer = entropyBytes.Length == 0 ? IntPtr.Zero : entropyHandle.AddrOfPinnedObject(),
            };

            var succeeded = protect
                ? CryptProtectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, LocalMachineFlag | UiForbiddenFlag, out output)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, UiForbiddenFlag, out output);

            if (!succeeded || output.DataPointer == IntPtr.Zero)
            {
                // Every caller of a machine wrap treats this one code as "this machine cannot open
                // it" and falls back to the password. The operating system's own numeric reason
                // belongs in neither the message nor the screen (AGREEMENT item 15).
                throw new CryptoException(
                    protect ? ErrorCode.Corrupt : ErrorCode.Tampered,
                    protect
                        ? "This machine could not seal the key."
                        : "This machine could not open the sealed key.");
            }

            var result = new byte[output.DataLength];
            Marshal.Copy(output.DataPointer, result, 0, output.DataLength);
            return result;
        }
        finally
        {
            if (output.DataPointer != IntPtr.Zero)
            {
                // Wipe the operating system's own copy before handing the page back: it holds the
                // key in the clear for as long as it stays allocated.
                ZeroNative(output.DataPointer, output.DataLength);
                LocalFree(output.DataPointer);
            }

            CryptographicOperations.ZeroMemory(inputBytes);
            inputHandle.Free();
            entropyHandle.Free();
        }
    }

    private static void ZeroNative(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }

    private const int LocalMachineFlag = 0x4;
    private const int UiForbiddenFlag = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        IntPtr description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
