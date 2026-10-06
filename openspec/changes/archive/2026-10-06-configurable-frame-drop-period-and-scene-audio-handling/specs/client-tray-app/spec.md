## MODIFIED Requirements

### Requirement: Adaptive Settings Dialog Layout
The client settings dialog SHALL adapt its layout based on the active operational mode, rendering Dual PC controls or Single PC controls while preserving shared overlay and application configuration.

#### Scenario: Dual PC layout presentation
- **WHEN** the settings dialog is viewed in Dual PC mode
- **THEN** the dialog displays the server IP dropdown, UDP port field, Game Audio Monitoring toggle, and Game Audio Output device selector, while hiding the OBS connection and microphone selection controls

#### Scenario: Single PC layout presentation
- **WHEN** the settings dialog is viewed in Single PC mode
- **THEN** the dialog displays OBS connection fields (IP, port, password toggle), skipped frames threshold, evaluation period in seconds (default 5, range 1 to 300), OBS audio capture device dropdown, OS microphone selection dropdown, Game Audio Monitoring toggle, and Game Audio Output selector, while hiding the server IP and server UDP port controls

#### Scenario: Single PC missing microphone styling
- **WHEN** the settings dialog is in Single PC mode and the saved microphone is not currently connected to the machine
- **THEN** the microphone dropdown renders the saved microphone as the first selected item with red strikethrough styling, followed by all currently connected capture devices

#### Scenario: Shared controls accessibility across modes
- **WHEN** switching between modes in the settings dialog
- **THEN** Run on startup, Retry timeout, Overlay maximum opacity, Pulse frequency, Animation cycles, Overlay size, and version display remain accessible and retain their values

### Requirement: Local JSON Settings Persistence
The client application SHALL store all configuration settings in `stream-helper-client-settings.json` located strictly within the application's executable directory, preserving independent Dual PC and Single PC configuration parameters across mode switches.

#### Scenario: Settings saved on modification
- **WHEN** any setting (operational mode, port, server IP, OBS settings, skipped frames threshold, skipped frames evaluation period, audio devices, retry timeout, opacity, frequency, animation cycles, overlay geometry) is modified
- **THEN** the changes are saved immediately to `stream-helper-client-settings.json` in the local application folder

#### Scenario: Coexistence of Dual PC and Single PC parameters
- **WHEN** the user switches between Dual PC mode and Single PC mode
- **THEN** the previous settings for the alternate mode remain preserved in `stream-helper-client-settings.json` and are restored upon returning to that mode

#### Scenario: Isolated configuration for different directories
- **WHEN** two client executables run from different folders (e.g., `C:\Client1` and `C:\Client2`)
- **THEN** each client reads and writes exclusively to its own folder's `stream-helper-client-settings.json` without cross-instance interference
