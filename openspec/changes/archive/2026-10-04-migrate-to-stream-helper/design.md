## Context

See `proposal.md` for motivation and requirements summary.

The current codebase is a .NET 10 solution consisting of `MicHelper.Shared`, `MicHelper.Server`, `MicHelper.Client`, and `MicHelper.Tests`. The application must operate with near-zero resource consumption (CPU, memory, VRAM) and avoid any game process inspection or graphics API hooking to guarantee anti-cheat safety.

## Goals / Non-Goals

**Goals:**
- Provide an all-in-one streaming issues HUD and tray monitor for OBS Studio connectivity, encoder/render lag, network congestion, game audio capture, and microphone mute.
- Retain both **Dual-PC** and **Single-PC** modes by placing all monitoring and correlation engines in `StreamHelper.Shared`, allowing `StreamHelper.Client.exe` to run them in-process on single-PC rigs without requiring `StreamHelper.Server.exe`.
- Guarantee low network and CPU overhead through 100ms WASAPI sub-sampling batched into 1-second UDP packets.
- Prevent stuck alerts and flickering via rolling 60-second delta calculations and a 5-second hysteresis recovery window.

**Non-Goals:**
- Video frame capture or pixel analysis: all OBS health metrics are gathered strictly via WebSocket v5 telemetry (`GetStreamStatus`, `GetStats`).
- Game process injection or graphics API hooking: the overlay remains an external layered Windows desktop window (`WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED`).
- Cloud synchronization or remote web dashboards: operation is strictly local network (UDP multicast/unicast) or localhost in-process.

## Decisions

### Decision 1: Shared Core Engines for Unified Single-PC and Dual-PC Architecture
- **Choice:** Implement `ObsMonitor`, `WindowsAudioMonitor`, and `AudioCorrelationEngine` as decoupled services in `StreamHelper.Shared`.
  - In **Dual-PC mode**: `StreamHelper.Server.exe` runs these engines and broadcasts via `UdpBroadcaster`. `StreamHelper.Client.exe` runs `UdpListener` and loopback capture, transmitting telemetry to the server.
  - In **Single-PC mode**: `StreamHelper.Client.exe` instantiates the shared engines directly in-process, feeding loopback telemetry into the correlation engine in-memory with zero network sockets.
- **Alternatives Considered:** Forcing single-PC users to launch both `Server.exe` and `Client.exe` concurrently. Rejected due to redundant system tray icons, port collisions on localhost, and higher memory/CPU footprint.

### Decision 2: Rolling 60-Second Delta for Skipped Frames with Hysteresis
- **Choice:** Maintain a timestamped ring buffer of dropped frames from OBS `GetStats` and `GetStreamStatus`. Calculate the delta rate over the trailing 60 seconds against the user threshold. Require metrics to remain below threshold for 5 consecutive seconds before clearing the alert.
- **Alternatives Considered:** Evaluating cumulative frame counters directly. Rejected because cumulative counters never decrease, permanently locking alerts into an active state after the first threshold breach.

### Decision 3: 100ms Audio Sub-Sampling with Batched 1-Second UDP Datagrams
- **Choice:** Client audio worker uses `WASAPI Loopback Capture` to calculate RMS (dBFS) and peak over 100ms windows (10 Hz). In Dual-PC mode, it batches ten 100ms readings into a single UDP packet sent once per second (`[timestamp_ms, [rms_0, ... rms_9]]`).
- **Alternatives Considered:**
  - 1-second single samples: Mathematically incompatible with testing audio latency in 50–100ms steps, and averages out transient peaks.
  - 10 UDP packets per second: Increases network context switching and packet processing overhead on the server.

### Decision 4: Alert Suppression Hierarchy
- **Choice:** Enforce a strict alert suppression order:
  $$\text{Device Disconnected} \longrightarrow \text{Muted (Mic or OBS)} \longrightarrow \text{Correlation Sound Issue}$$
  If a capture device is disconnected or muted, suppress algorithmic anomaly detection (`sound issue`) to eliminate redundant alarms and conflicting tray cycles.
- **Alternatives Considered:** Independent evaluation of all rules. Rejected because a muted device naturally causes correlation mismatch, generating false "sound issue" alerts alongside "sound muted".

### Decision 5: Multi-Status Display Cycling
- **Choice:**
  - **System Tray:** Cycle active status icons once every 1 second when multiple alerts exist, displaying a line-by-line bulleted list of all active issue descriptions on hover.
  - **In-Game Overlay:** Cycle active alert graphics according to the configured number of full pulse animation cycles (1 to 10 cycles, defaulting to 1).

## Risks / Trade-offs

- **[Dual-PC Clock Skew]** &rarr; System clocks on separate PCs may differ by hundreds of milliseconds.
  *Mitigation:* The Server correlates client telemetry using relative arrival sequence and local receipt timestamps rather than assuming wall-clock synchronization.
- **[Game Audio Dynamic Range]** &rarr; Games with quiet scenes (cutscenes, loading screens) could trigger false silence alerts.
  *Mitigation:* The Conditional Silence Detector only triggers if the gaming PC has sustained audio above $-45\text{ dBFS}$ for 3–5 seconds. If the game is quiet, detection is suppressed.
- **[Anti-Cheat Compatibility]** &rarr; Kernel-level anti-cheat software (Easy Anti-Cheat, BattlEye, Vanguard) may flag overlay windows.
  *Mitigation:* Maintain pure Win32 external layered click-through window styles (`WS_EX_TOPMOST`, `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`) without process opening or window hooks.
