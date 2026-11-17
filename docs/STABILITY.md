# Stability audit — 2026-11-18

Candidate branch: `test/stage-6-stability`. Installed/released version remains 0.6.0. These fixes have not been installed or released.

## Reproduced and fixed

- `Clips: null` in library.json crashed the real WPF startup. Settings now normalize null entries, missing names, invalid ranges and numeric overflow. Damaged originals are copied into Data/Recovery; malformed JSON can recover the last successful `.bak` save. Existing primary saves remain atomic file replacements.
- Rapid Play/Stop before the output callback accumulated stopped mixer inputs and eventually hit NAudio's input limit. Stop now removes clip inputs immediately, and failed mixer insertion disposes its readers. Microphone inputs remain connected.
- Output/capture callback errors are surfaced in the window; microphone status follows capture state. Physical unplugging has not been tested.
- DSP, gain/bass processing and decoders were not changed.

## Passed locally

| Check | Evidence |
|---|---|
| Settings recovery | malformed JSON, exact original preservation, backup recovery, null clips/entries, missing names/paths, overflow and ranges |
| Actual WPF startup | null clip list renders a window and preserves the damaged original |
| Audio stress | 2,000 Play/Stop cycles, 341.3 seconds simulated audio; 1.4 seconds wall time; finite samples; managed memory delta -14,912 bytes; handle delta +4 |
| Rapid control | 1,100 Play/Stop operations before callback; fixture file deletable afterwards |
| Actual VB-CABLE | 10 reopen/play/stop cycles; invalid device rejected, valid device opens afterwards |
| Real-time device run | 121 seconds, 120 silent WAV launches at 500%/24 dB, handle delta +2; no reported callback errors |
| Audio regressions | real OGG decoding through EOF, resampling, channel conversion, gain/bass/stop, synthetic voice remains after clip stop |
| OPL | portable round trip, order/settings, duplicates, rollback and malformed/checksum/path rejection |
| UI and languages | tiles/list, search, persistence, selection/removal/undo, scrolling; uk/en/ru switching and small-window rendering |
| Installer script | update/reinstall preserve Data; missing executable, traversal and Data payload rejected; locked-file failure rolls back earlier writes |

Device tests used generated silence and did not capture the physical microphone or change system default devices. Installer fault tests used disposable payloads and destinations, not the installed application. The released installer wrapper was verified in stage 5; it was not rebuilt for this audit.

The repeatable headless stability and installer checks are now included in the GitHub Actions workflow. CI results must be checked separately; local success is not a CI result.

## Compatibility and limits

Actual host: Windows 11 25H2 x64, build 26200.9974. The legacy ProductName registry value says Windows 10 Pro; build/version identify the host as Windows 11. See [Microsoft Windows release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information).

No separate Windows 10 machine/VM was available. Windows 10 compatibility is **not verified**. The current [Microsoft .NET support matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions) limits Windows 10 support to listed LTSC/Enterprise editions; general Windows 10 22H2 support must not be promised merely because the application targets Windows.

Not tested: a live Discord conversation, physical microphone capture after these fixes, hot unplug/replug, sleep/wake, a clean OS without runtime/driver, or a multi-hour run. The short stress/device tests do not establish long-term stability.

## Next checkpoint

Stop after this audit. On the user's confirmation: implement GitHub Releases updates in a separate branch, validate installer checksums and library preservation, then prepare the final release. No unattended replacement while OpenPad is running; downloading/installing must have clear user control. Final publication is postponed until tomorrow per user request.
