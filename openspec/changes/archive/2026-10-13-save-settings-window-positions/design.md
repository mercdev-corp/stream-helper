## Context

Both `StreamHelper.Client` and `StreamHelper.Server` provide system tray applications with modal-like non-resizable configuration dialogs (`ClientSettingsForm` and `ServerSettingsForm`). Currently, both dialogs hardcode `StartPosition = FormStartPosition.CenterScreen` in their `InitializeComponent()` routines. Any user movement of these windows is not stored in `stream-helper-client-settings.json` or `stream-helper-server-settings.json`. Consequently, every time a user reopens settings or restarts either app, the window resets to the center of the screen.

Per project core requirements, all user adjustments in settings dialogs must be saved and applied immediately without a separate "Save" or "Apply" button.

See `proposal.md` for problem motivation and scope summary.

## Goals / Non-Goals

**Goals:**
- Persist window position (`SettingsWindowX`, `SettingsWindowY`) in settings JSON immediately whenever the settings window is moved in either the client or server app.
- Restore the settings window to its last saved position when reopened within the same session and after application restarts.
- Ensure off-screen safety: if saved coordinates are outside visible monitor work areas (e.g. following monitor disconnection or resolution change), fall back cleanly to screen centering.
- Maintain testability with comprehensive unit tests for position persistence, restoration, and off-screen fallback across both client and server settings forms.

**Non-Goals:**
- Window size persistence: `ClientSettingsForm` and `ServerSettingsForm` are fixed-size dialogs (`FormBorderStyle.FixedDialog`), so size persistence is not required.
- Persisting position for secondary modal dialogs or message boxes.

## Decisions

### 1. Nullable integer properties on settings models
- **Choice**: Add `int? SettingsWindowX` and `int? SettingsWindowY` to `ClientSettings` and `ServerSettings` (and register them in `ClientSettingsJsonContext` and `ServerSettingsJsonContext`).
- **Rationale**: `null` cleanly signifies "no saved position yet; use default screen centering", avoiding arbitrary magic numbers or sentinels.
- **Alternatives considered**:
  - `Point?` or a custom struct: Requires custom JSON serialization handling and tight coupling with System.Drawing in data models.
  - Sentinel values like `-1`: Negative coordinates are valid in multi-monitor setups where secondary monitors sit to the left of or above the primary monitor.

### 2. Shared `WindowPositionHelper` in `StreamHelper.Shared.UI`
- **Choice**: Implement a reusable `WindowPositionHelper` utility in `StreamHelper.Shared.UI`.
- **Rationale**: Encapsulates `Screen.AllScreens` boundary intersection checks and visible area validation in one place, avoiding duplicated code between `StreamHelper.Client` and `StreamHelper.Server`.
- **Validation logic**: A saved `(x, y)` location for a given `Size(width, height)` is considered valid on-screen if its rectangle intersects the `WorkingArea` of any connected screen with at least a minimum visible margin (e.g. 50x30 pixels). If not, fallback to `FormStartPosition.CenterScreen`.
- **Alternatives considered**:
  - Placing logic separately in `ClientSettingsForm` and `ServerSettingsForm`: Results in duplicated multi-monitor math.

### 3. Immediate save on window repositioning
- **Choice**: Hook the form's `LocationChanged` event. When the form has loaded and is in `FormWindowState.Normal`, update `_settings.SettingsWindowX = Location.X` and `_settings.SettingsWindowY = Location.Y` and invoke `_settings.Save()`.
- **Rationale**: Follows the existing proven pattern in `OverlayForm.cs` (which saves on every position change) and adheres to the architecture rule that settings changes are persisted immediately.
- **Alternatives considered**:
  - Save only on `FormClosing` or `WM_EXITSIZEMOVE`: If the application or OS is terminated abruptly while the window is open, the moved position would be lost. Immediate save guarantees persistence.

## Risks / Trade-offs

- **[Risk] High-DPI or monitor layout changes leave window inaccessible** → **Mitigation**: `WindowPositionHelper.IsOnScreen` validates the rectangle against `Screen.AllScreens` before applying coordinates; if the saved position is off-screen, it falls back to `CenterScreen`.
- **[Risk] LocationChanged firing during form construction/initialization** → **Mitigation**: Guard position updates behind an `_isLoaded` flag set after initial layout/load, ensuring initial centering or position setup does not trigger unintended saves.
- **[Risk] I/O frequency during window dragging** → **Mitigation**: Configuration files are small JSON payloads (<2KB) written synchronously in sub-millisecond time. In WinForms, title bar dragging runs in the Windows modal move loop and dispatches `LocationChanged`; synchronous file save is fast and matches `OverlayForm`'s drag handling.
