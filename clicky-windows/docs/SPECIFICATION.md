# Clicky for Windows — SDD Specification & Audit (v2, re-verified)

**Status:** Living document. Regenerated from a line-by-line re-audit of `clicky_windows/` at `HEAD = 444e78d` ("Implement Universal AI Provider Architecture, Visual Teaching Engine, and Clicky Cursor fixes").
**Scope:** `clicky_windows/` is the single source of truth for the Windows implementation. `leanring-buddy/` (Swift/macOS) and `worker/` are consulted only as reference contracts.
**Working tree:** clean except this `docs/` folder (untracked).

## How this audit was validated

Every claim below was checked by tracing the **current** source, not by inference. For framework behavior (the Avalonia `Screens` API), the Avalonia 12.0.4 source was fetched directly (`WindowBase.Screens`, `TopLevel.Screens`). Each spec item cites the exact file:line that demonstrates the defect.

## Implementation status

| Spec | Status | Notes |
|---|---|---|
| SPEC-08 (CI template) | ✅ Closed (branch `fix/epic0-engineering-hygiene`) | Workflow replaced with clean `dotnet build`+`test` for Debug+Release on `windows-latest`. |
| SPEC-09 (bin/obj tracked) | ✅ Closed | `.gitignore` updated; 226 artifacts `git rm --cached`'d; `git ls-files` shows 0 bin/obj. |
| SPEC-10 (dead scaffolding) | ✅ Closed | Removed `MainWindow*`, `ViewModels/*`, `ViewLocator`, `App.axaml` registration, unused `CommunityToolkit.Mvvm`. |
| SPEC-01 (AssemblyAI token) | ✅ Closed | `AssemblyAIClient.ResolveTokenRequest` splits worker mode (POST `{worker}/transcribe-token`) from direct mode (GET `streaming.assemblyai.com/v3/token` + key). Default `SttEndpoint` blanked; unconfigured state throws a clear error instead of 404'ing. 14 unit tests cover both modes. |
| SPEC-05 (AssemblyAI JSON parsing) | ✅ Closed | `HandleMessage` reads all turn fields as optional (`ReadOptionalString/Boolean/Int32`); `SessionBegins`/partial envelopes no longer throw. Stop ordering fixed (close before cancel) so the final turn isn't dropped. |
| SPEC-07 (pointing coordinate drift) | ✅ Closed | `CoordinateMath.ToLocalDip` subtracts the monitor origin in device px and divides by Scaling exactly once; removed the old `Bounds.X * Scaling` double-scaling. `app.manifest` now declares PerMonitorV2 DPI awareness. A regression test proves the old code yielded a negative coordinate where the fix yields the correct positive value. |
| SPEC-03 (capture quality/exclusion/DPI) | ✅ Closed | Captures downscale to ≤1280px (quality 85 JPEG) with aspect preserved; own-process windows blanked out of each capture via `EnumWindows`; all GDI handle teardown moved to try/finally. 12 scaling tests added. |
| SPEC-02 (threading/cancellation) | ✅ Closed | `CompanionManager.OnPropertyChanged` marshals to `Dispatcher.UIThread` (all `SetField` callers safe from any thread); `Stop()` cancels/disposes `_pipelineCts` and resets `VoiceState`/`Point*`; `GlobalHotkey.Start/Stop` marshal to the UI thread and guard against double-install so the `WH_KEYBOARD_LL` hook always lives on the message-pump thread (safe across suspend/lock cycles). |
| SPEC-04, SPEC-06, SPEC-11..SPEC-14 | ⏳ Open | |

The test project (`clicky-windows-tests`, xUnit) now has **43 passing tests** covering `ProviderRegistry`, `ClickySettings` serialization, the AssemblyAI token resolver, the overlay coordinate math, and the capture scaling math; a `clicky-windows.sln` ties both projects together.

## Retractions from the v1 audit

I was wrong on two material points in the first audit. Acknowledging and correcting them here:

1. **"The blue cursor never appears" (v1 SPEC-01) — RETRACTED.** I claimed `_settingsWindow.Screens.All` is empty for a freshly-constructed window. That is false in Avalonia 12.0.4. `TopLevel.Screens` reads `PlatformImpl?.TryGetFeature<IScreenImpl>()`, and `PlatformImpl` is assigned in the `Window` constructor (eager window creation), so `Screens.All` is populated at construction — before `Show()`. The overlay windows **are** created, the `IsOverlayVisible` toggle drives `Show()`/`Hide()` in `OverlayWindow.axaml.cs:86-105`, and the cursor works. This was an inference error on my part about framework internals. The cursor overlay is **not** a defect.

2. **"The app is locked to the placeholder Cloudflare Worker" (v1 SPEC-02) — substantially RETRACTED.** BYO-provider support is fully implemented: 21 provider presets in `Models/ProviderRegistry.cs`, a complete Settings UI with preset/format/auth/vision/streaming controls in `Views/SettingsWindow.axaml(.cs)`, and `GenericAiClient.cs` handling both Anthropic and OpenAI request formats with configurable auth. Direct API calls work for AI and TTS. Only the AssemblyAI default endpoint has a real bug (see SPEC-01 below); it is not a systemic "every path 404s" failure.

What survives this re-audit is below — smaller, sharper, and each item independently reproducible.

---

## 1. Validated Issues → Specifications

Severity: **P0** (broken feature), **P1** (works-but-wrong / regression risk), **P2** (polish).

---

### SPEC-01 — AssemblyAI direct-mode token endpoint is wrong (P1)

**Current behavior.** `ClickySettings.SttEndpoint` defaults to `https://api.assemblyai.com/v2` (`Models/SettingsManager.cs:22`). For the default `SttProvider = "AssemblyAI"`, `SpeechToTextProvider.StartSessionAsync` passes that string to `AssemblyAIClient.StartSessionAsync(proxyUrl, …)`, which builds `{proxyUrl}/transcribe-token` → `POST https://api.assemblyai.com/v2/transcribe-token` (`AssemblyAIClient.cs:39-40`). That route does not exist on AssemblyAI (the real endpoint is `https://streaming.assemblyai.com/v3/token`), so the token fetch 404s and the session throws. The websocket URL/params themselves (`AssemblyAIClient.cs:48-63`) are correct and match the macOS reference.

**Expected behavior.** In worker mode (endpoint = a worker URL), `POST {worker}/transcribe-token` works because the worker proxies to the correct `v3/token` (`worker/src/index.ts:82-91`). In direct AssemblyAI mode, the token must be fetched from `https://streaming.assemblyai.com/v3/token` with the AssemblyAI API key as the `authorization` header — not via the `/transcribe-token` worker route. The two modes need distinct token-fetch code paths.

**Root cause.** One code path (worker `/transcribe-token` POST) is used for both worker and direct modes. The default endpoint mixes a direct-AssemblyAI host with a worker-route suffix.

**Acceptance criteria.**
- AC1: With the endpoint set to a Cloudflare Worker URL, streaming transcription connects and returns a transcript.
- AC2: With `SttProvider=AssemblyAI`, a direct `SttApiKey`, and no worker, transcription connects via `streaming.assemblyai.com/v3/token` and returns a transcript.
- AC3: No request is sent to `api.assemblyai.com/v2/transcribe-token`.

**Evidence.** `AssemblyAIClient.cs:30-45`, `Models/SettingsManager.cs:22`, `SpeechToTextProvider.cs:42-50`.

---

### SPEC-02 — Cross-thread mutation of observable state; pipeline not cancellable on Stop() (P1)

**Current behavior.** `CompanionManager` is a plain `INotifyPropertyChanged` with no thread affinity. `OnFinalTranscriptReceived` runs inside a `Task.Run` (`CompanionManager.cs:283`) and sets `VoiceState`, `PointX`, `PointY`, `BubbleText`, `LastTranscript` from a thread-pool thread, while `OverlayWindow.OnAnimationTick`/`OnManagerPropertyChanged` read those same properties on the UI thread — no marshalling. Separately, `Stop()` (`CompanionManager.cs:217-223`) uninstalls the hotkey and stops audio/TTS but **never cancels `_pipelineCts`**, so an in-flight screenshot+AI+TTS pipeline keeps running after Stop. The power-suspend and session-lock handlers in `App.axaml.cs:197-223` call `Stop()` then `Start()`, which means a response can continue during a locked session and the keyboard hook can be reinstalled on the `SystemEvents` thread rather than the UI thread.

**Expected behavior.** UI-bound state mutated only on the UI thread; `Stop()` cancels the in-flight pipeline; suspend/lock cleanly tears down and resume/unlock re-arms on the UI thread.

**Root cause.** No UI-thread invariant; partial cancellation wiring; cross-thread hook lifecycle.

**Acceptance criteria.**
- AC1: Rapidly toggling state during a streaming response does not throw or race.
- AC2: `Stop()` aborts the AI/TTS pipeline within ~1 frame.
- AC3: Lock→unlock does not leave two keyboard hooks installed (single `_hookId`).

**Evidence.** `CompanionManager.cs:217-266, 283-448`, `App.axaml.cs:197-223`, `GlobalHotkey.cs:33-55`.

---

### SPEC-03 — Screen capture sends full-resolution screenshots including the app's own overlays; no DPI awareness (P1)

**Current behavior.** `ScreenCapturer.CaptureScreenRect` does a raw `BitBlt` at full native monitor resolution and JPEG-encodes with no quality/compression setting (`ScreenCapturer.cs:122-160`). Three problems:
- No max-dimension cap. The macOS reference downscales to 1280px on the long side (`CompanionScreenCaptureUtility.swift:84-92`). A 4K monitor sends a multi-MB base64 payload.
- The app's own topmost overlay/settings windows are captured into the screenshot, so the AI sees the cursor/bubble it is drawing. macOS explicitly excludes own-app windows (`SCContentFilter(display:excludingWindows:)`, `CompanionScreenCaptureUtility.swift:42-45`).
- `app.manifest` declares no `<dpiAware>`/`<dpiAwareness>` (only a Windows 10 `supportedOS`, `app.manifest:9-16`). On multi-DPI setups this causes GDI pixel coordinates to disagree with Avalonia `Screen.Bounds`, which breaks pointing accuracy (interacts with SPEC-07).
- GDI handles (`hBitmap`, DCs) are released only on the success path; an exception in `Image.FromHbitmap` leaks them (no try/finally, `ScreenCapturer.cs:134-158`).

**Expected behavior.** Downscale to ≤1280px; exclude own windows; declare per-monitor v2 DPI awareness; release all GDI handles on every path.

**Acceptance criteria.**
- AC1: A 3840×2160 monitor's JPEG has its longest side ≤ 1280px.
- AC2: A screenshot never contains the blue cursor or the settings window.
- AC3: On a mixed-DPI multi-monitor setup, a `[POINT]` coordinate lands within a few px of the intended element.
- AC4: GDI handle count is stable across 100 capture cycles.

**Evidence.** `ScreenCapturer.cs:82-160`, `app.manifest:9-16`. Reference: `CompanionScreenCaptureUtility.swift:42-45, 84-92`.

---

### SPEC-04 — LLM is non-streaming despite a `SupportsStreaming` setting; default model stale (P1)

**Current behavior.** Both request paths hardcode `stream = false` (`GenericAiClient.cs:104`, `GenericAiClient.cs:194`). The `AiSupportsStreaming` setting is collected in the Settings UI and stored but **never read** in `GenericAiClient` — there is no SSE parser. The macOS reference streams via SSE so TTS can begin sentence-by-sentence. The default `AiModel = "claude-3-5-sonnet-20241022"` (`Models/SettingsManager.cs:12`) is an older model id (macOS defaults to `claude-sonnet-4-6`).

**Expected behavior.** When `AiSupportsStreaming` is true, stream the response and parse `[POINT]`/`[DRAW]` tags so speech can begin before the full response arrives. Update the default model to a current Claude id (or make it preset-driven so the registry is the single source of truth).

**Acceptance criteria.**
- AC1: With a streaming-capable provider, the first TTS sentence begins before the full response is received.
- AC2: All `[POINT]`/`[DRAW]` tags parse and execute correctly under streaming.
- AC3: Non-streaming providers continue to work unchanged.
- AC4: The default model id is current, not `claude-3-5-sonnet-20241022`.

**Evidence.** `GenericAiClient.cs:98-107, 190-196`, `Models/SettingsManager.cs:12`. Reference: `ClaudeAPI.swift:101-212`.

---

### SPEC-05 — AssemblyAI JSON parsing rejects valid messages (P1)

**Current behavior.** `AssemblyAIClient.HandleMessage` reads `end_of_turn`, `turn_is_formatted`, `turn_order` with `GetProperty(...).GetBoolean()/GetInt32()` (`AssemblyAIClient.cs:167-169`) — required, not optional. Any `turn` envelope missing one of these fields throws and is swallowed by the surrounding catch (`AssemblyAIClient.cs:190-195`), dropping the message. The macOS decoder marks all of these optional. Real AssemblyAI streams emit `SessionBegins`, `PartialTranscript`, and `Turn` messages with varying schemas.

**Expected behavior.** Mirror the macOS decoder: treat every field as optional, accumulate formatted turns, deliver interim updates, and never throw on a well-formed-but-incomplete envelope.

**Acceptance criteria.**
- AC1: A `SessionBegins`-style message does not throw or drop subsequent messages.
- AC2: Interim transcripts update `LastTranscript` live while listening.
- AC3: The final formatted transcript is delivered on key-up.

**Evidence.** `AssemblyAIClient.cs:155-195`. Reference: `AssemblyAIStreamingTranscriptionProvider.swift:92-105`.

---

### SPEC-06 — "System" TTS spawns powershell.exe per utterance (P1)

**Current behavior.** `TextToSpeechProvider.SpeakLocalSystemAsync` builds a `ProcessStartInfo` with `FileName = "powershell"` and an `Add-Type -AssemblyName System.Speech; … $synth.Speak(...)` command (`TextToSpeechProvider.cs:159-167`), starting a new PowerShell process for every utterance. This adds ~300ms cold-start latency per sentence, can be flagged by AV/EDR as suspicious child-process spawning, and leaks the process if `StopPlayback` races the kill (`TextToSpeechProvider.cs:215-219`). Note: this only affects the **System TTS** provider; the default ElevenLabs and OpenAI paths use in-process NAudio playback and are fine.

**Expected behavior.** Use an in-process speech synthesizer (`System.Speech.Synthesis.SpeechSynthesizer` referenced as a NuGet assembly, or `NAudio` + an installed SAPI voice), never a shell.

**Acceptance criteria.**
- AC1: No `powershell.exe` (or `conhost.exe`) process is spawned by the app at any time.
- AC2: System TTS speaks with <50ms setup latency.
- AC3: Stopping mid-speech halts audio within one frame without orphaned processes.

**Evidence.** `TextToSpeechProvider.cs:151-181, 215-219`.

---

### SPEC-07 — Pointing/drawing coordinate mapping mixes two monitor coordinate systems (P1)

**Current behavior.** `CompanionManager.OnFinalTranscriptReceived` adds each monitor's GDI `MONITORINFO` `X`/`Y` (device pixels, top-left origin) to drawing/point coordinates (`CompanionManager.cs:382-412`). `OverlayWindow` then subtracts `_targetScreen.Bounds.X * _targetScreen.Scaling` (`OverlayWindow.axaml.cs:132-139, 471-474`). These two coordinate sources — Win32 `MONITORINFO` vs Avalonia `Screen.Bounds` — use different origins and scaling semantics, so on any multi-monitor or non-100%-DPI setup, drawings and `[POINT]` targets drift. On a single 100%-scale monitor it happens to cancel out.

**Expected behavior.** One coordinate space end-to-end. Recommendation: keep everything in Win32 device pixels (what `MONITORINFO` and `GetCursorPos` already use) and divide by `Scaling` only at the final `Canvas.SetLeft/Top` step. Stop reading `Screen.Bounds` inside the overlay.

**Acceptance criteria.**
- AC1: On a 2-monitor setup with different DPI scales, `[POINT]` and `[DRAW]` land within a few px of the intended target on both monitors.
- AC2: The cursor-following position matches `GetCursorPos` exactly at 100% scale.

**Evidence.** `CompanionManager.cs:370-412`, `OverlayWindow.axaml.cs:131-139, 470-504`.

---

### SPEC-08 — CI workflow is the unmodified WPF/MSIX template (P1)

**Current behavior.** `.github/workflows/dotnet-desktop.yml` at HEAD still references `your-solution-name`, `your-test-project-path`, `your-wap-project-directory-name`, `your-wap-project-path` (env block lines 59-62), decodes a `Base64_Encoded_Pfx` secret (line 93), runs `dotnet test` against a nonexistent test project (line 82), and MSIX-packages a nonexistent `.wapproj`. There is no `.sln`, no test project, and no packaging project. Every push/PR to `main` runs a red pipeline.

**Expected behavior.** CI builds `clicky-windows.csproj` with `dotnet build` for Debug + Release. Add a test project so `dotnet test` has a target.

**Acceptance criteria.**
- AC1: The workflow is green on `main` for Debug and Release.
- AC2: No placeholder names or absent secrets referenced.

**Evidence.** `.github/workflows/dotnet-desktop.yml:59-62, 82, 93` (verified at HEAD).

---

### SPEC-09 — 226 build-output files are tracked in git; `.gitignore` has no bin/obj entries (P1)

**Current behavior.** `git ls-tree -r HEAD --name-only | grep -E "clicky-windows/(bin|obj)/"` returns **226 files** at HEAD, including every Avalonia dependency DLL, the compiled exe/pdb, and generated assembly info. `.gitignore` at HEAD contains only `worker/node_modules/`, `worker/.dev.vars`, `.DS_Store`, `*.xcuserstate`, `build/`, `releases/`, `.claude/`, `coding-plans/` — no `bin/`, no `obj/`, no `*.user`.

**Expected behavior.** `bin/`, `obj/`, and VS user files ignored and untracked.

**Acceptance criteria.**
- AC1: `git ls-files clicky-windows | grep -E "(bin|obj)/"` returns nothing.
- AC2: `git status` is clean after a fresh build.

**Evidence.** `git ls-tree -r HEAD` (226 matches), `.gitignore` (verified at HEAD).

---

### SPEC-10 — Dead template scaffolding (P2)

**Current behavior.** `Views/MainWindow.axaml(.cs)`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/ViewModelBase.cs`, and `ViewLocator.cs` are Avalonia project-template leftovers. The app is tray-only; no `MainWindow` is ever shown. A grep for external references to `MainWindow`, `MainWindowViewModel`, `ViewModelBase`, `ViewLocator` returns **zero** hits outside their own files. `CommunityToolkit.Mvvm` is referenced in the csproj but no `[ObservableProperty]`/`RelayCommand`/`ObservableObject` is used anywhere.

**Expected behavior.** Remove unused scaffolding, or actually adopt MVVM for the settings window. Either way, no dead files and no unused dependency.

**Acceptance criteria.**
- AC1: No source file is unreferenced/dead.
- AC2: `CommunityToolkit.Mvvm` is used or removed from `clicky-windows.csproj`.

**Evidence.** grep across `clicky_windows/` for external references (none); `clicky-windows.csproj:24`.

---

### SPEC-11 — `Logger` exists but is unused (P2)

**Current behavior.** `Logger.cs` provides `Info`/`Warn`/`Error` writing to `%LocalAppData%/Clicky/clicky.log`, but `Logger.Info/Warn/Error` has **zero call sites** across the codebase. All 38 diagnostic call sites use `Console.WriteLine`, which a tray app has no console for in production — logs are lost.

**Expected behavior.** Route diagnostics through `Logger`; in Debug also echo to a debug listener.

**Acceptance criteria.**
- AC1: Lifecycle and error events appear in `clicky.log`.
- AC2: No bare `Console.WriteLine` remains for diagnostics.

**Evidence.** grep `Logger.(Info|Warn|Error)` → 0 matches; grep `Console.WriteLine` → 38 matches.

---

### SPEC-12 — No user-visible error feedback; pipeline can stick in Processing (P2)

**Current behavior.** When STT/TTS/AI fail, handlers write to console and reset `VoiceState = Idle` (e.g. `CompanionManager.cs:198-202`), but a failure inside the response `Task.Run` after the AI call but before the per-step loop is caught at `CompanionManager.cs:440-443` and only logged — the user sees the cursor vanish with no explanation. macOS at least speaks a credits-error fallback (`CompanionManager.swift:761-766`).

**Expected behavior.** Surface failures visibly (overlay bubble or settings status), and never leave the cursor stuck in `Processing` on error.

**Acceptance criteria.**
- AC1: A provider error shows a dismissible message.
- AC2: A mid-response failure returns to Idle cleanly within ~1 frame.

**Evidence.** `CompanionManager.cs:198-202, 440-448`.

---

### SPEC-13 — Hardcoded version string; no installer/update channel (P2)

**Current behavior.** `Views/SettingsWindow.axaml:102` renders a literal `v1.2 (Universal)` text. There is no installer, no `VERSION` constant, and no Windows update feed (the repo's `appcast.xml` is macOS-Sparkle only).

**Expected behavior.** The settings header reads the real assembly version. A first-class install path exists (MSIX or Velopack/InnoSetup). Auto-update is a later epic.

**Acceptance criteria.**
- AC1: Settings header shows the assembly version, not a hardcoded string.
- AC2: A one-click installer produces a Start Menu entry and uninstaller.

**Evidence.** `Views/SettingsWindow.axaml:102`.

---

### SPEC-14 — Production-hardening gaps (P2)

A cluster of smaller gaps, each its own ticket:
- **No single-instance guard** — a second launch installs a second hook + tray icon.
- **No keyboard-hook watchdog** — if the OS drops the low-level hook, push-to-talk silently dies.
- **No microphone device selection** — `AudioRecorder` uses the default capture device only (`AudioRecorder.cs:22-25`).
- **Settings window positioning assumes a bottom taskbar** (`SettingsWindow.axaml.cs:21-33`) — breaks for a top taskbar.

---

## 2. Feature-parity matrix (re-verified)

| Advertised feature | Windows status at HEAD | Spec |
|---|---|---|
| Tray icon + settings panel | ✅ works | — |
| Push-to-talk Ctrl+Alt | ✅ works (minor edge cases, see SPEC-02) | — |
| Live waveform while listening | ✅ works | — |
| Blue cursor follows mouse | ✅ works (v1 was wrong) | — |
| Multi-monitor overlays | ✅ created correctly (v1 was wrong) | — |
| `[POINT]` pointing | ⚠️ drifts on multi-DPI | SPEC-07 |
| `[DRAW:*]` annotations | ✅ works (Windows superset over macOS) | — |
| BYO AI/STT/TTS providers | ✅ implemented (v1 was wrong); ⚠️ AssemblyAI direct mode broken | SPEC-01 |
| Streaming AI response | ❌ non-streaming | SPEC-04 |
| Conversation history | ✅ works | — |
| Settings persistence | ✅ works | — |
| Screen capture | ⚠️ no resize/exclude/DPI | SPEC-03 |
| TTS (ElevenLabs/OpenAI) | ✅ works | — |
| TTS (System) | ⚠️ shells out to powershell | SPEC-06 |

---

## 3. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| CI always red | High (certain) | Blocks collaboration | SPEC-08/09 |
| bin/obj in git bloats repo/diffs | High | Slows PRs | SPEC-09 |
| Multi-DPI pointing wrong | High | Core UX | SPEC-03/07 |
| Thread races crash app | Medium | Instability | SPEC-02 |
| AssemblyAI direct mode broken | High | STT unusable without worker | SPEC-01 |
| PowerShell flagged by AV | Medium | Deployment | SPEC-06 |

---

## 4. Out of scope (deferred)

- PostHog analytics.
- Onboarding video + demo interaction (macOS-specific media).
- MSIX auto-update (SPEC-13 covers installer only).
- Windows-equivalent TCC permission UX.

---

## 5. Definition of Done (per spec item)

A spec item is closed only when: implementation merged, build green, relevant ACs verified, no regression in the parity matrix, and this document updated.
