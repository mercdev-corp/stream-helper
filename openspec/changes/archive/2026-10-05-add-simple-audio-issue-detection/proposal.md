## Why

Games with low-volume audio (quiet scenes, ambience, heavily compressed mixes) trigger false `sound issue` alerts because the Pearson correlation check is strict about how closely the OBS capture must match the gaming PC audio. In practice, if there is sound on the gaming PC and some sound is also captured in OBS, that is good enough. Users need a tunable tolerance and a simple mode that only flags a truly silent capture.

## What Changes

- Add server and client settings + UI slider **"Audio match tolerance (dB)"**: the maximum RMS difference between the gaming PC signal and the OBS capture signal at which the capture is still considered matching (no sound issue). Default is 20 dB (range 0–40 dB).
- Add server and client settings + UI switch **"Simple audio issue detection"**: when enabled, the Pearson/variance-difference analysis is skipped entirely and a sound issue is reported only when gaming PC audio is active but OBS capture is silent (the existing conditional silence detector).
- UI placement: both controls go directly below the OBS Game Audio Capture Source dropdown in both Server settings and Client settings (Single PC mode), switch first, slider below.
- Identical linked behavior across Server and Client: when switch is on, slider is disabled (grayed) and visually moved to maximum, while the persisted tolerance value is unchanged and restored when switch is turned off.
- Shared code logic: detection logic lives in `StreamHelper.Shared` (`AudioCorrelationEngine`, `PearsonCorrelationEngine`), and UI interaction/linking logic is shared between Client and Server forms. Settings are applied live without restart.
- Update README (audio section, server settings, and client single-PC mode settings descriptions).

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `audio-telemetry-correlation`: the cross-correlation mismatch requirement gains a configurable RMS-difference tolerance, and a new simple detection mode requirement is added.
- `server-tray-app`: the server settings window gains the new switch and slider with the specified ordering and linked disabled/max-position behavior, persisted in settings.
- `client-tray-app`: the client settings window in Single PC mode gains the new switch and slider with the specified ordering, linked behavior, and live engine updates, persisted in settings.

## Impact

- `src/StreamHelper.Server/Config/ServerSettings.cs` (two new persisted properties)
- `src/StreamHelper.Server/UI/ServerSettingsForm.cs` (new controls, layout shift, callbacks)
- `src/StreamHelper.Server/ServerTrayApplicationContext.cs` (wire settings into engine, live updates)
- `src/StreamHelper.Client/Config/ClientSettings.cs` (two new persisted properties for Single PC mode)
- `src/StreamHelper.Client/UI/ClientSettingsForm.cs` (new controls in Single PC panel, layout shift, callbacks)
- `src/StreamHelper.Client/ClientTrayApplicationContext.cs` (wire settings into client engine, live updates)
- `src/StreamHelper.Shared/` (detection logic & shared UI controller logic)
- Tests in `tests/StreamHelper.Tests`, and `README.md`
