## ADDED Requirements

### Requirement: Settings Window Position Persistence and Restoration
The client settings dialog SHALL persist its on-screen window coordinates in `stream-helper-client-settings.json` whenever the window is moved, and restore its position to those saved coordinates upon being reopened within the same session or across application restarts. If no saved coordinates exist or if the saved coordinates place the window entirely outside the visible bounds of any active display screen, the settings window SHALL fall back to a visible position centered on the primary display screen.

#### Scenario: Settings window moved saves position immediately
- **WHEN** the user moves the client settings dialog on the screen
- **THEN** the updated window coordinates (X and Y) are saved immediately to `stream-helper-client-settings.json`

#### Scenario: Settings window reopened restores last position
- **WHEN** the user closes the client settings dialog and reopens it during the same application run
- **THEN** the settings window opens at the last saved coordinates instead of resetting to the screen center

#### Scenario: Settings window restores position across application restarts
- **WHEN** the client application is restarted and the user opens the settings dialog
- **THEN** the settings window reads the persisted coordinates from `stream-helper-client-settings.json` and opens at that exact position

#### Scenario: Off-screen coordinates fallback to visible area
- **WHEN** the settings dialog is opened and its saved coordinates are outside the visible work area of all connected monitors
- **THEN** the settings window falls back to being centered on the active or primary display screen so that it remains fully visible to the user
