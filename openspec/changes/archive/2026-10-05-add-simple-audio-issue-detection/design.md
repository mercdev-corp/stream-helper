## Context

`AudioCorrelationEngine` (Shared) combines `ConditionalSilenceDetector` (client > -45 dBFS, OBS < -60 dBFS for 3 s) and `PearsonCorrelationEngine` (client variance > 5 dB, max lagged Pearson r < 0.5 for 50 samples). It is created by `ServerTrayApplicationContext` and also by the Single-PC client in `ClientTrayApplicationContext`. Server settings live in `ServerSettings` and Client settings live in `ClientSettings` (JSON). `ServerSettingsForm` and `ClientSettingsForm` both manage settings via WinForms dialogs. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Configurable tolerance that makes quiet-but-present OBS audio count as matching.
- Simple mode bypassing Pearson/tolerance logic.
- Single PC mode UI updates: provide the same audio issue detection experience as Dual PC mode using only the Client app, backed by shared code logic.
- Shared UI helper/controller and configuration logic so both Dual PC (Server) and Single PC (Client) share identical control behavior, persistence, and live engine updates.

**Non-Goals:**
- Changing the silence detector thresholds or the Pearson parameters.

## Decisions

1. **Tolerance semantics**: after computing the best-lag Pearson result, also compute the mean absolute RMS difference (dB) between the client and OBS windows at the best lag. A correlation mismatch counts only if `r < 0.5` **and** mean abs difference `> tolerance`. Range 0–40 dB, default 20 (setting `AudioMatchToleranceDb`). Alternative considered: replacing correlation with a pure level comparison — rejected as it would lose detection of wrong-source capture at similar volume. Note: with tolerance 0 behavior is effectively the existing correlation-only logic.
2. **Simple mode**: `AudioCorrelationEngine.SimpleDetection` flag; when true, `ProcessClientReading`/`ProcessTelemetryPacket` skip feeding the Pearson engine (and reset it), and `EvaluateSoundIssue` uses only the silence detector result. Setting `SimpleAudioIssueDetection`, default false.
3. **Engine API**: add thread-safe `Configure(double toleranceDb, bool simpleMode)` (or two properties) guarded by the engine lock; `PearsonCorrelationEngine` gets a settable `ToleranceDb`. Applied live; changing mode resets the Pearson state to avoid stale alerts.
4. **Settings wiring**: Both `ServerSettings` and `ClientSettings` store `AudioMatchToleranceDb` (default 20, 0–40) and `SimpleAudioIssueDetection` (default false). `ServerSettingsForm` and `ClientSettingsForm` invoke callbacks or update settings; both `ServerTrayApplicationContext` and `ClientTrayApplicationContext` apply initial values from settings at startup and configure their respective `AudioCorrelationEngine` live upon changes. Persist via existing `Save()`.
5. **UI & Shared Logic**:
   - Shared controller/helper logic: extract or share the linked switch-and-slider behavior (when switch on, slider disabled and visually set to max; when switch off, slider restored from settings; guarded against recursive saves) so Server and Client forms use identical interaction logic.
   - Server: add `CheckBox` "Simple audio issue detection" and labelled `TrackBar` directly below the OBS audio capture dropdown, shifting lower controls and resizing form while keeping 20 px right margin.
   - Client (Single PC): add identical `CheckBox` and labelled `TrackBar` directly below `_cboObsAudio` inside `_pnlSinglePc`, shifting controls below it and expanding panel/form height while maintaining 20 px right margin. Controls are only active and shown in Single PC mode.
6. **README**: update the Audio correlation section and describe both new settings and the simple mode in Server and Client Single-PC documentation.

## Risks / Trade-offs

- [Tolerance default of 20 dB may hide real partial failures] → Simple mode and slider let users tune; 0 restores strict behavior.
- [Layout shifts could break existing form tests that check positions] → Update tests and verify the 20 px right margin test on both Server and Client forms.
- [Mean abs RMS difference depends on OBS gain staging] → Documented in README; slider is user adjustable.
- [Reconfiguring while telemetry arrives on another thread] → Guard with engine lock.
