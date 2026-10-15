## 1. Shared Engine Implementation

- [x] 1.1 Update `AudioCorrelationEngine` with `RetryTimeoutSeconds`, `LastAudioIssueTime`, and background expiration timer to auto-clear `HasSoundIssue` when unconfirmed during the retry timeout period; verify with targeted unit test
- [x] 1.2 Add `SetRetryTimeout(int retryTimeoutSeconds)` and include retry timeout in constructor and `Configure` methods of `AudioCorrelationEngine`; verify timeout property updates properly

## 2. Server & Client Integration

- [x] 2.1 Pass `RetryTimeout` to `AudioCorrelationEngine` at startup and in `onTimeoutChanged` within `ServerTrayApplicationContext`; verify configuration wiring
- [x] 2.2 Pass `RetryTimeout` to `AudioCorrelationEngine` at startup and in `SetRetryTimeout` within `ClientTrayApplicationContext` for Single-PC mode; verify configuration wiring

## 3. Unit Tests

- [x] 3.1 Add unit tests in `AudioTelemetryAndCorrelationTests.cs` verifying that a triggered sound issue expires and clears after the retry timeout elapses when no further audio or packets arrive
- [x] 3.2 Add unit tests in `AudioTelemetryAndCorrelationTests.cs` verifying that continuous/repeated issue detections refresh `LastAudioIssueTime` and sustain the active alert across multiple intervals
- [x] 3.3 Add unit tests in `AudioTelemetryAndCorrelationTests.cs` verifying that valid OBS audio arrival or suppression states immediately clear the alert before the retry timeout elapses

## 4. Documentation & Agent Requirements

- [x] 4.1 Update `README.md` to document the sound issue retry-timeout expiration behavior and its interaction with quiet or restored audio sources; verify formatting
- [x] 4.2 Update `AGENTS.md` and `README.md` to establish the mandatory requirement that agents must update `README.md` whenever application business logic changes; verify documentation integrity
