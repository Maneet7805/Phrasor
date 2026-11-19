# Changelog

All notable changes to Phrasor are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

- Rewriting rules: a thin draft gets one line for what the answer should cover; a draft that already says it stays near its wording; no invented facts, names, pages, summaries or checks (#219). Not yet measured.

## [2.0.0] - 2026-11-17

**Read this first.**
- **New name: Phrasor (was PromptForge).** Earlier installed copies don't offer this update. Download `Phrasor.exe` once by hand and choose **Yes**. On first start it copies your settings and re-saves your API keys from the old `PromptForge` data folder (the old folder is left untouched). An old PromptForge install is not removed: uninstall it in Settings → Apps.
- **New main window** (Home, Try it, History, Quality check, Settings, first run). Open it from the tray. Nothing is saved to History unless you press Save.
- **The window needs the Microsoft Edge WebView2 runtime.** It is part of Windows 11 and of most Windows 10 PCs. If it is missing, the window says so and offers **Install now** (after your click it downloads Microsoft's installer, checks that Microsoft signed it, and runs it without admin rights). The shortcuts, the review window and voice dictation never depend on it.
- **Size:** the exe is about 83 MB (1.6.0 was 76.1 MB).
- **Rewriting quality: no clear change.** The rewriting rules only had the old app name replaced. The quality check ran before (2026-11-16) and after (2026-11-17), and the provider stopped both runs early on its daily limit. Runs that finished passed 258/326 (79%) before and 205/249 (82%) after. Of 107 cases compared, 6 were better, 3 worse, 21 noise, and 17 could not be compared because of rate-limit errors. We don't claim the new prompt is better.
- The exe is still not code-signed, so SmartScreen may warn on a manual download.
- **The WebView2 "Install now" button was not tested on a PC that lacks the runtime** (no such PC was available). Only code checks cover it. A clean-PC test is planned for 2.0.1.

### Added
- **Quality check page.** The main window's Quality check page replaces the tray's Developer menu: choose a test set, set the judge model, pick runs per case, and see what the run will cost in requests and minutes (with a warning if it is more than a day of the free allowance) before you press **Start**. It never starts on its own. It shows progress, **Cancel** (a cancelled check saves nothing) and **Open results folder**, and asks before you close the window mid-run. Results go to `%APPDATA%\Phrasor\quality\`; logs hold only counts. The judge setting moved here from the classic Settings window (#212).
- **First run in the main window.** When there is no usable API key, Phrasor now opens its main window on three steps instead of the classic Settings window and a balloon: get a free Groq or Gemini key (the links open in your browser), paste it and test it (it is saved only after a passing test, and you can **Skip for now**), then see your shortcuts. Your shortcuts and the tray keep working throughout. If the browser engine can't start, the classic Settings window opens as before (#211).
- **Save and History.** The Try-it result and the review window have a **Save to History** button. Nothing is saved unless you press it (never automatically). The new History page lists saved rewrites by day, with search, Copy, Delete, **Delete all** (with a confirmation) and a choice to remove items after 30 or 90 days (off by default). Home shows your three latest saved items. History is stored only on this PC, in Local app data, encrypted for your Windows account; it keeps up to 500 items and each side is cut at 20,000 characters. Old items are removed when you open History or save to it, not at startup (#208).
- Settings in the main window now has a **Data** group: what Phrasor stores and what leaves your PC, **Open data folder**, **Open logs folder**, and **Delete all data** with a confirmation in the page. Delete all removes your settings, API keys, logs, History and the start-with-Windows entry, then closes Phrasor (#208).
- Settings in the main window now also has **Shortcuts** (click a shortcut and press the keys; your shortcuts are released only while you record, and come back on Save, Discard, leaving the page or closing the window; a shortcut another app already holds is refused with a plain message), **Voice** (on or off, provider, model, language, stop after, optimise what I dictate) and **Requirements** (your saved text per kind of task, up to 2,000 characters each). Only **Data** stays in the classic Settings window (#205).
- Settings in the main window now has **General** (theme, default mode, review window, start with Windows, app hint, fix dropped details, update check, About me) and **Model** (main and fallback model, API keys, **Test connection**), with explicit **Save** and **Discard**. A saved key is never shown again, not even to the window itself: it only says Saved or Not set. Shortcuts, voice, requirements and data stay in the classic Settings window for now. A wrong model name now says so plainly in Try-it too (#199).
- Home in the main window now shows real status (running or paused, your model, whether an API key is saved, and your shortcuts, with a Pause button that does what the tray's "Pause shortcuts" does). The **Try it** box now rewrites a draft the same way the shortcut does and shows the result in the window with a Copy button. It never pastes into another app, never saves anything, and errors are shown as plain messages (#191).
- The main window now has its real shell: a side rail (Home, History, Quality check, Settings), page frames, a Home banner and a "Try it" box, in dark and light. These are preview pages with no data yet, and the Theme choice on Settings works. Fonts (Fraunces, Inter) and the banner image ship inside Phrasor and load from there only; licences are in THIRD-PARTY.md. The exe grows by about 0.39 MB (83,608,178 to 84,001,906 bytes measured) (#189).
- If the Microsoft Edge WebView2 runtime is missing (a small number of Windows 10 PCs), the main window now says so plainly, with **Install now**, **Open Microsoft's page**, **Use classic settings** and **Not now**. **Install now** is the only thing that downloads anything: after your click it fetches Microsoft's small installer from Microsoft, checks that it is signed by Microsoft (and deletes it, unrun, if not), runs it without admin rights, and opens the window. Cancel, offline and refusal each get a plain message. Shortcuts, the review window and dictation never depend on it (#187).
- The main window now holds a locked-down web view (the shared Microsoft Edge WebView2 runtime) showing a placeholder "hello" page. It loads only files inside Phrasor and blocks every other network request; if the runtime is missing, the window says so. The exe grows by about 0.4 MB (83.17 to 83.58 MB measured) (#183).
- The tray menu has "Open Phrasor", which opens an empty main window with its own title bar (a first step; the pages come later). Starting Phrasor a second time now brings that window to the front instead of opening Settings (#181).
- Eval: the "fix my login bug" case now checks that the rewrite stays under 5x the draft, and expects a spelled-out rewrite instead of questions (#165).
- The review window warns when a rewrite contains numbers your draft never had (for example "in 50 words"), so you can check them. It's a warning only; the rewrite is unchanged. Because of this, instant mode now opens the review window for those rewrites instead of replacing your text (#79).

### Fixed
- The main window's title bar no longer leaves stale or half-drawn caption buttons behind after you resize the window (#207).
- Saving Settings in the main window now writes the file before the running app uses the new settings, and a failed save says so plainly and changes nothing (#203).
- Upgrading from a PromptForge build now keeps your settings and API keys: on first start Phrasor copies settings from the old `PromptForge` data folder and re-saves your keys. The old folder is left untouched, and a profile that already has Phrasor settings is not changed (#201).
- A request the provider calls too large is now retried on the same model with a smaller reply budget, instead of sending the fallback the same oversized request (#101).
- Voice: recordings with only typing, clicks, breathing or room noise are no longer sent for transcription, so they stop turning into words like "Thank you."; a small speech model now checks that someone actually spoke (#66). Very quiet or whispered words may be treated as no speech.

### Changed
- The README and the website now describe the main window (Home, Try it, History, Quality check, Settings) with real screenshots, and the website uses the app's warm black, cream and orange colours. They also correct the exe size (about 83 MB), the number of test drafts (128) and where the judge is set (the Quality check page) (#214).
- The tray menu's **Developer** submenu is gone: the quality check now lives on the main window's Quality check page. The classic Settings window no longer has a quality check button or judge settings, so on a PC without the browser engine the quality check is available only as `Phrasor.exe --eval` (#212).
- The README, SECURITY.md, the website, the uninstall prompts and the classic Settings window now say what is true: a rewrite is saved only if you press Save to History, it is stored encrypted for your Windows account in Local app data, and Delete all data and uninstall can remove it (#208).
- The review, settings, update and status windows now use Phrasor's warm dark or light colours, following Windows (or the `Ui.Theme` setting). Layout and shortcuts are unchanged; combo boxes, buttons and tab headers only partly follow the theme (#179).
- The app has a new name: Phrasor. The program, install folder, Start menu entry and tray name changed. Earlier installed copies don't offer this update: install once by hand; on first start your settings and API keys are carried over (#171, #201).
- Rewrites now keep the parts of a draft that report what happened (what worked, what failed), not only the final request (#164).
- Setup shows a small "Installing Phrasor" window with a moving bar while it copies files and restarts the app (#146).
- After "Update now" in the update card, setup no longer asks a second time (#141). This takes effect from the update after 1.6.0, because 1.6.0's updater doesn't pass the new flag.

## [1.6.0] - 2026-11-10

### Added
- When an update is available, a small card offers it (Update now / Later) and then shows download progress with a Cancel button, instead of tray balloons (#134).

### Fixed
- Cancelling an update download now removes the partly downloaded file, and a download that takes too long says so (#134).
- On long drafts the repair step now also puts back capitalised words the rewrite dropped (a person's name, a month, a place), inside a full sentence. Names typed in lowercase are not detected (#130).

## [1.5.0] - 2026-11-07

### Added
- Settings → Requirements: a new "All tasks" entry. Its text is added word for word to the end of every rewrite, after the text for that kind of task, and never twice (#118).

### Fixed
- The repair step no longer appends a dropped detail as a bare fragment (a lone number on its own line, or stuck on after a full stop). Such a repair is now rejected and the first rewrite is kept with the "check these details" warning. The repair request also asks for the detail inside a full sentence (#124).
- Raw `<rewrite>` and `</rewrite>` tags no longer show up in the Improved text after the repair step (#123).

## [1.4.1] - 2026-11-05

### Fixed
- A copy of `Phrasor.exe` built with `dotnet build` (it has `Phrasor.dll` beside it) no longer offers to install itself. Before, choosing Yes copied only the launcher into the install folder and broke the installed app. Release downloads are unchanged (#113).

## [1.4.0] - 2026-11-05

### Added
- Settings → Requirements: write your own standing requirements for each kind of task (for example, Coding: "find the root cause, follow existing conventions, no workarounds"). They are added word for word to the end of every rewrite of that kind, are never sent to the model, and are skipped when a rewrite comes back unchanged (#36).

### Changed
- The log now records why a rewrite failed: the error kind, the provider's token limit and requested size when it reports them, the mode and the draft length. Never the prompt or the provider's message. This is so "too long for the free-tier limit" errors can be diagnosed (#101).
- The log line for each rewrite now also says what the repair step did (restored, no improvement, failed, off), how many exact details were missing before and after, and why any failed call led to a retry or the backup model. Error kinds, limits and counts only (#104).
- When a call is rate-limited, the log now also records the tokens already used in that minute and how long the provider said to wait, so the right wait before falling back to the backup model can be chosen from real numbers. Numbers only (#101).

### Fixed
- The review window now says when the backup model answered ("Answered by the backup model (...) because ... couldn't answer. Press Ctrl+R after a minute to try the main model again."). In instant mode this also opens the review window instead of replacing your text straight away, so you see the notice (#101).
- Running a second long draft within a minute no longer silently switches to the backup model when the main model only needs a short wait: Phrasor now waits up to 25 seconds (Groq asked for 19 in testing) and retries the main model once before falling back. Before, it waited only for the last model in the list, and only up to 6 seconds (#101).
- Long drafts: when the rewrite drops exact details, the repair step now sends only the rewrite and a short excerpt of the draft for each missing detail, not the whole draft again, and tries the backup model if the main one is rate-limited. Before, on a 14,700-character draft the repair request alone was 7,863 tokens against the 8,000-per-minute free-tier limit, so it always failed and the details stayed missing. A repair that shortens the rewrite is ignored (#76).

## [1.3.0] - 2026-11-02

### Added
- The running version now shows in the tray menu ("Phrasor 1.2.0") and at the bottom of the Settings window (#84).
- Quality check: three long synthetic drafts (about 1,600, 3,800 and 7,400 characters, 11 to 13 points each), each in Standard and Detailed, so dropped points and shrinking on long input can be measured (#76).
- Quality check: a per-case `minLengthRatio` (set to 0.5 on the long cases) fails a rewrite that shrinks below it. The README now says the judge can't be trusted to see dropped points on long drafts (#90).
- Phrasor now appears in Settings → Apps → Installed apps with an Uninstall button. Uninstall removes the app, its Start menu shortcut and its start-with-Windows entry, and asks before deleting your settings and API keys (default: keep). The entry is added when you install or update (#86).

### Fixed
- When the model's reply isn't valid JSON (Groq: "Failed to validate JSON", seen on long drafts in Detailed mode), Phrasor now retries once and then tries the fallback model, instead of stopping with an error (#88).
- "Check for updates" now says when GitHub is limiting requests from your network and when to try again, instead of only "GitHub answered 403" (#95).

## [1.2.0] - 2026-10-30

### Added
- Quality check: a new case for a short bug report with a "don't break anything" limit, taken from real use (#74).
- Quality check: each case runs 3 times by default (`--repeat N` to change), and the report shows the pass rate per case, lowest first, plus how many cases passed every run, failed every run, or were mixed.
- Quality check: a judge model set separately in Settings → Data (Gemini by default, using the Gemini key from the Model tab). Without its key, the main model's fallback grades and the report says the judge isn't independent.
- Quality check: the judge also flags rewrites written as the AI's reply and requests the draft didn't make (extra deliverables, answer formats, "ask me first" steps), and its intent check covers statements, answers and standing instructions.
- `--system-prompt <file>` runs the quality check with another version of the rewriting rules, such as `eval/prompts/v1.0.0.txt`, without a new build.
- `--eval-compare before\results.json after\results.json compare.md` compares two runs case by case and separates real changes from run-to-run noise.
- Eval cases can carry the app hint (`writtenIn`), and 8 new cases cover the rewrites reported in #16, #17, #18 and #25–#28.
- Quality check: cases say how deep the ideal rewrite goes (`expect`) and what it must cover (`mustCover`), and a third of the new cases are held out (`--include-held-out`) for final measurements. 36 new cases cover agent tasks, mid-chat messages, the gaps found in real prompts and open-ended requests; `cases-write.json` holds briefs for the coming write mode.
- Quality check: the judge grades by intent (#34). Spelling out a stated goal ("make sure nothing breaks" → plan, approve, verify) no longer counts as an added request; a new "under-delivered" check fails rewrites that only fix the wording when the draft's goals needed spelling out, or that miss a case's `mustCover` item. The judge now reads each case's `expect` and `mustCover`.
- `--rejudge <results.json> <cases.json> <folder>` grades a saved run again with the current judge, without calling the optimiser, so an old baseline can be graded by the same judge as a new run. With `--grades <file.csv>` it also reports how often the judge agrees with your own grades.

### Changed
- Rewriting rules (#35): rewrites now follow what you meant. Stated goals ("make sure nothing breaks") are spelled out as concrete steps; open-ended requests ("you decide") get a proposed structure labelled as suggestions; statements and answers are only tidied. The old per-task-type additions and the "ask me first" fallback lines are gone, and missing details are asked in the review window instead.
- Rewriting rules, measured with the quality check: 57 of 75 runs passed on the 25 new cases (old rules 31 of 75), 185 of 222 on the 76 older cases (158 of 228), and 26 of 32 on the 11 held-out cases (16 of 31). Known gaps, to be worked on next: invented numbers or extra requirements in some rewrites (#79), fewer questions on vague drafts (#80), and a pasted message plus a short "fix this" that can come back garbled (#81).

### Fixed
- Re-run no longer discards edits you made to the improved prompt without asking: it now asks before replacing them. "Undo my edits" also no longer lights up for an untouched multi-line prompt (#20).
- "Undo my edits" no longer shows a multi-line improved prompt on a single line in the review window (#63).
- Re-run now sends the review window's questions together with your answers, in their own section beside the draft. Before, only the answers were appended to the draft, so a short reply like "uni essay, 800" lost its meaning, was rewritten as more draft, and had to survive the dropped-details check word for word (#19).
- Quality check: runs that errored (rate limits, network failures) no longer count as failures. Comparisons leave them out, mark a case "not comparable" when errors leave it too few clean runs, and report how many runs errored on each side; pass rates in the report leave them out too.
- Quality check: a rate limit no longer turns the rest of the run into errors. The runner waits out short limits (up to 20 minutes, 3 times per call) and retries the same run; when the day's allowance is gone it stops cleanly, keeps what finished, and says which cases didn't run in `progress.txt` and the report.
- Quality check: a judged run the judge couldn't grade is now an error instead of a pass.

### Removed
- The phrase list that flagged rewrites written in the AI's voice. It only caught wordings we had thought of; the judge checks this now.

## [1.1.1] - 2026-10-15

### Fixed
- Voice now records from Windows' default recording device (and follows it if you change it). Before, it always used the first microphone in the device list, which on PCs with a headset, webcam or several mics could be the wrong one.
- The shortcut box in Settings recorded Ctrl+Alt+key as Ctrl+key on AltGr keyboard layouts, because Windows left Alt out of the keypress. It now reads the physical modifier keys too.
- Shortcuts that clash with your keyboard layout are now explained. On layouts where Ctrl+Alt acts as AltGr (German, Polish, French, English (India) and many more), Ctrl+Alt+key types a character: Phrasor now names the character at startup and before saving in Settings, instead of silently blocking it or failing.

### Changed
- Updated to NAudio 3's renamed recording API (`WaveIn`), removing the build warnings.

## [1.1.0] - 2026-10-15

### Added
- **Update check.** Once a day (and from **Check for updates** in the tray menu), Phrasor asks GitHub for the latest release. If it's newer, a notification offers to update: the exe is downloaded, verified against `SHA256SUMS.txt`, and installed through the usual **Yes** prompt. It can be turned off in Settings → General and sends no prompt text, settings or keys.
- **Image** task type: image-generation prompts (comma-separated descriptors, `--ar` style parameters) keep their form and only get typo fixes, instead of being rewritten into sentences.
- The user's own template slots, like `[Brand]`, `{{first_name}}` and `${Role:default}`, are now protected details and are checked after every rewrite.
- System prompt examples now include drafts that should come back unchanged.
- Eval: 9 new cases (image, placeholders, assistant-voice, already-good) and a check that flags rewrites written as the AI's reply.
- When a draft refers to material it doesn't include ("the attached file", "the code above", "the provided material"), the review window asks what it is instead of the rewrite guessing.
- Eval: the judge now flags dropped requirements and changed intent, and 10 new cases cover missing material, casually stated rules, and questions or requests that must keep their type.

### Changed
- Voice recording now uses NAudio 3.1 (was 2.4).
- Rules, criteria and conditions the user states are treated as requirements however casually they're written, and every item a draft asks about is kept.
- The rewrite keeps what kind of message the draft is: a question stays a question even with a word missing, and the requested action ("fix") isn't swapped for another ("explain"). Pasted text followed by a short request is treated as context, not as the task.

### Fixed
- Voice: a silent recording (microphone muted or turned off in Windows, or nothing said) was sent for transcription, and Whisper typed text it invented from the silence, usually "Thank you.". Silent recordings are now caught before sending, and a notification says whether the mic recorded nothing at all or just no speech.
- The repair pass for dropped details could make the model add an instruction like "Ensure the explanation includes the exact phrases…". It now asks for the details to be put back where they were, without meta-instructions.
- Rewrites could come back in the assistant's voice ("Once I have this information, I can..."). The system prompt now says the rewrite is always the user's request to the AI.
- Standard mode now puts any background the user gave before the request.

## [1.0.0] - 2026-10-13

First public release. It contains everything from the 0.x builds plus the fixes below.

### Added
- Open-source release under the MIT License, with contributing guide, security policy, issue templates and CI.
- Release builds are produced by GitHub Actions from version tags, with a SHA-256 checksum.

### Fixed
- Settings: **Cancel** and **Esc** now close the window. Before, only the title-bar close button did.

### Highlights
- Global shortcuts optimise the focused text box (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>P</kbd>) or the current selection (<kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>), with a review window (improved / word diff / original) before anything is replaced.
- Replacement is verified by reading the field back. Windows 11 Notepad's delayed paste is handled with delayed clipboard rendering, and the clipboard is restored afterwards.
- The optimiser classifies the task, asks questions instead of inventing details, keeps exact details (with a repair pass), and holds back pasted code and traces so they come back byte-for-byte.
- Groq (free tier) by default, with an automatic fallback model. Gemini, Ollama and any OpenAI-compatible API are also supported.
- Voice dictation via Whisper, tap-to-toggle or hold-to-talk.
- The app installs and updates itself: no admin rights, a Start menu shortcut, and optional start with Windows.
- Built-in quality check over a 49-prompt test set.

## [0.3.1] - 2026-10-13

### Fixed
- Pasted material that the model placed mid-sentence is restored on its own lines.

## [0.3.0] - 2026-10-13

### Changed
- Vague drafts now get follow-up questions instead of coming back unchanged; no invented numbers; no questions the draft already answers.

## [0.2.0] - 2026-10-13

### Added
- Verified paste via delayed clipboard rendering, self-install/update, quality check from the tray, Settings brought to the front on relaunch.

## [0.1.0] - 2026-10-12

### Added
- Initial personal build: core optimiser, tray app, review window, settings, voice and eval runner.

[1.5.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.5.0
[1.4.1]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.4.1
[1.4.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.4.0
[1.3.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.3.0
[1.2.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.2.0
[1.1.1]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.1.1
[1.1.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.1.0
[1.0.0]: https://github.com/Maneet7805/Phrasor/releases/tag/v1.0.0
[0.3.1]: https://github.com/Maneet7805/Phrasor/commits/main
[0.3.0]: https://github.com/Maneet7805/Phrasor/commits/main
[0.2.0]: https://github.com/Maneet7805/Phrasor/commits/main
[0.1.0]: https://github.com/Maneet7805/Phrasor/commits/main
