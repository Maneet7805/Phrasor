# Security policy

## Supported versions

Only the [latest release](https://github.com/Maneet7805/Phrasor/releases/latest) gets fixes. To update, download the new `Phrasor.exe`, run it and choose **Yes**.

## Reporting a vulnerability

**Please don't open a public issue for security problems.** Report them privately through [GitHub's private vulnerability reporting](https://github.com/Maneet7805/Phrasor/security/advisories/new).

Include what you found, the steps to reproduce it, and the impact you expect. You should get a reply within a week. Once a fix is released, you'll be credited in the release notes unless you'd rather not be.

## What Phrasor does with your data

This is useful context for judging a report:

- **API keys** are encrypted with Windows DPAPI (`CurrentUser` scope) in `%APPDATA%\Phrasor\secrets.dat`. Only your Windows account can decrypt them.
- **Prompt text** is sent only when you press a shortcut (or Rewrite in the main window's Try it), and only to the provider you configured. It's never written to logs, and it is never written to disk unless you press Save to History.
- **History** holds only what you press Save on, in `%LOCALAPPDATA%\Phrasor\History\history.dat`, encrypted with Windows DPAPI (`CurrentUser` scope). That keeps it from other Windows accounts and from a copied file, not from software running as you. Up to 500 items; each side is cut at 20,000 characters.
- **Logs** (`%APPDATA%\Phrasor\logs`, kept 7 days) record events and timings only.
- **Keystrokes:** Phrasor sends <kbd>Ctrl</kbd>+<kbd>A</kbd>/<kbd>C</kbd>/<kbd>V</kbd> to the focused app. It refuses password fields, never sends keys to terminals, and never presses Enter or submits anything.
- **Quality check results** (when you run one) are saved as plain, unencrypted text under `%APPDATA%\Phrasor\quality`. They can include the test cases' prompts and the rewrites. Delete all data removes them.
- **Network:** Phrasor connects to your configured model and voice providers over HTTPS. The Custom and Ollama providers also accept `http://` addresses, so a local server works. Besides that:
  - **Update check:** once a day Phrasor asks `api.github.com` for the latest release. It sends no prompt text, settings or keys. It is on by default; turn it off with **Check for updates once a day** in Settings. If you choose to update, it downloads the exe from GitHub and checks it against the release's `SHA256SUMS.txt` before installing.
  - **WebView2:** only if the runtime is missing and you press **Install now**, Phrasor downloads Microsoft's installer from `go.microsoft.com`.
  - There's no telemetry and no server of our own.

## Unsigned executable

Release builds aren't code-signed, so Windows SmartScreen warns on first run. Every release lists a SHA-256 checksum in `SHA256SUMS.txt`. Verify your download in PowerShell:

```powershell
Get-FileHash .\Phrasor.exe -Algorithm SHA256
```

Releases are built from the tagged source by GitHub Actions. You can also build it yourself; see the README.
