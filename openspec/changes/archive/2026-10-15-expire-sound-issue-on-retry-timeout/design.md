## Context

In Stream Helper, `AudioCorrelationEngine` (in `StreamHelper.Shared.Audio`) detects audio capture failures between the gaming PC output and OBS Studio capture. It coordinates `ConditionalSilenceDetector` and `PearsonCorrelationEngine`.

When sound routing from the source PC to OBS fails and the source emits a transient sound, the engine flags a `sound issue`. Currently, if the source becomes silent afterward, no further audio is delivered to OBS. Because recovery currently relies on observing matching sound in OBS, the `sound issue` alert remains active indefinitely during silence. Furthermore, `WASAPI Loopback Capture` does not produce buffer packets when no client application is rendering sound, meaning the correlation engine receives no tick to re-evaluate or decay its state during silence.

Settings include a user-configurable `RetryTimeout` (default 5s, range 1-300s) used across connection checks and reconnection timers, but previously not used for sound issue alert lifetime.

## Goals / Non-Goals

**Goals:**
- Update `AudioCorrelationEngine` in shared code to record `LastAudioIssueFacedTime` whenever an audio issue condition is actively detected.
- Limit the active duration of `HasSoundIssue` to the configured `RetryTimeout` period unless re-confirmed by ongoing issue detection.
- Provide a timer-driven expiration mechanism inside `AudioCorrelationEngine` that automatically clears the `sound issue` alert when source silence halts incoming telemetry.
- Maintain immediate alert clearance when valid audio is positively captured in OBS or when explicit suppression rules apply (OBS muted, disconnected, scene change).
- Support live application of `RetryTimeout` updates to `AudioCorrelationEngine` without requiring an application restart.
- Update `README.md` and add agent requirements in `AGENTS.md` requiring agents to keep `README.md` up to date when business logic changes.

**Non-Goals:**
- Modifying the underlying 12 dB hysteresis gap or 3-second consecutive active requirement for initial silence detection.
- Modifying reconnection timeouts for OBS WebSocket, audio endpoint polling, or multicast network discovery.
- Adding new configuration UI controls (reusing the existing `Retry timeout` setting).

## Decisions

### 1. In-Engine Expiration Timer (`System.Threading.Timer`)
- **Decision**: Embed a `System.Threading.Timer` inside `AudioCorrelationEngine` to trigger auto-expiration when the retry timeout duration elapses.
- **Rationale**: When game audio stops, `WasapiLoopbackCapture` produces 0 buffer packets. Without an internal timer, no evaluation loop runs to check if the timeout has elapsed.
- **Alternatives Considered**:
  - *Heartbeat pump in Server/Client tray contexts*: Would require duplicate periodic polling loops in both server and client hosts. Putting the timer inside `AudioCorrelationEngine` encapsulates the behavior cleanly in shared code for both Single-PC and Dual-PC modes.

### 2. State Evaluation and Timestamp Renewal
- **Decision**:
  - When a sound issue condition is flagged in `EvaluateSoundIssue()`, update `_lastAudioIssueTime = DateTime.UtcNow` and arm the timer for `_retryTimeoutSeconds`.
  - If subsequent incoming readings continue to confirm the issue, update `_lastAudioIssueTime` and reset the timer.
  - If OBS captures valid audio or alert suppression activates, disarm the timer and set `HasSoundIssue = false` immediately.
  - When the expiration timer fires, evaluate whether `(DateTime.UtcNow - _lastAudioIssueTime) >= TimeSpan.FromSeconds(_retryTimeoutSeconds)`. If so, transition `HasSoundIssue` to `false` and fire `SoundIssueChanged`.

### 3. Thread Safety and Resource Management
- **Decision**: Protect all state mutations (`_lastAudioIssueTime`, `_retryTimeoutSeconds`, timer scheduling) under `AudioCorrelationEngine`'s existing `_lock`. Fire `SoundIssueChanged` outside the lock to prevent deadlocks.
- Implement `IDisposable` (or timer disarm on `Reset()`) in `AudioCorrelationEngine` to cleanly dispose the timer on shutdown or reconfiguration.

### 4. Integration with Server and Client Settings
- **Decision**:
  - Add `SetRetryTimeout(int retryTimeoutSeconds)` to `AudioCorrelationEngine` and accept it in constructor / `Configure(...)`.
  - In `ServerTrayApplicationContext`: pass `_settings.RetryTimeout` upon initialization and in `onTimeoutChanged` callback.
  - In `ClientTrayApplicationContext`: pass `_settings.RetryTimeout` in Single-PC mode upon initialization and in `SetRetryTimeout(int timeout)`.

### 5. Documentation & Agent Instructions
- **Decision**:
  - Update `README.md` under Behavior / Audio capture monitoring and Retry timeout sections to specify that sound issue alerts automatically expire after the retry timeout if not re-confirmed during silence.
  - Update `AGENTS.md` to add a strict rule requiring agents to update `README.md` whenever business logic changes.

## Risks / Trade-offs

- **[Risk]** Timer callback fires concurrently with a new telemetry packet that re-confirms an issue.
  - **Mitigation**: Under `_lock`, check `DateTime.UtcNow - _lastAudioIssueTime >= retryTimeout`. If a recent packet updated `_lastAudioIssueTime`, the timer callback simply re-arms or exits without clearing the alert.
- **[Risk]** Very short retry timeouts (e.g., 1 second) causing alert flicker during brief gameplay pauses.
  - **Mitigation**: Settings enforce a minimum retry timeout of 1 second (default 5 seconds). Consecutive detection counts ensure reliable detection before re-triggering.
