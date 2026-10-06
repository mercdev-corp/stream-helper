## Why

Currently, dropped frame alerts (from network congestion, render lag, or encoder overload) evaluate skipped frames across a fixed 60-second rolling window. This long window keeps the overlay warning indicator pulsating for up to a full minute even after a brief, transient drop has resolved long ago. Allowing users to configure the evaluation period (defaulting to 5 seconds) provides faster, more responsive alerts and quicker recovery.

Additionally, when a streamer switches to an OBS scene that does not include the target game audio capture source (such as a chatting, intermission, or "be right back" scene), the correlation engine receives silence from OBS while client audio may still be active, causing false-positive sound issue alerts to pulse on screen. Suppressing sound issue alerts when the active OBS scene does not contain the target audio capture source prevents these spurious warnings.

## What Changes

- **Configurable Frame Drop Evaluation Period**:
  - Add a configurable evaluation period in seconds (default: 5 seconds, range: 1 to 300 seconds) for counting skipped frames across network, render, and encoder metrics.
  - Update `RollingFrameCounter` to dynamically support a configurable window duration.
  - Persist `SkippedFramesPeriodSeconds` in both `ServerSettings` and `ClientSettings`.
  - Update `IObsMonitor` / `ObsMonitor` to accept and dynamically update the evaluation window.
  - Update Server and Client Settings dialogs: remove ` (per min)` from the `Skipped frames threshold (per min):` label (making it `Skipped frames threshold:`), and add a numeric input field for the evaluation period in seconds (defaulting to 5).
  - Share validation and UI presentation patterns across Server and Client applications.

- **Scene-Aware Sound Issue Suppression**:
  - Extend OBS monitoring to track the current program scene name and its active scene items (including nested scenes and groups).
  - Differentiate global special audio inputs (which exist across all scenes) from scene-specific audio sources.
  - Detect whether the target OBS audio capture source is present in the currently active program scene.
  - Suppress broadcasting or triggering the `ObsSoundCaptureIssue` alert when the target capture source is not present in the active scene, clearing any active sound issue alert upon switching to a scene without the source.
  - Support this suppression behavior in both Server (Dual-PC) and Client (Single-PC) modes.

- **Documentation**:
  - Update `README.md` to document the configurable dropped frame evaluation period (replacing obsolete 60-second fixed delta descriptions), the updated `Skipped frames threshold:` label and new period seconds field, and the scene-aware audio suppression behavior.

## Capabilities

### New Capabilities

*(None)*

### Modified Capabilities

- `server-obs-monitor`: Update stream and output health monitoring to evaluate dropped frames over a user-configured evaluation window instead of a hardcoded 60-second window; track active program scene and determine if the target capture source is present in the current scene.
- `server-tray-app`: Update server settings dialog OBS controls to remove ` (per min)` from the threshold label, add a numeric input for evaluation period in seconds (default: 5), and persist the setting in `stream-helper-server-settings.json`.
- `client-tray-app`: Update client Single-PC settings dialog OBS controls to add the evaluation period in seconds input (default: 5), persist the setting in `stream-helper-client-settings.json`, and apply scene-aware sound issue suppression.
- `audio-telemetry-correlation`: Extend the alert suppression hierarchy so that sound issue alerts (`ObsSoundCaptureIssue`) are suppressed when the target audio capture device is not present in the currently active OBS program scene.

## Impact

- **Configuration & Storage**:
  - `ServerSettings` and `ClientSettings` add `SkippedFramesPeriodSeconds` (int, default 5).
- **Core Monitoring**:
  - `RollingFrameCounter` accepts dynamic window updates or configurable durations.
  - `IObsMonitor` and `ObsMonitor` expose configuration methods for the period and track program scene item membership for the target audio device.
  - `AudioCorrelationEngine` suppression state handles missing scene source.
- **UI**:
  - `ServerSettingsForm` and `ClientSettingsForm` layout updated with the new numeric input for period seconds and clean label text.
- **Protocol & Network**:
  - No breaking changes to the UDP broadcast packet schema; alerts continue to use existing `AlertFlags`.
- **Documentation**:
  - `README.md` updated to reflect the new evaluation period field, updated UI labels, and scene-aware sound issue suppression.
