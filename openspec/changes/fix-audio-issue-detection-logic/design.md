## Context

See [proposal.md](./proposal.md) for the problem statement and motivation.

Currently, `AudioCorrelationEngine`, `ConditionalSilenceDetector`, and `PearsonCorrelationEngine` in `StreamHelper.Shared/Audio/` are shared by both `StreamHelper.Server` (Dual-PC mode via UDP telemetry packets) and `StreamHelper.Client` (Single-PC mode via direct in-memory readings). However, several architectural flaws interact during real-world gameplay (e.g., Escape from Tarkov with a `-8.4 dB` OBS Limiter filter and a `+12` to `+20 dB` hotter OBS input):

1. **Uncompensated Cross-PC Clock Offset (`±3000ms`)**: `ProcessTelemetryPacket` trusts raw `packet.TimestampUnixMs` whenever `Math.Abs(serverNowMs - packet.TimestampUnixMs) <= 3000`, shifting client sample timestamps by up to 3 seconds relative to `_obsMeterHistory` (which uses the Server clock).
2. **Non-Causal Forward Lag Lookup**: `ResolveObsRmsForSilenceDetection` only searches forward `(sampleTimeMs, sampleTimeMs + lagWindowMs]` at the exact instant a client reading arrives, when future OBS readings have not yet arrived in real time and slightly earlier OBS readings (`[sampleTimeMs - leadAllowance, sampleTimeMs)`) are ignored.
3. **`-48 dBFS` Active Threshold & 3-Second Window Ignored Real-World Sounds**: `ConditionalSilenceDetector` required `clientRmsDbfs > -48.0 dBFS` for 30 consecutive readings (3.0 seconds). Quiet in-game sounds such as running footsteps (`-48` to `-55 dBFS`) or inventory UI (`-56 dBFS`) were treated as quiet/silent, and any single 100ms dip `<= -48 dBFS` completely reset the consecutive run counter.
4. **`ToleranceDb` Masked Silent OBS Audio in Full Mode**: `PearsonCorrelationEngine.CalculateLaggedDifference` evaluated active samples `> -48.0 dBFS`, clamped silent OBS readings (`< -60 dBFS`) to `-60.0 dBFS`, and allowed `ToleranceDb` (20–30 dB) to mask silent OBS capture whenever Gaming PC audio was below `-30` to `-40 dBFS`. It also required 30 readings (3.0s).
5. **Blocked Recovery in `IngestObsAudioMeter` & `EvaluateSoundIssue`**: `IngestObsAudioMeter` did not reset `_silenceDetector`, and `EvaluateSoundIssue` checked `if (matchingObsAudio && !issueAlertActive)`, which never executed once `issueAlertActive` became `true`.
6. **Sticky Counter & `RetryTimeout` Refresh Trap**: Because `_consecutiveActiveClientSilentObsCount` decayed by only `-1` per quiet reading, brief sounds bumped the counter back to max and refreshed `_lastAudioIssueTime = DateTime.UtcNow`, preventing `RetryTimeout` expiration until the Client was paused.
7. **Pearson Correlation Distortion Under Limiters, Gain Offsets (`OBS > Client`), and `-100 dBFS` Silence Floors**:
   - Digital silence ticks (`-100 dBFS`) on the PC vs OBS's 300ms meter decay (`-35..-50 dBFS`) and `+20 dB` hotter OBS gain inflated symmetric `Math.Abs(client - obs)` past `20 dB`.
   - When loud gunshots (`-14 dB` on PC) hit an OBS brickwall Limiter filter (`-8.4 dB`), OBS levels were clamped flat (`denomY -> 0`), driving Pearson $r < 0.5$.
   - Toggling `simpleMode` in `Configure` only reset `_correlationEngine`, leaving `_silenceDetector` and `_lastAudioIssueTime` latched.

## Goals / Non-Goals

**Goals:**
- Detect continuous audio capture failures lasting >= 1 second (10 readings at 100ms) whenever original Gaming PC audio is louder than `-60.0 dBFS` (including footsteps at `-48` to `-55 dBFS` and UI sounds at `-56 dBFS`) when OBS fails to capture them (`< -60.0 dBFS`), across both Simple and Full modes.
- Unify Single-PC (`ProcessClientReading`) and Dual-PC (`ProcessTelemetryPacket`) sample processing in `StreamHelper.Shared/Audio/AudioCorrelationEngine.cs` so every fix applies identically to both modes.
- Continuously estimate and apply the Client-to-Server clock offset in Dual-PC mode without additional network traffic.
- Support real-time causal lag compensation (`[sampleTime - leadAllowance, sampleTime + lagWindow]`) so delayed OBS audio never triggers false alerts while still remaining deterministic in synchronous unit tests, decoupling the lag window from the detection window.
- Guarantee immediate alert recovery whenever OBS captures non-silent audio (`>= -60 dBFS`), even when the Gaming PC is below `-60 dBFS`.
- Make Full mode (`PearsonCorrelationEngine`) resilient to OBS Limiter/Compressor filters, positive OBS gain offsets (`OBS > Client`), and `-100 dBFS` digital silence floors while evaluating silent OBS readings as full signal loss so quiet sounds like footsteps are never masked by `ToleranceDb`.

**Non-Goals:**
- Changing the UDP binary wire protocol (`AudioTelemetryPacket` layout remains unchanged for full backward compatibility).
- Adding new user-facing settings controls or manual calibration steps.

## Decisions

### Decision 1: Continuous Clock Offset Estimation & Unified Shared Sample Pipeline
- **Choice**: In `AudioCorrelationEngine.ProcessTelemetryPacket`, compute the instantaneous packet clock delta `rawOffsetMs = serverNowMs - packet.TimestampUnixMs`. Maintain a smoothed `_clockOffsetMs` (initialized on the first packet or after a gap `> 10s`, and updated via an exponential moving average `alpha = 0.2` when within reasonable drift bounds, or snapped if drift jumps). Map each reading `i` in the batch to `alignedSampleTimeMs = (packet.TimestampUnixMs + _clockOffsetMs) - (count - 1 - i) * 100`, clamped so it does not exceed `serverNowMs` in real-time streaming. If `_obsMeterHistory` contains synthetic test timestamps far from `serverNowMs` (used by deterministic unit tests that pass explicit `packet.TimestampUnixMs`), preserve direct alignment with `packet.TimestampUnixMs`. Route both `ProcessClientReading` (Single-PC) and `ProcessTelemetryPacket` (Dual-PC) through the same internal sample evaluation method.
- **Rationale**: Eliminates the `±3000ms` uncompensated clock-skew bug in Dual-PC mode while preserving 100ms inter-sample spacing and sharing 100% of downstream logic with Single-PC mode.
- **Alternatives Considered**: Adding a dedicated NTP-style ping/pong UDP packet between Client and Server (rejected: unnecessary protocol complexity when 1 Hz telemetry packets already carry `TimestampUnixMs` over a `< 1ms` local LAN).

### Decision 2: Bidirectional & Causal Lag-Window Resolution with Pending Buffer
- **Choice**:
  1. When resolving OBS RMS for silence detection at `sampleTimeMs`, search `_obsMeterHistory` across `[sampleTimeMs - LeadAllowanceMs, sampleTimeMs + lagWindowMs]` (where `LeadAllowanceMs = Math.Min(500, lagWindowMs)` covers batching/jitter lead, and `lagWindowMs` covers capture card / OBS pipeline delay). Do not assume `_obsMeterHistory` is strictly monotonic when scanning.
  2. If any OBS meter reading in that window is `>= ObsSilenceThresholdDbfs` (`-60.0 dBFS`), immediately resolve the sample as matched.
  3. If OBS is `< -60.0 dBFS` so far, check whether the forward horizon `sampleTimeMs + lagWindowMs` has already been observed (either `lagWindowMs == 0`, `_obsMeterHistory` already has a timestamp `>= sampleTimeMs + lagWindowMs`, or `nowMs >= sampleTimeMs + lagWindowMs`, or OBS meter stream is stale/empty). If not yet elapsed, hold the active client reading in a bounded `_pendingSilenceReadings` queue.
  4. Whenever `IngestObsAudioMeter` receives a new OBS reading:
     - If `rmsDbfs >= -60.0 dBFS`, clear `_pendingSilenceReadings`, reset `_silenceDetector`, and immediately evaluate/clear any active silence alert.
     - Otherwise, flush any pending readings whose `sampleTimeMs + lagWindowMs <= obsTimestampMs` into `_silenceDetector`.
  5. Decouple `_lagWindowSeconds` from the silence detector required readings count (remove `maxLagFromSilence` clamp).
- **Rationale**: Fixes the causal paradox where `ResolveObsRmsForSilenceDetection` looked into the future `(T, T + lagWindow]` before OBS had emitted those meters, while keeping existing synchronous unit tests (which pre-populate `_obsMeterHistory` up to `T + delayMs`) working without delay.

### Decision 3: Immediate OBS Recovery & -60 dBFS / 1.0s Silence Detector
- **Choice**:
  - In `ConditionalSilenceDetector.ProcessReading(clientRmsDbfs, obsRmsDbfs)`:
    1. Set default `clientActiveThresholdDbfs = -60.0`, `obsSilenceThresholdDbfs = -60.0`, and `consecutiveReadingsRequired = 10` (1.0 second).
    2. Check `if (obsRmsDbfs >= _obsSilenceThresholdDbfs)` **first** (before checking `clientRmsDbfs <= _clientActiveThresholdDbfs`). Whenever OBS has non-silent audio (`>= -60.0 dBFS`), set `_consecutiveActiveClientSilentObsCount = 0` and `_isAlertActive = false` immediately.
    3. When `clientRmsDbfs <= _clientActiveThresholdDbfs` (silent/quiet game audio `<= -60.0 dBFS` while OBS is `< -60.0 dBFS`), decay `_consecutiveActiveClientSilentObsCount` by `-2` per reading and do **not** report `IsActivelyConfirmingIssue = true`.
    4. By decaying `-2` without resetting an artificial strict consecutive run, continuous audio with brief 100ms micro-gaps (e.g. footsteps) accumulates and triggers once 1 second of net uncaptured active audio occurs, while isolated single spikes after quiet gameplay cannot re-latch `IsActivelyConfirmingIssue` or perpetually reset `_lastAudioIssueTime`.
  - In `AudioCorrelationEngine.IngestObsAudioMeter` and `EvaluateSoundIssue`:
    - When `rmsDbfs >= _silenceDetector.ObsSilenceThresholdDbfs`, reset `_silenceDetector` and clear `_pendingSilenceReadings`.
    - In `EvaluateSoundIssue`, if `matchingObsAudio && !issueAlertActive`, immediately clear `_lastAudioIssueTime = null`, disarm `_expirationTimer`, and set `HasSoundIssue = false`.

### Decision 4: Silence-Floor Clamping, Directional Tolerance, and Full Signal Loss in `PearsonCorrelationEngine`
- **Choice**:
  1. Set default `consecutiveLowCorrCountRequired = 10` (1.0 second) and `minRequired = Math.Max(15, _maxLagSteps + 5)`.
  2. **Silence-Floor Clamping (`-60.0 dBFS`)**: Clamp samples at `-60.0 dBFS` (`Math.Max(sample, -60.0)`) when computing lagged Pearson correlation (`CalculateLaggedCorrelation` inside `AddSamples`) so `-100.0 dBFS` digital silence drops do not dominate variance and correlation.
  3. **Directional Active Attenuation & Limiter Ceiling Check**:
     - Over the best-lag aligned window, evaluate active client samples where `c > -60.0 dBFS`:
       - If OBS is silent on that sample (`o < -60.0 dBFS`), treat it as complete signal loss evaluated against the `-100.0 dBFS` floor (`Math.Max(c - o, c - (-100.0))`). This ensures silent OBS capture for quiet sounds (like footsteps at `-48` to `-55 dBFS`) yields differences > 40 dB, never masked by `ToleranceDb` (20–30 dB).
       - If OBS is actively capturing audio (`o >= -60.0 dBFS`), measure attenuation as how much **quieter** OBS is than the Client (`Math.Max(0.0, c - o)`). If OBS is louder (`o >= c`, e.g. `+12..+20 dB` hotter OBS input) or if OBS is compressed against a high limiter ceiling (`c >= -25.0 dBFS && o >= -15.0 dBFS`), treat the effective attenuation difference as within tolerance (`0.0 <= ToleranceDb`).
  4. **Full Mode Silence Suppression Guard**: In `AudioCorrelationEngine.EvaluateSoundIssue`, Full mode only suppresses `silenceIssue` when `_correlationEngine.HasValidCalculation` is true **and** OBS has non-silent audio (`obsHasAudio`).

### Decision 5: Complete State Reset on Mode Toggles and Pause/Disable
- **Choice**:
  - In `AudioCorrelationEngine.Configure`, when `_simpleDetection != simpleMode`, reset `_silenceDetector`, `_correlationEngine`, `_pendingSilenceReadings`, and `_lastAudioIssueTime`, and disarm `_expirationTimer`.
  - In `ClientTrayApplicationContext` (Single-PC pause or disabling Game Audio Monitoring) and `ServerTrayApplicationContext` (Server pause), call `_correlationEngine.Reset()` so stale sound issue flags do not linger in memory.

## Risks / Trade-offs

- **[Risk] Lowering active threshold to `-60.0 dBFS` could trigger on background noise** -> **Mitigation**: Audio loopback on the gaming PC produces digital silence (< -90 dBFS) when games are quiet; `-60.0 dBFS` cleanly isolates audible game sounds (footsteps, ambient effects, dialogue, UI) while ignoring digital noise floors. Furthermore, alerts only trigger when OBS is simultaneously below `-60.0 dBFS` (capturing nothing).
- **[Risk] Reducing confirmation duration to 1.0 second (10 readings) could trigger during rapid transient spikes** -> **Mitigation**: 10 readings at 100ms require 1 full second of sustained uncaptured active sound, preventing transient 100–200ms audio drops from firing false alerts, and any arrival of valid OBS audio resets the counter in 0ms.
- **[Risk] Allowing `OBS >= Client` (positive gain offset) could mask an issue where OBS captures a loud unrelated constant tone** -> **Mitigation**: Unrelated constant tones or uncorrelated background music at normal levels (`-30 dBFS`) with `toleranceDb: 0` still fail correlation (`r < 0.5`), and we preserve `Math.Abs(client - obs)` when `ToleranceDb == 0` unless signals correlate (`r >= 0.5`) or match limiter/gain-offset envelopes within `ToleranceDb > 0`.
