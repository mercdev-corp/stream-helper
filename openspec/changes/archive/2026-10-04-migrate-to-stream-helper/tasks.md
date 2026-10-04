## 1. Solution & Project Rebranding

- [x] 1.1 Rename solution and project files from `MicHelper.*` to `StreamHelper.*` (`StreamHelper.Shared`, `StreamHelper.Server`, `StreamHelper.Client`, `StreamHelper.Tests`) and verify `dotnet build` succeeds.
- [x] 1.2 Update settings filenames to `stream-helper-server-settings.json` and `stream-helper-client-settings.json`, update default port to `19205`, and verify configuration load/save unit tests pass.
- [x] 1.3 Update build scripts (`build_all.ps1`), `Directory.Build.props`, and GitHub Actions release workflows for the new binary and package names.

## 2. OBS WebSocket v5 Integration

- [x] 2.1 Integrate OBS WebSocket v5 client in `StreamHelper.Shared` and verify connection, authentication, and reconnect retry timer against mock.
- [x] 2.2 Implement `GetStreamStatus` and `GetStats` polling with rolling 60-second frame drop delta rate calculation and 5-second hysteresis recovery.
- [x] 2.3 Implement OBS audio input enumeration and `EventSubscription::InputVolumeMeters` event subscription for peak/RMS telemetry.

## 3. Audio Telemetry & Correlation Engine

- [x] 3.1 Implement client-side `WASAPI Loopback Capture` with 100ms sub-sampling windows for RMS (dBFS) and peak calculations.
- [x] 3.2 Implement reverse UDP telemetry streaming (batching ten 100ms readings per second) and in-memory direct routing for Single-PC mode.
- [x] 3.3 Implement Conditional Silence Detector (triggering `sound issue` when client > -45 dBFS for 3–5s while OBS < -60 dBFS).
- [x] 3.4 Implement Pearson Cross-Correlation across 0 to 500 ms lag range in 100ms steps with alert suppression hierarchy.

## 4. Network Protocol & Multi-Status Aggregation

- [x] 4.1 Update `StatusPacket` and UDP broadcaster/listener to support multi-status alert bitmasks/payloads on port `19205`.
- [x] 4.2 Update event burst transmission and periodic heartbeat broadcast on port `19205`.

## 5. System Tray & Settings UI

- [x] 5.1 Implement 1-second system tray icon cycling across active alert states and format line-by-line hover tooltips in Server and Client.
- [x] 5.2 Update Server settings form with OBS connection parameters, skipped frames threshold, and OBS audio device selector.
- [x] 5.3 Update Client settings form with Dual-PC vs Single-PC mode switcher, dynamic layout adaptation, and game audio output selector.
- [x] 5.4 Add Donate context menu item to Server and Client tray applications positioned immediately below Settings.

## 6. Overlay Engine & Visuals

- [x] 6.1 Update `OverlayAssetManager` to pre-render and cache all new alert icons (`obs-disconnected`, `obs-reconnect`, `obs-network-issue`, `obs-render-issue`, `sound-issue`, `sound-muted`).
- [x] 6.2 Implement configurable multi-status overlay rotation (1 to 10 animation cycles, default 1) when multiple alerts are active.
- [x] 6.3 Implement WYSIWYG overlay edit mode with 1:1 aspect ratio constraint, mouse wheel scaling, and corner handles.

## 7. Verification & Testing

- [x] 7.1 Update unit and integration tests in `StreamHelper.Tests` covering frame delta calculation, silence detection, correlation, and settings persistence.
- [x] 7.2 Execute full test suite via `dotnet test` and run `build_all.ps1` to verify standalone single-file executables compile cleanly.
