## Context

Stream Helper monitors stream stability and audio capture health across OBS Studio and Windows Core Audio in both Dual-PC (Server/Client) and Single-PC modes.

Currently:
1. Frame drop rate detection in `RollingFrameCounter` is hardcoded to a 60-second window. The Server and Client settings dialogs display a threshold input with label `Skipped frames threshold (per min):` on the Server, and the alert stays active for an entire minute following a transient frame drop spike.
2. In OBS Studio WebSocket v5, audio volume meter events (`InputVolumeMeters`) return -100 dBFS for capture sources that are not part of the active program scene. The correlation engine interprets this silence as an audio capture failure when the gaming PC produces sound, raising false-positive `ObsSoundCaptureIssue` alerts.

See `proposal.md` for motivation and background.

## Goals / Non-Goals

**Goals:**
- Make the dropped frame evaluation period user-configurable (default: 5 seconds, range: 1–300 seconds) in both Server and Client (Single-PC mode) settings.
- Update `RollingFrameCounter` to dynamically support a configurable window duration.
- Clean up the UI label to `Skipped frames threshold:` and add a companion `Evaluation period (seconds):` numeric input field.
- Track OBS Studio's active program scene and determine whether the target audio capture device is a global input or present in the active scene (including nested scenes and groups).
- Suppress `ObsSoundCaptureIssue` alerts in both Server and Client modes when the target capture source is absent from the active program scene.

**Non-Goals:**
- Changing the 5-second hysteresis recovery period for dropped frames (alert clearance after staying below threshold for 5 seconds remains unchanged).
- Re-architecting OBS WebSocket protocol communication away from the existing client implementation.
- Handling arbitrary complex custom scene plugins outside standard OBS scenes, groups, and inputs.

## Decisions

### Decision 1: Dynamic Rolling Window in `RollingFrameCounter`
- **Choice**: Add a mutable `Window` property (or `SetWindow(TimeSpan window)`) on `RollingFrameCounter`.
- **Rationale**: Retaining existing sample history while pruning samples that exceed the newly configured window allows seamless runtime adjustment without re-instantiating counters or dropping recent telemetry data.
- **Alternatives Considered**:
  - Reinstantiating the counters on every config change: Triggers unnecessary object allocations and resets active alert states momentarily.

### Decision 2: Scene Item Hierarchy Traversal
- **Choice**: Query `GetCurrentProgramScene` and `GetSceneItemList` (with `GetGroupSceneItemList` for groups and recursion with a visited set for nested scenes) to check if the target capture source is present.
- **Rationale**: Streamers commonly nest game capture sources within scene groups or embedded sub-scenes (e.g., "Gameplay Overlay" scene embedded in "Main Stream" scene). If the target input is identified as a global special input (from `GetSpecialInputs`), scene traversal is bypassed and it is always treated as present.
- **Alternatives Considered**:
  - Only checking top-level scene items: Breaks for streamers using groups or nested scenes for their game capture.
  - Relying on `inputAudioActive` property: In OBS WebSocket v5, per-source active states can be erratic during transitions; scene presence directly maps to streamer intent.

### Decision 3: Event-Driven Scene Change Tracking
- **Choice**: Subscribe to `Scenes` events (`eventSubscriptions |= 4`) and handle `CurrentProgramSceneChanged` in `ObsMonitor.HandleEvent`.
- **Rationale**: Responding directly to scene change events ensures instant suppression updates when transitioning scenes, rather than waiting for the 1-second polling timer.
- **Alternatives Considered**:
  - Only checking on the 1-second poll timer: Can introduce a 1-second delay during which a false alert might briefly fire after a scene switch.

### Decision 4: Alert Suppression via Correlation Engine State
- **Choice**: Extend `AudioCorrelationEngine.UpdateSuppressionStates(bool obsAudioDisconnected, bool obsAudioMuted, bool isNotInActiveScene)` and integrate with `ObsMonitor.IsAudioSourceInCurrentScene`.
- **Rationale**: Follows the existing suppression hierarchy (`ObsCaptureDeviceDisconnected` -> `ObsCaptureDeviceMuted` -> `NotInActiveScene` -> `SoundIssue`). The correlation engine immediately resets `HasSoundIssue = false` when suppressed, cleanly clearing any active alert.
- **Alternatives Considered**:
  - Suppressing only at the UI/Broadcaster layer: Leaves the correlation engine in an inconsistent internal alert state and clutters log files with false anomaly warnings.

### Decision 5: UI Placement and Consistency
- **Choice**: In `ServerSettingsForm` and `ClientSettingsForm`, display `Skipped frames threshold:` and `Evaluation period (seconds):` side-by-side or stacked consistently with 20px padding guarantees. Remove ` (per min)` from the Server label.
- **Rationale**: Both dialogs present identical controls for OBS dropped frame configuration, sharing default values (Threshold: 60, Period: 5s, Range: 1–300s).
- **Alternatives Considered**:
  - Separate advanced settings tab: Adds unnecessary UI complexity for a straightforward pair of numeric inputs.

## Risks / Trade-offs

- **[Risk] Deeply nested or circular scene references in OBS**: A streamer could configure nested scenes in a loop.
  → **Mitigation**: Maintain a `HashSet<string> visitedScenes` and maximum depth limit (e.g., 3 levels) during scene item traversal.
- **[Risk] Small period seconds causing noisy alerts**: Setting the period to 1 or 2 seconds could cause sensitivity to single-frame drops if the threshold is set very low.
  → **Mitigation**: Clamp minimum period to 1 second (defaulting to 5 seconds). The 5-second hysteresis recovery period prevents flickering.
- **[Risk] WebSocket request concurrency during rapid scene transitions**: Multiple rapid scene switches could trigger concurrent scene item requests.
  → **Mitigation**: Serialize scene inspection using a cancellation token or single-flight flag so stale scene inspections are discarded.
