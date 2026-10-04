## MODIFIED Requirements

### Requirement: Server System Tray Status Visualization
The server application SHALL display a system tray icon reflecting the current operational state: Active (`obs-logo-blue.png`, tooltip "Helper is active"), Paused (`pause.png`, tooltip "Helper is paused"), OBS Disconnected (`obs-disconnected.png`, tooltip "OBS is not running"), OBS Reconnecting (`obs-reconnect.png`, tooltip "OBS reconnecting"), OBS Network Issue (`obs-network-issue.png`, tooltip "OBS network issue"), OBS Render/Encoding Issue (`obs-render-issue.png`, tooltip "OBS render issue"), Microphone Muted (`mic-muted.png`, tooltip "Microphone muted"), Microphone Disconnected (`device-disconnected.png`, tooltip "Microphone disconnected"), OBS Capture Device Disconnected (`device-disconnected.png`, tooltip "OBS capture device disconnected"), OBS Capture Device Muted (`sound-muted.png`, tooltip "OBS capture device muted"), or OBS Sound Capture Issue (`sound-issue.png`, tooltip "OBS sound capture issue"). If more than one status is active simultaneously, the tray icon SHALL cycle through active status icons once per second, listing all active descriptions line-by-line in the tooltip on hover.

#### Scenario: Display muted state
- **WHEN** the monitored microphone is in a muted state and the server is active
- **THEN** the system tray shows the microphone muted icon with tooltip "Microphone muted"

#### Scenario: Display device disconnected state
- **WHEN** the monitored microphone or OBS capture device is disconnected or not found
- **THEN** the system tray shows the disconnected icon with the corresponding tooltip ("Microphone disconnected" or "OBS capture device disconnected")

#### Scenario: Display paused state
- **WHEN** the user pauses the server
- **THEN** the system tray shows the paused icon with tooltip "Helper is paused"

#### Scenario: Server healthy state
- **WHEN** the server is active and no OBS, audio, or microphone issues are detected
- **THEN** the system tray displays the default blue icon (`obs-logo-blue.png`) with the tooltip "Helper is active"

#### Scenario: Multiple statuses active in server tray
- **WHEN** multiple issues are simultaneously active (such as OBS network issue and microphone muted)
- **THEN** the system tray icon cycles through the active status icons once every 1 second, and the hover tooltip displays a bulleted line-by-line list of all active issues

### Requirement: Context Menu Navigation
The server application SHALL provide a right-click context menu on the system tray icon with Pause/Resume, Settings, Donate, and Exit options, with Donate positioned immediately below Settings.

#### Scenario: Toggle pause and resume
- **WHEN** the user clicks Pause or Resume in the context menu
- **THEN** the server toggles its paused state, updates the tray icon, saves the paused setting immediately, and either drops clients or resumes broadcasting

#### Scenario: Open settings dialog
- **WHEN** the user clicks Settings in the context menu
- **THEN** the settings window opens while background broadcasting continues uninterrupted

#### Scenario: Open donation webpage
- **WHEN** the user clicks Donate in the context menu
- **THEN** the application opens the donation URL from the embedded DONATE resource in the default web browser

#### Scenario: Exit application
- **WHEN** the user clicks Exit in the context menu
- **THEN** the application cleanly unregisters callbacks, closes the tray icon, and terminates

### Requirement: Local JSON Settings Persistence
The server application SHALL store all configuration settings in `stream-helper-server-settings.json` located strictly within the application's executable directory.

#### Scenario: Settings saved on modification
- **WHEN** any setting (OBS parameters, skipped frames threshold, audio devices, port, retry timeout, paused state) is modified in the dialog or context menu
- **THEN** changes are saved immediately to `stream-helper-server-settings.json` in the local directory and applied live

#### Scenario: Isolated configuration for different directories
- **WHEN** two server executables run from different folders (e.g., `C:\Server1` and `C:\Server2`)
- **THEN** each server reads and writes exclusively to its own folder's `stream-helper-server-settings.json` without cross-instance interference

### Requirement: Port Number Configuration and In-Use Collision Detection
The settings dialog SHALL allow configuring the broadcast port (default 19205) and detect if the specified port is already bound by another process or another server instance.

#### Scenario: Port in use validation
- **WHEN** the user enters a port number that is already in use
- **THEN** the input text displays in red color and hovering over the field shows the tooltip "Port is already in use"

#### Scenario: Port change applied live
- **WHEN** a valid, available port number is entered
- **THEN** the network broadcaster re-binds to the new port and saves the setting immediately

## ADDED Requirements

### Requirement: OBS Connection and Health Configuration Controls
The server settings dialog SHALL provide controls for configuring the OBS Studio WebSocket connection (IP address input default `127.0.0.1`, port input default `4455`, password with reveal/hide toggle), skipped frames threshold per minute, and a dropdown for selecting the OBS game audio capture device with red strikethrough styling for missing sources.

#### Scenario: Configure OBS connection parameters
- **WHEN** the user updates the OBS IP, port, or password in the server settings dialog
- **THEN** the settings are saved immediately and the OBS monitor attempts to connect or re-authenticate using the new credentials

#### Scenario: Missing OBS audio capture device in settings dropdown
- **WHEN** the server settings dialog opens and the saved OBS audio source is not found in OBS Studio
- **THEN** the missing device is rendered as the first item with red strikethrough styling, followed by all available OBS audio inputs
