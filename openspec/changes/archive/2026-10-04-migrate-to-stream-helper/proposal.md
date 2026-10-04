## Why

Gameplay streamers regularly miss stream-breaking failures while focused on full-screen games—such as muted microphones, uncaptured game audio, OBS network congestion, encoder/render lag, or OBS disconnects. Stream Helper forks and expands Mic Helper into an all-in-one in-game streaming HUD and tray indicator that detects and surfaces these critical streaming failures in real time across dual-PC and single-PC setups.

## What Changes

- **BREAKING: Rebranding & Naming**: Rebrand the entire application from "Mic Helper" to "Stream Helper". Executable binaries become `StreamHelper.Server.exe` and `StreamHelper.Client.exe`, and settings files become `stream-helper-server-settings.json` and `stream-helper-client-settings.json`.
- **BREAKING: Network Port**: Update the default broadcast port from `13205` to `19205`.
- **NEW: OBS Studio Integration (`server-obs-monitor`)**: Connects to OBS Studio via WebSocket v5 to track process health, stream/recording status, reconnection attempts (`obs reconnecting`), network dropped frames (`obs network issue`), and rendering/encoding lag dropped frames (`obs render issue`) evaluated against a rolling 60-second threshold.
- **NEW: Game Audio Correlation Engine (`audio-telemetry-correlation`)**: Captures game audio output via WASAPI Loopback on the gaming PC, computes RMS and peak volume in 100ms sub-sampling windows, transmits telemetry over UDP to the server, and runs both a Conditional Silence Detector and a Pearson Cross-Correlation engine against OBS volume meters (`InputVolumeMeters`).
- **MODIFIED: Client Overlay Engine (`client-overlay-engine`)**: Rotates through multiple active alert visuals according to the configured animation cycles (1 to 10, default 1), adds pre-rendered scaling and caching for all new OBS and audio status assets, and enforces 1:1 aspect ratio in WYSIWYG edit mode.
- **MODIFIED: Client Tray App (`client-tray-app`)**: Preserves Dual-PC and Single-PC operational modes with adaptive settings controls, adds animation cycles slider (1 to 10, default 1), cycles active tray icons once per second when multiple alerts exist, provides line-by-line hover tooltips, and includes Donate context menu item positioned immediately below Settings.
- **MODIFIED: Server Tray App (`server-tray-app`)**: Adds configuration fields for OBS connection, skipped frames threshold, and OBS audio capture device selection, with 1-second tray icon cycling and multi-line hover tooltips.
- **MODIFIED: Core Network Protocol (`core-network-protocol`)**: Expands UDP datagram payloads to broadcast multi-status alert states and establishes a reverse UDP telemetry channel for client-to-server audio telemetry.
- **MODIFIED: Server Audio Monitor (`server-audio-monitor`)**: Shares Windows Core Audio OS microphone capture endpoint monitoring between the Server and the Client (in Single-PC mode).

## Capabilities

### New Capabilities
- `server-obs-monitor`: OBS WebSocket v5 lifecycle, authentication, health checks, stream status metrics, frame drop threshold alerts, and audio volume meter subscription.
- `audio-telemetry-correlation`: WASAPI loopback capture, 100ms RMS sub-sampling, reverse UDP telemetry transport, Conditional Silence Detection, and Pearson cross-correlation against OBS input levels.

### Modified Capabilities
- `client-overlay-engine`: Expands overlay rendering to support all new alert conditions, pre-rendered asset caching for new icons, and configurable animation cycle multi-status visual rotation.
- `client-tray-app`: Rebrands to Stream Helper, retains Single-PC and Dual-PC mode toggling with dynamic settings layout, adds 1-second tray cycling, and formats multi-issue hover tooltips.
- `server-tray-app`: Rebrands to Stream Helper Server, introduces OBS connection and audio capture settings, adds 1-second tray cycling, and formats multi-issue hover tooltips.
- `core-network-protocol`: Updates default port to 19205, defines multi-status packet payload format, and specifies the reverse UDP telemetry datagram contract.
- `server-audio-monitor`: Clarifies OS microphone monitoring scope and enables in-process reuse for Client Single-PC mode.

## Impact

- **Projects & Solutions**: Renames solution projects (`MicHelper.Client` -> `StreamHelper.Client`, `MicHelper.Server` -> `StreamHelper.Server`, `MicHelper.Shared` -> `StreamHelper.Shared`).
- **Dependencies**: Integrates an OBS WebSocket v5 client and WASAPI Loopback Capture (via NAudio / CoreAudio interop).
- **Build Scripts & Workflows**: Updates `build_all.ps1`, `Directory.Build.props`, and GitHub Actions release workflows to build and package `StreamHelper.Server.exe` and `StreamHelper.Client.exe` into `stream-helper-<version>-<arch>.zip`.
