using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class RuntimeInstallPlanTests
{
    [Fact]
    public void The_download_address_is_https_on_a_microsoft_host() =>
        Assert.True(RuntimeInstallPlan.IsMicrosoftHttps(new Uri(RuntimeInstallPlan.DownloadUrl)));

    [Fact]
    public void The_manual_page_is_https_on_a_microsoft_host() =>
        Assert.True(RuntimeInstallPlan.IsMicrosoftHttps(new Uri(RuntimeInstallPlan.ManualPageUrl)));

    [Theory]
    [InlineData("https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/x/MicrosoftEdgeWebview2Setup.exe", true)]
    [InlineData("https://microsoft.com/x", true)]
    [InlineData("http://go.microsoft.com/fwlink/p/?LinkId=2124703", false)]
    [InlineData("https://evilmicrosoft.com/x", false)]
    [InlineData("https://microsoft.com.evil.com/x", false)]
    [InlineData("https://go.microsoft.com@evil.com/x", false)]
    [InlineData("https://user@go.microsoft.com/x", false)]
    [InlineData("https://go.microsoft.com:8443/x", false)]
    [InlineData("ftp://go.microsoft.com/x", false)]
    [InlineData("file:///C:/setup.exe", false)]
    public void Only_https_on_a_microsoft_host_is_accepted(string url, bool expected) =>
        Assert.Equal(expected, RuntimeInstallPlan.IsMicrosoftHttps(new Uri(url)));

    [Fact]
    public void A_missing_address_is_refused() => Assert.False(RuntimeInstallPlan.IsMicrosoftHttps(null));

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("O=Microsoft Corporation, CN=Microsoft Corporation", true)]
    [InlineData("CN=Microsoft Corporation Fake, O=Microsoft Corporation", false)]
    [InlineData("CN=Microsoft Corporation, O=Someone Else", false)]
    [InlineData("CN=Someone Else, O=Microsoft Corporation", false)]
    [InlineData("CN=Microsoft Windows, O=Microsoft Corporation", false)]
    [InlineData("CN=Evil, OU=Microsoft Corporation", false)]
    [InlineData("not a subject", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_microsoft_is_an_accepted_signer(string? subject, bool expected) =>
        Assert.Equal(expected, RuntimeInstallPlan.IsAcceptedSigner(subject));

    [Fact]
    public void The_install_arguments_are_exactly_silent_and_install() => Assert.Equal("/silent /install", RuntimeInstallPlan.InstallArguments);

    [Theory]
    [InlineData(0, InstallOutcome.Installed)]
    [InlineData(1223, InstallOutcome.Cancelled)]
    [InlineData(740, InstallOutcome.AccessDenied)]
    [InlineData(-2147024891, InstallOutcome.AccessDenied)]
    [InlineData(-2147012889, InstallOutcome.Offline)]
    [InlineData(-2147012867, InstallOutcome.Offline)]
    [InlineData(1, InstallOutcome.Failed)]
    [InlineData(-2147219440, InstallOutcome.Failed)]
    public void Each_exit_code_maps_to_a_message_kind(int code, InstallOutcome expected) =>
        Assert.Equal(expected, RuntimeInstallPlan.FromExitCode(code));

    [Fact]
    public void Messages_hold_no_paths_or_installer_text()
    {
        foreach (var outcome in Enum.GetValues<InstallOutcome>())
        {
            var message = RuntimeInstallPlan.Message(outcome, 42);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain('\\', message);
            Assert.DoesNotContain(':', message);
            Assert.DoesNotContain(".exe", message);
            Assert.DoesNotContain("Users", message);
        }
    }

    [Fact]
    public void Only_a_plain_failure_shows_the_exit_code()
    {
        Assert.Contains("42", RuntimeInstallPlan.Message(InstallOutcome.Failed, 42));
        Assert.DoesNotContain("42", RuntimeInstallPlan.Message(InstallOutcome.Offline, 42));
    }
}
