## Why

When Stream Helper Server (or Client in Single-PC mode) launches before OBS Studio (such as on Windows startup), it alerts that OBS is disconnected. When the user subsequently launches OBS, the application fails to detect that OBS is running and continues alerting an OBS connection issue indefinitely until restarted, because the internal reconnection loop spawns overlapping tasks with conflicting cancellation tokens and socket churn on each retry interval.

## What Changes

- Refactor `ObsMonitor` connection lifecycle to use a single-flight reconnect mechanism (preventing overlapping tasks, token disposal collisions, and socket churn) that reliably retries connection every configured retry timeout period.
- Add `SetRetryTimeout(int timeoutSeconds)` to `IObsMonitor` and `ObsMonitor`, and update `ServerTrayApplicationContext` and `ClientTrayApplicationContext` to pass the configured `RetryTimeout` at startup and whenever it is updated in settings.
- Update `ServerTrayApplicationContext` pause/resume handling to cleanly stop `ObsMonitor` when paused and restart it when resumed, ensuring paused state halts reconnection loops and resumes cleanly.
- Ensure that once OBS is started, the next retry attempt connects, completes authentication, transitions to `Connected`, and immediately clears `obs disconnected` alerts without requiring an application restart.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `server-obs-monitor`: Update `OBS Studio WebSocket v5 Connection and Reconnection` requirement to specify single-flight periodic reconnection behavior, reliable detection and alert clearance when OBS starts after the application, and immediate runtime application of retry timeout adjustments.
- `server-tray-app`: Update `Context Menu Navigation` pause/resume scenario to cleanly suspend and resume OBS monitoring alongside other background monitors.

## Impact
- **Affected code**: `StreamHelper.Shared/Obs/IObsMonitor.cs`, `StreamHelper.Shared/Obs/ObsMonitor.cs`, `StreamHelper.Server/ServerTrayApplicationContext.cs`, `StreamHelper.Client/ClientTrayApplicationContext.cs`.
- **Tests**: Add unit tests in `StreamHelper.Tests` validating single-flight retry scheduling, reconnection upon OBS availability after initial failure, runtime retry timeout updates, and pause/resume lifecycle.
- **Documentation**: Update `README.md` to document the automatic OBS reconnection behavior and retry timeout application.
