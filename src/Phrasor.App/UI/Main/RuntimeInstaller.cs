using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Phrasor.App.Native;
using Phrasor.Core.Web;

namespace Phrasor.App.UI.Main;

/// <param name="NotRun">Debug simulation only: the file was downloaded and verified but deliberately not run.</param>
internal sealed record InstallResult(InstallOutcome Outcome, int ExitCode = 0, bool NotRun = false)
{
    public string Message => RuntimeInstallPlan.Message(Outcome, ExitCode);
}

/// <summary>
/// Downloads Microsoft's WebView2 bootstrapper and runs it, after the user clicked "Install now". Order matters:
/// download (https, Microsoft hosts only, every redirect checked) -> check the signature -> run not elevated -> delete.
/// A file whose signature is not Microsoft's is deleted and never run. Logs event names, exit codes and timings only.
/// </summary>
internal static class RuntimeInstaller
{
    private const int MaxBytes = 20 * 1024 * 1024, MaxRedirects = 5, DownloadTimeoutSeconds = 60, DeleteTries = 5, DeleteRetryMs = 200;
    private const string FolderPrefix = "Phrasor-webview2-";

    public static async Task<InstallResult> RunAsync(bool dryRun, CancellationToken cancel)
    {
        var clock = Stopwatch.StartNew();
        Log.Info("webview2 install start");
        var folder = Path.Combine(Path.GetTempPath(), FolderPrefix + Guid.NewGuid().ToString("N"));
        InstallResult result;
        try
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, RuntimeInstallPlan.InstallerFileName);
            result = await DownloadAsync(file, cancel) ?? await VerifyAndRunAsync(file, folder, dryRun, cancel);
        }
        catch (OperationCanceledException)
        {
            result = new InstallResult(InstallOutcome.Cancelled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"webview2 install file error: {ex.GetType().Name}");
            result = new InstallResult(InstallOutcome.Failed, ex.HResult);
        }
        finally
        {
            await DeleteFolderAsync(folder);
        }
        Log.Info($"webview2 install result kind={result.Outcome} exit={result.ExitCode} notRun={result.NotRun} ms={clock.ElapsedMilliseconds}");
        return result;
    }

    /// <summary>Saves the bootstrapper to <paramref name="file"/>; returns null on success, or the failure.</summary>
    private static async Task<InstallResult?> DownloadAsync(string file, CancellationToken cancel)
    {
        var clock = Stopwatch.StartNew();
        // Redirects are followed by hand so that every hop, not only the last, is checked.
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds) };
        try
        {
            var url = new Uri(RuntimeInstallPlan.DownloadUrl);
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!RuntimeInstallPlan.IsMicrosoftHttps(url))
                {
                    Log.Warn("webview2 download refused: address is not https on a Microsoft host");
                    return new InstallResult(InstallOutcome.Failed);
                }
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectKeepVerb or HttpStatusCode.PermanentRedirect
                    && response.Headers.Location is { } next)
                {
                    url = next.IsAbsoluteUri ? next : new Uri(url, next);
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
                {
                    Log.Warn($"webview2 download failed status={(int)response.StatusCode}");
                    return new InstallResult(InstallOutcome.Failed, (int)response.StatusCode);
                }
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                // HttpClient.Timeout covers only the headers here, so the body gets its own limit; the catch below maps it to Offline.
                using var bodyLimit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                bodyLimit.CancelAfter(TimeSpan.FromSeconds(DownloadTimeoutSeconds));
                if (!await CopyCappedAsync(source, target, bodyLimit.Token))
                {
                    Log.Warn("webview2 download too large");
                    return new InstallResult(InstallOutcome.Failed);
                }
                Log.Info($"webview2 download ok ms={clock.ElapsedMilliseconds}");
                return null;
            }
            Log.Warn("webview2 download refused: too many redirects");
            return new InstallResult(InstallOutcome.Failed);
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"webview2 download failed: {ex.HttpRequestError}");
            return new InstallResult(ex.StatusCode is null ? InstallOutcome.Offline : InstallOutcome.Failed);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            Log.Warn("webview2 download timed out");
            return new InstallResult(InstallOutcome.Offline);
        }
    }

    private static async Task<bool> CopyCappedAsync(Stream source, Stream target, CancellationToken cancel)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            total += read;
            if (total > MaxBytes) return false;
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
        }
        return true;
    }

    private static async Task<InstallResult> VerifyAndRunAsync(string file, string folder, bool dryRun, CancellationToken cancel)
    {
        // Held open (read-only, no write or delete by others) from the check until the installer has started,
        // so the checked file is the file that runs.
        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Authenticode.IsSignedByMicrosoft(file, locked))
        {
            Log.Warn("webview2 signature bad; file deleted, not run");
            return new InstallResult(InstallOutcome.BadSignature);
        }
        Log.Info("webview2 signature ok");
        if (dryRun) return new InstallResult(InstallOutcome.Installed, NotRun: true);

        Process? process;
        try
        {
            // Never elevated: Windows installs it for this user only. If it insists on elevation, that is reported, not forced.
            process = Process.Start(new ProcessStartInfo(file, RuntimeInstallPlan.InstallArguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = folder,
            });
        }
        catch (Win32Exception ex)
        {
            Log.Warn($"webview2 installer did not start code={ex.NativeErrorCode}");
            return new InstallResult(RuntimeInstallPlan.FromExitCode(ex.NativeErrorCode), ex.NativeErrorCode);
        }
        if (process is null) return new InstallResult(InstallOutcome.Failed);

        using (process)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(RuntimeInstallPlan.InstallTimeLimit);
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                return new InstallResult(cancel.IsCancellationRequested ? InstallOutcome.Cancelled : InstallOutcome.TimedOut);
            }
            return new InstallResult(RuntimeInstallPlan.FromExitCode(process.ExitCode), process.ExitCode);
        }
    }

    // The installer can hold its file for a moment after it exits.
    private static async Task DeleteFolderAsync(string folder)
    {
        for (var attempt = 0; attempt < DeleteTries; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(DeleteRetryMs);
            }
        }
        Log.Warn("webview2 installer folder not deleted");
    }
}
