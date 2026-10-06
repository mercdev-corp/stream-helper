## 1. Frame Drop Evaluation Period Configuration

- [x] 1.1 Update `RollingFrameCounter` to support dynamic window configuration via a mutable `Window` property (or `SetWindow`) defaulting to 5 seconds, and verify unit tests pass for dynamic window sizing and sample pruning.
- [x] 1.2 Update `ServerSettings` and `ClientSettings` to add `SkippedFramesPeriodSeconds` (default: 5, validated range: 1–300) with JSON serialization and deserialization, and verify settings save and load tests pass.
- [x] 1.3 Update `IObsMonitor` and `ObsMonitor` to accept `skippedFramesPeriodSeconds` in constructor, `UpdateConfig`, and `SetSkippedFramesPeriod(int seconds)`, configuring the rolling frame counters accordingly, and verify unit tests in `ObsMonitorTests`.

## 2. Server and Client Settings UI Updates

- [x] 2.1 Update `ServerSettingsForm` to remove ` (per min)` from `Skipped frames threshold (per min):`, add a numeric input for evaluation period in seconds (default: 5, range: 1–300), wire value change callbacks to save settings and update `ObsMonitor`, and verify layout preserves the 20px right margin.
- [x] 2.2 Update `ClientSettingsForm` Single-PC panel to add a numeric input for evaluation period in seconds (default: 5, range: 1–300), wire value change callbacks to save settings and update `ObsMonitor`, and verify layout preserves the 20px right margin.
- [x] 2.3 Create shared constants or helper for frame drop settings limits and defaults in `StreamHelper.Shared` to ensure consistency between Server and Client.

## 3. Active Program Scene and Audio Source Tracking

- [x] 3.1 Update `ObsMonitor` event subscription bitmask to include `Scenes (4)` (`1 | 4 | 8 | 64 | 65536`) and handle `CurrentProgramSceneChanged` events.
- [x] 3.2 Implement scene item inspection in `ObsMonitor`: query `GetCurrentProgramScene` and `GetSceneItemList` (with `GetGroupSceneItemList` for groups and cycle-protected recursion for nested scenes) to check if the target audio capture source is present in the current program scene.
- [x] 3.3 Identify global special audio inputs (`GetSpecialInputs`) so global inputs (e.g., Desktop Audio) are treated as present across all scenes without querying scene items.
- [x] 3.4 Expose `bool IsAudioSourceInCurrentScene { get; }` on `IObsMonitor` / `ObsMonitor` and fire `StateChanged` when scene membership changes, verifying with unit tests.

## 4. Scene-Aware Sound Issue Alert Suppression

- [x] 4.1 Update `AudioCorrelationEngine.UpdateSuppressionStates` to accept `bool isNotInActiveScene` and suppress `HasSoundIssue` whenever the source is not present in the active scene, verifying with unit tests in `AudioTelemetryAndCorrelationTests`.
- [x] 4.2 Update `ServerTrayApplicationContext.UpdateAllStates` to check `_obsMonitor.IsAudioSourceInCurrentScene`, pass the suppression state to `_correlationEngine`, and suppress broadcasting `AlertFlags.ObsSoundCaptureIssue` when the source is not in the active scene.
- [x] 4.3 Update `ClientTrayApplicationContext.UpdateAllStates` (Single-PC mode) to check `_obsMonitor.IsAudioSourceInCurrentScene`, pass the suppression state to `_correlationEngine`, and suppress `AlertFlags.ObsSoundCaptureIssue` when the source is not in the active scene.

## 5. Verification and Integration Tests

- [x] 5.1 Add unit tests in `ObsMonitorTests` for dynamic period adjustment, `CurrentProgramSceneChanged` event handling, and scene item presence detection.
- [x] 5.2 Add unit tests in `AudioTelemetryAndCorrelationTests` verifying that sound issue alerts are suppressed and cleared when the audio device is marked not present in the active scene.
- [x] 5.3 Verify that Server and Client settings tests pass for `SkippedFramesPeriodSeconds` persistence and defaults.
- [x] 5.4 Update `README.md` to describe the configurable dropped frames period, the updated settings label and field, and the active scene sound issue suppression behavior, verifying that all documentation matches the updated application capabilities.
