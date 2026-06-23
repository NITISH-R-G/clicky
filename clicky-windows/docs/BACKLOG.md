# Clicky for Windows — Agile Backlog (v2, re-verified)

Derived from `docs/SPECIFICATION.md` (v2). Every task maps to a spec item whose defect was verified against the current `clicky_windows/` source at `HEAD = 444e78d`.

## What changed from v1

The two v1 P0s ("cursor never appears" and "locked to worker endpoints") were **retracted** after re-audit — both features already work. The backlog is now P1/P2 work, ordered so each epic leaves the app verifiably better and never breaks a working path.

---

## Epic 0 — Engineering hygiene (unblock clean diffs + CI) ✅ DONE (branch `fix/epic0-engineering-hygiene`)
- **T0.1 (SPEC-09)** ✅ Updated `.gitignore` for `bin/`, `obj/`, `*.user`, `.vs/`; `git rm -r --cached` the 226 artifacts. *AC met: `git ls-files` shows 0 bin/obj.*
- **T0.2 (SPEC-08)** ✅ Replaced `.github/workflows/dotnet-desktop.yml` with a minimal `dotnet build`+`test` workflow (Debug + Release, `windows-latest`). *AC met: no placeholders, builds both configs locally.*
- **T0.3 (SPEC-10)** ✅ Removed unused `MainWindow*`, `ViewModels/*`, `ViewLocator`, `App.axaml` registration, and unused `CommunityToolkit.Mvvm`. *AC met: no dead code; build green.*
- **T0.4** ✅ Created `clicky-windows-tests` xUnit project + `clicky-windows.sln`; 11 real tests (ProviderRegistry + ClickySettings serialization) pass in Debug and Release.

## Epic 1 — Make AssemblyAI actually work (P1) ✅ DONE
- **T1.1 (SPEC-01)** ✅ Split `AssemblyAIClient` token fetch via `ResolveTokenRequest`: worker mode → `POST {worker}/transcribe-token`; direct mode → `GET streaming.assemblyai.com/v3/token` with `authorization: SttApiKey`. *AC1-3 met (unit-tested).*
- **T1.2 (SPEC-01)** ✅ Default `SttEndpoint` blanked; the resolver throws a clear "not configured" error routed to `ErrorOccurred` when neither a worker URL nor a key is set, so no request ever hits `api.assemblyai.com/v2/transcribe-token`.
- **T1.3 (SPEC-05)** ✅ All turn fields read as optional (`ReadOptionalString/Boolean/Int32`); `SessionBegins`/partial envelopes no longer throw; stop ordering fixed (close-before-cancel) so the final turn isn't dropped. *AC1-3 met.*
- **T1.4** ✅ 14 new unit tests lock down both modes, the old broken default, the placeholder guard, and the trailing-slash normalization. (Live network round-trips deferred to manual QA on a machine with real keys.)

> Note: T1.4's *automated* half is complete. The *manual* round-trip against a live AssemblyAI/Worker requires real API keys and a Windows desktop session, so it's a verification step for the reviewer, not something this branch can close on its own.

## Epic 2 — Streaming AI responses (P1)
- **T2.1 (SPEC-04)** Add an SSE parser to `GenericAiClient`; honor `AiSupportsStreaming`. *AC1-3.*
- **T2.2 (SPEC-04)** Refactor `CompanionManager.OnFinalTranscriptReceived` to consume an `IAsyncEnumerable<TeachingStep>` so speech can start before the full response.
- **T2.3 (SPEC-04)** Update the default model id (or make the preset the single source of truth). *AC4.*

## Epic 3 — Screen-capture + coordinate correctness (P1) ✅ DONE (commits 4470f17, a47e386)
- **T3.1 (SPEC-03)** ✅ Downscale captures to ≤1280px (longest side, aspect preserved); JPEG quality 85. *AC1 met.*
- **T3.2 (SPEC-03)** ✅ Own-process visible top-level windows blanked out of each capture via `EnumWindows` + `GetWindowRect` + `GetWindowThreadProcessId` (self-contained in `ScreenCapturer`, no overlay hide-during-capture, no flicker). *AC2 met.*
- **T3.3 (SPEC-03)** ✅ `app.manifest` declares `PerMonitorV2, PerMonitor` DPI awareness. *AC3 met.*
- **T3.4 (SPEC-03)** ✅ All GDI handle teardown in try/finally; dropped 3 unused P/Invoke imports. *AC4 met.*
- **T3.5 (SPEC-07)** ✅ `CoordinateMath.ToLocalDip` centralizes device-px→DIP conversion; removed the `Bounds.X * Scaling` double-scaling in all three overlay call sites. *AC1-2 met (regression test asserts the old negative-coordinate bug is gone).*

> Verification: 18 new tests (6 coordinate + 12 scaling); 43 total green in Debug + Release; 0-warnings/0-errors. Live multi-DPI/multi-monitor manual check remains a reviewer step (no such hardware in this environment).

## Epic 4 — Pipeline threading, cancellation, lifecycle (P1)
- **T4.1 (SPEC-02)** UI-thread marshal helper; route all `CompanionManager` observable mutations through it. *AC1.*
- **T4.2 (SPEC-02)** Wire `Stop()` and hotkey-restart to cancel `_pipelineCts`. *AC2.*
- **T4.3 (SPEC-02)** Marshal keyboard-hook install/uninstall to the UI thread; verify single hook across suspend/lock cycles. *AC3.*

## Epic 5 — Replace PowerShell TTS + observability (P1→P2)
- **T5.1 (SPEC-06)** In-process System.Speech synth (or NAudio + SAPI voice); remove the `powershell` process spawn. *AC1-3.*
- **T5.2 (SPEC-11)** Route all diagnostics through `Logger`; add file + debug listeners. *AC1-2.*
- **T5.3 (SPEC-12)** User-visible error surface + never-stuck-on-Processing. *AC1-2.*

## Epic 6 — Ship it (P2)
- **T6.1 (SPEC-13)** Read assembly version into the Settings header; remove hardcoded `v1.2`. *AC1.*
- **T6.2 (SPEC-13)** Velopack (or InnoSetup) installer: Start Menu entry + uninstaller. *AC2.*
- **T6.3 (SPEC-14)** Single-instance guard; keyboard-hook watchdog; mic device picker; taskbar-aware settings positioning.
- **T6.4** Release checklist + signed build.

---

## Dependencies
- Epic 0 is independent and fastest; do it first.
- Epic 1 and Epic 2 are independent of each other.
- Epic 3 (T3.5 coordinate fix) benefits from Epic 4's threading work but can proceed in parallel.
- Epic 5/T5.1 (TTS) is independent.
- Epic 6 depends on all of the above.

## Recommended first milestone
**Epic 0 + Epic 1.** After this: clean repo, green CI, and a working STT round-trip in both worker and direct mode. That's the first verifiable, production-feel increment. Epic 2 (streaming) is the natural second milestone since it most improves perceived latency.
