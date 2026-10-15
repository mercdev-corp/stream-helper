## Context

`ObsMonitor` maintains an authenticated WebSocket connection to OBS Studio v5. When Stream Helper launches on PC startup before OBS Studio is open, the initial connection attempts fail. Previously, `StartConnectLoop` caught the connection failure exception and invoked `CleanupConnection()`, which scheduled a reconnect timer that invoked `StartConnectLoop()`, while the existing loop simultaneously awaited `Task.Delay` and looped. This caused exponential task branching ($2^N$), where dozens or hundreds of concurrent loops repeatedly overwrote and disposed each other's `_loopCts` and `_webSocket` instances. As a result, when OBS was later started, all subsequent connection attempts were cancelled in a race condition, leaving the application stuck in an `ObsDisconnected` alert state until restart.

Additionally, `IObsMonitor` lacked a `SetRetryTimeout` method, `_settings.RetryTimeout` was never forwarded to `ObsMonitor`, and `ServerTrayApplicationContext` did not stop or restart `ObsMonitor` upon pause/resume.

## Goals / Non-Goals

**Goals:**
- Eliminate concurrent connection task thrashing by implementing a single-flight connection lifecycle in `ObsMonitor`.
- Ensure `ObsMonitor` cleanly retries connection once every `_retryTimeoutSeconds` when OBS is not reachable.
- Immediately detect when OBS starts, complete the WebSocket handshake, transition to `ObsConnectionState.Connected`, and clear the `ObsDisconnected` alert without requiring an application restart.
- Add `SetRetryTimeout(int retryTimeoutSeconds)` to `IObsMonitor` and `ObsMonitor`, and forward runtime timeout changes immediately in Server and Client (Single-PC mode).
- Properly suspend `ObsMonitor` during Server pause and resume it upon unpausing.

**Non-Goals:**
- Changing OBS WebSocket v5 RPC payload structure, authentication math, or event subscription masks.
- Altering Dual-PC client connection behavior (Dual-PC clients communicate over UDP with StreamHelper.Server, not directly with OBS).

## Decisions

### Decision 1: Single-Flight Reconnection Worker Loop
Instead of combining a `while (true)` loop with a separate `_reconnectTimer` inside `CleanupConnection()`, `ObsMonitor` will maintain a single dedicated connection loop task.
- A boolean flag `_isConnectLoopRunning` (under `_lock`) ensures that `StartConnectLoop()` is strictly idempotent: if a loop is already running, subsequent calls return immediately without spawning another task.
- The connection loop runs sequentially:
  1. Checks if `_isRunning` and not `_isConnected`.
  2. Creates a scoped `CancellationTokenSource` linked to the master loop CTS.
  3. Attempts `ClientWebSocket.ConnectAsync` with a connection timeout.
  4. On successful connection, starts `ReceiveLoopAsync` and awaits its completion.
  5. On failure or disconnection, calls `CleanupConnection()`, which resets state and triggers disconnect events without scheduling a redundant timer.
  6. Awaits `Task.Delay(TimeSpan.FromSeconds(_retryTimeoutSeconds), ct)` before looping.
- `_reconnectTimer` is used only for explicit one-shot re-triggers (e.g., when config changes while connected), and always cleans up any previous timer.

*Alternative considered:* Relying solely on `System.Threading.Timer` without a `while` loop.
*Reason for rejection:* WebSocket receive loops are inherently async and long-running. A single-flight async loop cleanly pairs socket connection, message pump, and retry backoff in one sequential execution context.

### Decision 2: Adding SetRetryTimeout to IObsMonitor and Updating Callers
- Add `void SetRetryTimeout(int retryTimeoutSeconds)` to `IObsMonitor` and implement it in `ObsMonitor` to update `_retryTimeoutSeconds` (clamped to $\ge 1$) under `_lock`.
- In `ServerTrayApplicationContext`:
  - Pass `_settings.RetryTimeout` to `_obsMonitor` at startup.
  - In `onTimeoutChanged`, invoke `_obsMonitor.SetRetryTimeout(newTimeout)`.
  - In `OnPauseResumeClicked`, invoke `_obsMonitor.Stop()` when paused and `_obsMonitor.ConnectAsync(...)` when resumed.
- In `ClientTrayApplicationContext`:
  - Pass `_settings.RetryTimeout` to `_obsMonitor` at startup.
  - In `SetRetryTimeout`, if in Single-PC mode, invoke `_obsMonitor.SetRetryTimeout(timeout)`.

*Alternative considered:* Requiring `UpdateConfig(...)` with all parameters just to update timeout.
*Reason for rejection:* `UpdateConfig` requires host, port, password, threshold, device name, and period, which forces callers to retain and pass unrelated parameters and triggers unnecessary reconnects.

## Risks / Trade-offs

- **[Risk]** Rapid successive calls to `ConnectAsync` or `SetRetryTimeout`.  
  **Mitigation:** `_lock` guards state flags and configuration values. CTS cancellation cleanly aborts the in-flight delay or connection attempt without leaking resources.
- **[Risk]** Application exit while waiting in `Task.Delay`.  
  **Mitigation:** The loop CTS is cancelled and disposed in `Stop()` and `Dispose()`, terminating background delays promptly.
