## Why

When audio fails to deliver from the gaming source to OBS and the source plays a brief alert or sound effect, the system triggers a `sound issue` notification. If the audio source subsequently remains silent (such as during quiet gameplay, menus, desktop idle, or even after the user has successfully restored audio routing to OBS), the `sound issue` alert currently remains active indefinitely because the clearing logic requires active sound to be positively detected in OBS. This leads to persistent false alarms where the user sees a sound capture issue despite the stream audio being fully restored or naturally silent.

Expiring unconfirmed sound issues after the configured retry timeout allows transient issues to clear automatically during periods of source silence while preserving continuous alerts as long as mismatch or silence conditions are actively re-detected.

## What Changes

- **Sound Issue Expiration on Retry Timeout**: In `StreamHelper.Shared` (`AudioCorrelationEngine`), track the timestamp of the most recent detected sound issue (`LastAudioIssueFacedTime`). The active sound issue state remains active only during the configured retry timeout duration. If the issue is not re-confirmed within this duration, the alert automatically expires and clears.
- **Timer-Driven Auto-Clear During Silence**: Introduce a background expiration check / timer mechanism in `AudioCorrelationEngine` so that when a sound issue is active and source audio ceases completely (stopping loopback packets), the alert reliably clears once the retry timeout elapses without requiring new incoming audio frames.
- **Immediate Recovery Preserved**: Positive detection of matching OBS audio or explicit suppression (scene switch, device mute, disconnection) continues to resolve or suppress the alert immediately without waiting for the timeout.
- **Live Retry Timeout Configuration**: Support configuring and updating `RetryTimeout` on `AudioCorrelationEngine` at runtime, synchronizing immediately with settings changes in both Server and Client (Single-PC mode) applications.
- **Documentation & Agent Requirements**:
  - Update `README.md` to document the retry-timeout-based sound issue alert expiration logic and how it interacts with quiet audio sources.
  - Update `AGENTS.md` (and README) to enforce that agents must update `README.md` whenever business logic changes.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `audio-telemetry-correlation`: Add sound issue alert expiration on retry timeout, tracking last audio issue faced time and automatically clearing alerts when not re-confirmed within the configured retry timeout window.

## Impact

- **Shared Code**: `StreamHelper.Shared.Audio.AudioCorrelationEngine` gains retry timeout state, last issue timestamp tracking, timer-based expiration evaluation, and `SetRetryTimeout(int seconds)` / constructor configuration.
- **Server Application**: `StreamHelper.Server.ServerTrayApplicationContext` passes `RetryTimeout` to `AudioCorrelationEngine` at startup and in the `onTimeoutChanged` callback.
- **Client Application**: `StreamHelper.Client.ClientTrayApplicationContext` passes `RetryTimeout` to `AudioCorrelationEngine` in Single-PC mode at startup and in `SetRetryTimeout`.
- **Project Documentation & Rules**: `README.md` and `AGENTS.md` updated with the business logic and agent maintenance requirements.
- **Tests**: `StreamHelper.Tests` extended with unit tests verifying sound issue timestamp renewal, timeout expiration during complete silence, and immediate recovery when audio is detected.
