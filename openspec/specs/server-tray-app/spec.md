# server-tray-app Specification

## Purpose

Specifies the server system tray user interface, status representations, context menu commands, settings configuration, port collision validation, and multi-instance startup management.

## Requirements

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

### Requirement: Microphone Selection with Missing Device Indication
The settings dialog SHALL provide a dropdown list of available microphones, defaulting to the system default on first run, rendering distinct badges for general default and communications default devices when they differ, and rendering a missing configured microphone in red strikethrough text.

#### Scenario: Configured microphone missing in dropdown
- **WHEN** the settings dialog opens and the currently saved microphone is not present on the host
- **THEN** the missing microphone is displayed as the first item with red strikethrough styling, followed by all currently connected microphones

#### Scenario: Missing microphone persists unless changed
- **WHEN** the settings dialog is closed without selecting a different microphone
- **THEN** the missing microphone remains preserved in `stream-helper-server-settings.json`

#### Scenario: Distinct default device indication
- **WHEN** the system has distinct default console and communications capture devices
- **THEN** the dropdown indicates the primary console default microphone with `(Default)` and the communications default microphone with `(Default Communications)`

### Requirement: Dropdown Alphabetical Ordering with Missing Item Precedence
All dropdown selectors in the server settings dialog (including Microphone selection and OBS Audio Source selection) SHALL sort their displayed items in case-insensitive alphabetical order by display text, EXCEPT when the currently selected option is in a missing state (rendered with red strikethrough text)—in which case that selected missing item SHALL be placed as the first option in the list, followed by the remaining active items sorted in alphabetical order.

#### Scenario: All available options sorted alphabetically
- **WHEN** a dropdown in the server settings dialog is populated and the selected option is an active, available device or source
- **THEN** all items in the dropdown are sorted in alphabetical order by display text

#### Scenario: Missing selected option positioned first
- **WHEN** a dropdown in the server settings dialog is populated and the selected configured option is missing (rendered with red strikethrough text)
- **THEN** the selected missing option is placed as the first item in the list, and all remaining active items are sorted in alphabetical order

### Requirement: Port Number Configuration and In-Use Collision Detection
The settings dialog SHALL allow configuring the broadcast port (default 19205) and detect if the specified port is already bound by another process or another server instance.

#### Scenario: Port in use validation
- **WHEN** the user enters a port number that is already in use
- **THEN** the input text displays in red color and hovering over the field shows the tooltip "Port is already in use"

#### Scenario: Port change applied live
- **WHEN** a valid, available port number is entered
- **THEN** the network broadcaster re-binds to the new port and saves the setting immediately

### Requirement: Path-Unique Windows Startup Registration
The server application SHALL support automatic startup on Windows login by creating an instance-unique entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` based on the executable path.

#### Scenario: Enable run on startup
- **WHEN** the user enables "Run on startup" in the settings dialog
- **THEN** the application adds a registry value named uniquely using the application type and executable path hash, pointing to the exact executable path

#### Scenario: Disable run on startup
- **WHEN** the user disables "Run on startup"
- **THEN** the application removes its specific unique registry value without affecting startup entries belonging to instances in other folders

#### Scenario: Verify startup state
- **WHEN** checking whether "Run on startup" is enabled
- **THEN** the system verifies if an entry with the exact current executable path exists under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### Requirement: Per-Folder Single Instance Enforcement
The server application SHALL ensure only one instance runs from a specific directory while allowing concurrent instances from different directories.

#### Scenario: Duplicate launch in same directory
- **WHEN** a user attempts to launch a second server executable from the same directory
- **THEN** the second instance detects the existing instance via a path-scoped system mutex and exits without disrupting the running instance

#### Scenario: Concurrent launches in different directories
- **WHEN** a user launches a server executable from a different directory
- **THEN** the new instance acquires its own path-scoped mutex and starts successfully

### Requirement: Server Settings Application Version Display
The server settings dialog SHALL display the application version embedded during compilation, positioned at the bottom of the dialog between the "View Logs..." button and the "Close" button.

#### Scenario: Display current version in server settings
- **WHEN** the user opens the server settings dialog
- **THEN** the application version (e.g., `v0.1.0`) is displayed centered between the "View Logs..." and "Close" buttons, reflecting the version value defined at build time from the repository `VERSION` file.

### Requirement: Server Settings Dialog Layout and Margin Guarantees
The server settings dialog SHALL maintain a consistent minimum right margin of 20 pixels for all controls, labels, and text elements, preventing any text or input controls from clipping or extending flush against the right window boundary across standard and high-DPI scaling.

#### Scenario: Server settings controls layout padding
- **WHEN** the server settings dialog is opened
- **THEN** all controls, buttons, and descriptive labels maintain at least 20 pixels of clearance from the right edge of the window

### Requirement: OBS Connection and Health Configuration Controls
The server settings dialog SHALL provide controls for configuring the OBS Studio WebSocket connection (IP address input default `127.0.0.1`, port input default `4455`, password with reveal/hide toggle), skipped frames threshold per minute, and a dropdown for selecting the OBS game audio capture device with red strikethrough styling for missing sources.

#### Scenario: Configure OBS connection parameters
- **WHEN** the user updates the OBS IP, port, or password in the server settings dialog
- **THEN** the settings are saved immediately and the OBS monitor attempts to connect or re-authenticate using the new credentials

#### Scenario: Missing OBS audio capture device in settings dropdown
- **WHEN** the server settings dialog opens and the saved OBS audio source is not found in OBS Studio
- **THEN** the missing device is rendered as the first item with red strikethrough styling, followed by all available OBS audio inputs