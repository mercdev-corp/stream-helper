## Why

Real-world gameplay in both Simple and Full audio issue detection modes (such as streaming Escape from Tarkov in Dual-PC or Single-PC mode with an OBS Limiter filter and gain offset) triggers false positive `OBS sound capture issue` alerts right after loud combat and latches the alert indefinitely. At the same time, genuine continuous audio capture failures lasting longer than 1 second on gaming PC sounds between `-60 dBFS` and `-48 dBFS` (such as footsteps at `-48` to `-55 dBFS` or UI sounds at `-56 dBFS`) were ignored because the detector required sounds louder than `-48 dBFS` for 3.0 seconds (30 readings), and Pearson correlation allowed silent OBS samples to be masked by `ToleranceDb` (20–30 dB). Furthermore, cross-PC clock skew (1–3s), unbuffered forward-only lag lookups, early returns in `ConditionalSilenceDetector` when client audio drops below active thresholds while OBS is still capturing audio, non-resetting detector state on mode switches, and Pearson correlation distortion from OBS brickwall limiters and positive gain offsets prevented accurate detection and recovery.

## What Changes

- **1.0-Second Continuous Issue Detection for Sounds > -60 dBFS**: Lower the gaming PC active audio threshold from `-48.0 dBFS` to `-60.0 dBFS` and reduce the continuous detection window from 3.0 seconds (30 readings) to 1.0 second (10 readings at 100ms) in both `ConditionalSilenceDetector` and `PearsonCorrelationEngine`. Replace the strict consecutive run reset with a `-2` decay per silent reading (`<= -60.0 dBFS`), ensuring continuous footsteps with brief 100ms micro-gaps still accumulate and trigger after 1 second of uncaptured audio while single spikes after silence do not re-latch active issue confirmation.
- **Continuous Client-Server Clock Offset Alignment & Unified Shared Pipeline**: Continuously estimate and apply the Client-to-Server clock offset on incoming Dual-PC UDP telemetry batches so client sample timestamps are accurately anchored to the Server's timeline regardless of cross-PC clock drift, and unify `ProcessClientReading` (Single-PC) and `ProcessTelemetryPacket` (Dual-PC) onto a single shared evaluation pipeline in `StreamHelper.Shared`.
- **Bidirectional & Causal Lag-Window Resolution**: Evaluate OBS meter history across a bidirectional window (`[sampleTime - leadAllowance, sampleTime + lagWindow]`) and defer final silence-failure counting for in-flight client samples until `sampleTime + lagWindow` has elapsed on the local clock (or resolve immediately as soon as non-silent OBS audio arrives). Remove the artificial `maxLagFromSilence` clamp on `_lagWindowSeconds`.
- **Immediate OBS Audio Recovery & Non-Sticky Silence Detection**: Update `ConditionalSilenceDetector` and `AudioCorrelationEngine.IngestObsAudioMeter` so that any valid OBS audio (`>= -60 dBFS`) immediately resets the silence counter and clears active silence alerts even when Gaming PC audio is currently quiet (`<= -60 dBFS`). Prevent single-reading spikes after quiet periods from refreshing `LastAudioIssueTime` without a confirmed issue window.
- **Limiter-, Gain-Offset-, and Silence-Floor-Resilient Full Mode (`PearsonCorrelationEngine`)**: Clamp digital silence floor readings at `-60.0 dBFS` for correlation calculations. On active client samples (`> -60.0 dBFS`), evaluate positive gain (`OBS >= Client`) and OBS limiter ceilings (`client >= -25 dBFS && obs >= -15 dBFS`) per sample, and treat silent OBS readings (`< -60.0 dBFS`) as full signal loss (`Math.Max(c - o, c - (-100.0))`) so uncaptured footsteps and quiet game sounds are never masked by `ToleranceDb`.
- **Clean State Reset on Mode Toggles & Pause/Disable**: Reset both `ConditionalSilenceDetector`, `PearsonCorrelationEngine`, pending sample buffers, and `LastAudioIssueTime` when toggling between Simple and Full detection modes or when pausing/disabling game audio monitoring in both Single-PC and Dual-PC modes.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `audio-telemetry-correlation`: Update requirements for 1.0-second (10-reading) continuous audio issue detection on sounds `> -60.0 dBFS`, full signal loss evaluation for silent OBS readings in Pearson correlation, cross-PC clock offset alignment, causal/bidirectional lag-window evaluation, immediate silence-detector recovery upon OBS audio detection regardless of client level, limiter/gain-offset handling in Pearson correlation, and complete state resets on mode switches in both Single-PC and Dual-PC modes.

## Impact

- **`StreamHelper.Shared`**:
  - `Audio/AudioCorrelationEngine.cs` (clock offset estimation, unified Single-PC/Dual-PC sample pipeline, causal lag window resolution, removal of lag window clamp, immediate recovery on OBS audio, full state reset on mode toggle).
  - `Audio/ConditionalSilenceDetector.cs` (default `-60.0 dBFS` active threshold and 10 readings required; prioritize `obsRmsDbfs >= ObsSilenceThresholdDbfs` recovery before client quiet check; `-2` decay without single-spike re-latching).
  - `Audio/PearsonCorrelationEngine.cs` (default 10 readings required; active threshold `> -60.0 dBFS`; full signal loss for silent OBS samples; directional attenuation tolerance and OBS limiter ceiling resilience).
- **`StreamHelper.Client` & `StreamHelper.Server`**:
  - `ClientTrayApplicationContext.cs` and `ServerTrayApplicationContext.cs` (ensure correlation engine resets cleanly when pausing or disabling game audio monitoring in Single-PC and Dual-PC modes).
- **`StreamHelper.Tests`**:
  - `AudioTelemetryAndCorrelationTests.cs` (unit and integration tests covering footsteps at `-48` to `-55 dBFS` triggering after 1.0s when OBS is silent, Escape from Tarkov gunshot/limiter/gain-offset profiles, cross-PC clock drift, immediate post-fight recovery, and Single-PC/Dual-PC parity).
- **Documentation**:
  - `README.md` (update business logic documentation for `-60.0 dBFS` active audio threshold, 1.0-second continuous issue detection, clock offset alignment, causal lag compensation, limiter/gain-offset handling, and recovery behavior per `AGENTS.md`).
