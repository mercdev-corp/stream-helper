## 1. Settings

- [x] 1.1 Add `AudioMatchToleranceDb` (default 20, clamped 0–40 on load) and `SimpleAudioIssueDetection` (default false) to `ServerSettings`, including debug log lines
- [x] 1.2 Add `AudioMatchToleranceDb` (default 20, clamped 0–40 on load) and `SimpleAudioIssueDetection` (default false) to `ClientSettings` for Single PC mode, including debug log lines

## 2. Shared detection & UI logic

- [x] 2.1 Add `ToleranceDb` and mean-absolute-RMS-difference computation (at best lag) to `PearsonCorrelationEngine`; treat mismatch only when `r < threshold` and difference > tolerance
- [x] 2.2 Add thread-safe live configuration (tolerance + simple mode) to `AudioCorrelationEngine`; in simple mode skip/reset Pearson engine and evaluate only the silence detector
- [x] 2.3 Log mode/tolerance changes via `AppLogger`
- [x] 2.4 Implement shared UI helper/controller logic in `StreamHelper.Shared` or shared UI components for switch/slider linkage and state synchronization

## 3. Server & Client wiring

- [x] 3.1 Apply saved settings to the engine at startup in `ServerTrayApplicationContext`
- [x] 3.2 Add callback to `ServerSettingsForm` and handle it in `ServerTrayApplicationContext` to update the server engine live
- [x] 3.3 Apply saved settings to the client `AudioCorrelationEngine` at startup and mode switch in `ClientTrayApplicationContext`
- [x] 3.4 Add callback to `ClientSettingsForm` and handle it in `ClientTrayApplicationContext` to update the client engine live in Single PC mode

## 4. Settings UI

- [x] 4.1 Add `Simple audio issue detection` checkbox and tolerance `TrackBar` with value label below the OBS audio dropdown in `ServerSettingsForm` (switch above slider); shift lower controls and enlarge form, keeping 20 px right margin
- [x] 4.2 Add `Simple audio issue detection` checkbox and tolerance `TrackBar` with value label below the OBS audio dropdown in `ClientSettingsForm` within `_pnlSinglePc` (switch above slider); shift lower controls and adjust form layout, keeping 20 px right margin
- [x] 4.3 Connect shared linked behavior on both forms: when switch on, slider disabled and shown at maximum without altering saved value; restore saved value when off; guard programmatic changes with `_isUpdatingControls`
- [x] 4.4 Persist changes immediately and expose controls as `internal` properties for tests on both forms

## 5. Tests

- [x] 5.1 Engine tests: low correlation within tolerance does not alert; beyond tolerance alerts; simple mode alerts only on active client + silent OBS; mode toggling resets state
- [x] 5.2 Settings tests: defaults, JSON round-trip, clamping for both `ServerSettings` and `ClientSettings`
- [x] 5.3 Form tests: control order/placement, disabled + max slider in simple mode with saved value preserved and restored, and 20 px right margin tests for both `ServerSettingsForm` and `ClientSettingsForm` (Single PC mode)
- [x] 5.4 Run the full test suite

## 6. Documentation

- [x] 6.1 Update `README.md` (Audio correlation section, Server settings, and Client Single-PC settings) describing the tolerance slider and simple detection mode
