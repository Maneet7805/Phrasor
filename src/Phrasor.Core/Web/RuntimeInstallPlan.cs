using System.Security.Cryptography.X509Certificates;

namespace Phrasor.Core.Web;

/// <summary>What happened when installing the WebView2 runtime; each kind has one plain message.</summary>
public enum InstallOutcome { Installed, Offline, AccessDenied, Cancelled, TimedOut, BadSignature, Failed }

/// <summary>
/// The fixed facts about installing Microsoft's WebView2 runtime from its Evergreen bootstrapper. The app downloads
/// the file only after a click, checks its signature before running it, and never elevates.
/// </summary>
public static class RuntimeInstallPlan
{
    /// <summary>
    /// Microsoft's "Get the Link" address for the Evergreen bootstrapper, as used in Microsoft's published WebView2
    /// deployment samples (MicrosoftEdge/WebView2Samples, WV2DeploymentWiXCustomActionSample). It redirects to a Microsoft download host.
    /// </summary>
    public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>Microsoft's page for people who install it themselves.</summary>
    public const string ManualPageUrl = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";

    public const string InstallerFileName = "MicrosoftEdgeWebview2Setup.exe";

    /// <summary>Microsoft's documented unattended command; not elevated, so Windows installs it for this user only.</summary>
    public const string InstallArguments = "/silent /install";

    public const string AcceptedSigner = "Microsoft Corporation";

    public static readonly TimeSpan InstallTimeLimit = TimeSpan.FromMinutes(3);

    /// <summary>True for an https address on microsoft.com or a subdomain, with no user info and the default port.</summary>
    public static bool IsMicrosoftHttps(Uri? url) =>
        url is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true, UserInfo.Length: 0 }
        && (url.Host == "microsoft.com" || url.Host.EndsWith(".microsoft.com", StringComparison.Ordinal));

    /// <summary>True when the certificate subject names Microsoft Corporation as its common name and organisation.</summary>
    public static bool IsAcceptedSigner(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        try
        {
            string? cn = null, org = null;
            foreach (var rdn in new X500DistinguishedName(subject).EnumerateRelativeDistinguishedNames())
            {
                var type = rdn.GetSingleElementType().Value;
                if (type == "2.5.4.3") cn = rdn.GetSingleElementValue();
                else if (type == "2.5.4.10") org = rdn.GetSingleElementValue();
            }
            return cn == AcceptedSigner && org == AcceptedSigner;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // Windows error codes the bootstrapper is known to return for these cases; any other non-zero code is a plain failure.
    private const int ErrorAccessDenied = unchecked((int)0x80070005), ErrorCancelled = 1223, ErrorElevationRequired = 740;
    private static readonly int[] NetworkErrors =
    [
        unchecked((int)0x80072EE7), // name not resolved
        unchecked((int)0x80072EFD), // cannot connect
        unchecked((int)0x80072EE2), // timed out
        unchecked((int)0x80072EFE), // connection dropped
    ];

    public static InstallOutcome FromExitCode(int exitCode) => exitCode switch
    {
        0 => InstallOutcome.Installed,
        ErrorAccessDenied or ErrorElevationRequired => InstallOutcome.AccessDenied,
        ErrorCancelled => InstallOutcome.Cancelled,
        _ when NetworkErrors.Contains(exitCode) => InstallOutcome.Offline,
        _ => InstallOutcome.Failed,
    };

    /// <summary>The message shown to the user. Never holds installer text or paths; only a plain failure shows its number.</summary>
    public static string Message(InstallOutcome outcome, int exitCode = 0) => outcome switch
    {
        InstallOutcome.Installed => "The web view is installed.",
        InstallOutcome.Offline => "Couldn't reach Microsoft. Check your connection, or use the offline installer from Microsoft's page.",
        InstallOutcome.AccessDenied => "Windows did not allow the install. Your PC may block it; Microsoft's page has other ways to install it.",
        InstallOutcome.Cancelled => "The install was cancelled. Nothing was changed.",
        InstallOutcome.TimedOut => "The install took too long and was stopped. You can try again or use Microsoft's page.",
        InstallOutcome.BadSignature => "The download was not signed by Microsoft, so it was deleted and not run. Use Microsoft's page instead.",
        _ => $"The install did not finish (code {exitCode}). You can try again or use Microsoft's page.",
    };
}
