## 1. Core Audio Engine & Detection Logic

- [x] 1.1 Update `ConditionalSilenceDetector` default thresholds to client active `> -48.0 dBFS` and OBS silence `< -60.0 dBFS` to restore the hysteresis gap, and verify with unit tests
- [x] 1.2 Update `AudioCorrelationEngine` to accept a configurable `lagWindowSeconds` (0.0s to 3.0s) and search for non-silent OBS audio across $[T, T + \text{lagWindow}]$ in both Single-PC and Dual-PC modes
- [x] 1.3 Update `PearsonCorrelationEngine` to scale `maxLagSteps` to match the configured lag window and decouple full mode detection so verified correlation suppresses spurious silence alerts
- [x] 1.4 Call `_correlationEngine.Reset()` on OBS audio capture device changes in `ServerTrayApplicationContext` and `ClientTrayApplicationContext` to immediately flush historical meters and alerts

## 2. Configuration & Controller Updates

- [x] 2.1 Add `AudioLagWindowSeconds` (default `1.0`, range `0.0` to `3.0`) to `ServerSettings` and `ClientSettings` with JSON persistence and validation
- [x] 2.2 Extend `AudioDetectionSettingsController` to bind and control the audio lag compensation slider (0.0s to 3.0s, step 0.1s), keeping it active in both Simple and Full detection modes

## 3. UI Implementation

- [x] 3.1 Update `ServerSettingsForm` to render the audio lag compensation slider directly below `Simple audio issue detection`, expand client height, and preserve 20px right margin
- [x] 3.2 Update `ClientSettingsForm` Single-PC panel to render the audio lag compensation slider directly below `Simple audio issue detection`, adjust panel layout, and preserve 20px right margin

## 4. Testing & Verification

- [x] 4.1 Add unit tests in `AudioTelemetryAndCorrelationTests` verifying lag compensation with 500ms–1500ms delay and low-volume scenes (-46 dBFS client vs -50 dBFS OBS) without false alerts
- [x] 4.2 Add unit tests for `AudioDetectionSettingsController` and settings forms verifying lag slider interactions and settings persistence
- [x] 4.3 Run the complete test suite with `dotnet test` and verify all tests pass
