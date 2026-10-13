## Why

When streaming PC audio is routed via higher-latency duplication paths (such as Realtek Stereo Mix "Listen" to an HDMI capture card like the Elgato 4K X), OBS audio capture experiences an inherent 400ms to 1500ms delay compared to the gaming PC loopback. The current audio issue detection logic evaluates silence with 0ms delay tolerance and uses identical -48.0 dBFS thresholds for both client and OBS silence. This causes frequent false positive "OBS sound capture issue" alerts during streams, especially during quiet game scenes where slight analog/digital attenuation drops OBS audio below -48.0 dBFS while client audio remains active.

## What Changes

- Add a user-configurable **Audio lag compensation window** slider (0.0s to 3.0s, default 1.0s, step 0.1s or 0.2s) in Server Settings (Dual-PC mode) and Client Settings (Single-PC mode).
- Enhance `ConditionalSilenceDetector` and `AudioCorrelationEngine` to search for corresponding OBS audio activity across the configured lag window $[T, T + \text{lagWindow}]$, rather than matching only at $T = 0$.
- Restore the silence detection threshold hysteresis gap: maintain client active detection at $> -48.0$ dBFS while setting the OBS silence threshold to $< -60.0$ dBFS (or user-tuned margin) so low-volume game audio with slight attenuation is not falsely flagged as dead silence.
- Expand `PearsonCorrelationEngine` maximum lag steps dynamically to cover the configured lag window (up to 3.0s / 30 steps), and decouple full-mode detection so that verified Pearson correlation ($r \ge 0.5$ or within tolerance) suppresses spurious instantaneous silence alerts.
- Reset `AudioCorrelationEngine` history and alert state immediately whenever the configured OBS Game Audio Capture Source changes.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `audio-telemetry-correlation`: Add requirement for audio lag window search during silence detection, restore threshold hysteresis between client active and OBS silence, dynamically size Pearson correlation lag bounds to the configured window, and reset correlation state on OBS audio input change.
- `server-tray-app`: Add requirement for configurable audio lag compensation slider in Server Settings form under the OBS Game Audio Capture Source selector.
- `client-tray-app`: Add requirement for configurable audio lag compensation slider in Client Settings form for Single-PC mode under the OBS Game Audio Source selector.

## Impact

- **UI / Settings**: Server Settings form and Client Settings form (Single-PC tab) gain an audio lag compensation slider with real-time value display and settings persistence.
- **Engines**: `AudioCorrelationEngine`, `ConditionalSilenceDetector`, and `PearsonCorrelationEngine` updated with lag window search, hysteresis gap, and reset behavior.
- **Config**: `ServerSettings` and `ClientSettings` persist `AudioLagWindowSeconds` (or `AudioLagCompensationMs`).
- **Tests**: Unit tests for audio correlation, silence detection with latency, and settings UI controllers updated.
