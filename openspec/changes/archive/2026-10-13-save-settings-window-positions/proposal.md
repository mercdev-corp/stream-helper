## Why

Currently, when the settings window is moved in either Stream Helper Client or Stream Helper Server, its position is not saved. Every time a settings window is opened or the application is restarted, the window resets to its default center-screen location. Users who arrange their desktop workspace, stream setups, or multi-monitor configurations need their settings window position preserved across closes, reopens, and application restarts.

## What Changes

- Add settings window position properties (`SettingsWindowX`, `SettingsWindowY`) to `ClientSettings` and `ServerSettings`.
- In `ClientSettingsForm` and `ServerSettingsForm`, track window repositioning (e.g. on `Move` / `LocationChanged`) and save the updated coordinates immediately into the respective configuration JSON file (`stream-helper-client-settings.json` and `stream-helper-server-settings.json`), in alignment with the immediate-save architecture requirement.
- When opening `ClientSettingsForm` or `ServerSettingsForm`, check for saved coordinates:
  - If valid coordinates exist and are visible on an active display, restore the window to that location (`StartPosition = FormStartPosition.Manual; Location = new Point(x, y)`).
  - If no coordinates are stored (first run) or if the coordinates are completely off-screen (e.g. after a display disconnect or resolution change), fall back to centering on screen (`FormStartPosition.CenterScreen` or nearest visible display area).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `client-tray-app`: Specifies settings dialog window position persistence in `stream-helper-client-settings.json` while moved, and restoring the last saved position upon reopening and application restart with multi-monitor / off-screen safety checks.
- `server-tray-app`: Specifies settings dialog window position persistence in `stream-helper-server-settings.json` while moved, and restoring the last saved position upon reopening and application restart with multi-monitor / off-screen safety checks.

## Impact

- **Client Configuration (`ClientSettings`)**: Add `SettingsWindowX` and `SettingsWindowY` (nullable integers or default sentinel) with JSON serialization support in `ClientSettingsJsonContext`.
- **Server Configuration (`ServerSettings`)**: Add `SettingsWindowX` and `SettingsWindowY` (nullable integers or default sentinel) with JSON serialization support in `ServerSettingsJsonContext`.
- **Client UI (`ClientSettingsForm`, `ClientTrayApplicationContext`)**: Restore window position on creation/display and persist position updates during moves.
- **Server UI (`ServerSettingsForm`, `ServerTrayApplicationContext`)**: Restore window position on creation/display and persist position updates during moves.
- **Unit Tests (`ClientOverlayAndSettingsTests`, `ServerNetworkingAndSettingsTests`)**: Verify position persistence and restoration across restarts and openings, including off-screen fallback.
