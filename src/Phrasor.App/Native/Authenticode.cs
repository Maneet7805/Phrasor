using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Phrasor.Core.Web;

namespace Phrasor.App.Native;

/// <summary>Checks a file's Authenticode signature with Windows (WinVerifyTrust) and that the signer is Microsoft.</summary>
internal static class Authenticode
{
    private const uint UiNone = 2, ChoiceFile = 1, StateVerify = 1, StateClose = 2;
    // No revocation lookup and no network: the check must also work offline, and the file came from Microsoft over https.
    private const uint RevokeNone = 0, ProvRevocationCheckNone = 0x10, ProvCacheOnlyUrlRetrieval = 0x1000;
    private static readonly IntPtr InvalidHandle = new(-1);
    private static Guid _genericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath, hFile, pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WintrustData data);

    /// <summary>
    /// True only when Windows accepts the signature (trusted chain, file unchanged) and the signer is Microsoft.
    /// Pass the open handle you will run the file from, so the file cannot be swapped between this check and the run.
    /// </summary>
    public static bool IsSignedByMicrosoft(string path, FileStream openFile)
    {
        if (!IsTrusted(path, openFile)) return false;
        try
        {
#pragma warning disable SYSLIB0057 // The loader replacement cannot read a certificate out of a signed exe; Windows has already validated the chain above.
            return RuntimeInstallPlan.IsAcceptedSigner(X509Certificate.CreateFromSignedFile(path).Subject);
#pragma warning restore SYSLIB0057
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool IsTrusted(string path, FileStream openFile)
    {
        var pathPtr = Marshal.StringToCoTaskMemUni(path);
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WintrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                pcwszFilePath = pathPtr,
                hFile = openFile.SafeFileHandle.DangerousGetHandle(),
            }, filePtr, false);
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeNone,
                dwUnionChoice = ChoiceFile,
                pFile = filePtr,
                dwStateAction = StateVerify,
                dwProvFlags = ProvRevocationCheckNone | ProvCacheOnlyUrlRetrieval,
            };
            var result = WinVerifyTrust(InvalidHandle, ref _genericVerifyV2, ref data);
            data.dwStateAction = StateClose;
            WinVerifyTrust(InvalidHandle, ref _genericVerifyV2, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(filePtr);
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }
}
