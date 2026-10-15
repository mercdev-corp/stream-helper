## 1. ObsMonitor Reconnection Architecture

- [x] 1.1 Add `SetRetryTimeout(int retryTimeoutSeconds)` to `IObsMonitor` and implement in `ObsMonitor` to update `_retryTimeoutSeconds` under lock; verify interface and property updates
- [x] 1.2 Refactor `ObsMonitor.StartConnectLoop` and `CleanupConnection` to enforce a single-flight reconnection loop (`_isConnectLoopRunning` guard) without timer duplication or task accumulation; verify single loop execution
- [x] 1.3 Ensure `ObsMonitor` cleans up connection tokens, handles delays cancelably, and cleanly triggers reconnect attempts every `_retryTimeoutSeconds`; verify clean disconnect and reconnect cycling

## 2. Server and Client Integration

- [x] 2.1 Update `ServerTrayApplicationContext` to pass `_settings.RetryTimeout` to `_obsMonitor` at startup and in `onTimeoutChanged`; verify timeout propagation
- [x] 2.2 Update `ServerTrayApplicationContext.OnPauseResumeClicked` to stop `_obsMonitor` when paused and restart it with `ConnectAsync` when resumed; verify pause/resume monitor state
- [x] 2.3 Update `ClientTrayApplicationContext` to pass `_settings.RetryTimeout` to `_obsMonitor` at startup and in `SetRetryTimeout` for Single-PC mode; verify Single-PC timeout propagation

## 3. Unit Tests & Verification

- [x] 3.1 Add unit tests verifying `ObsMonitor.SetRetryTimeout` immediately updates the retry interval and clamping behavior; verify with test execution
- [x] 3.2 Add unit tests verifying that repeated failed connection attempts in `ObsMonitor` do not accumulate concurrent tasks or throw unhandled exceptions; verify with test execution
- [x] 3.3 Add unit tests verifying that `ObsMonitor` connects, transitions state to Connected, and triggers `ConnectionStateChanged` as soon as the target endpoint becomes available after prior failures; verify with test execution
- [x] 3.4 Add unit tests verifying that pausing and resuming in `ServerTrayApplicationContext` correctly suspends and restarts `ObsMonitor`; verify with test execution


## 4. Documentation

- [x] 4.1 Update `README.md` to document the automatic single-flight OBS reconnection behavior, startup recovery, and immediate retry-timeout application; verify documentation formatting and rule compliance
