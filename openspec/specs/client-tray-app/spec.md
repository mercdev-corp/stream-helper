# client-tray-app Specification

## Purpose

Specifies the client system tray user interface, status visualizer, context menu, WYSIWYG overlay positioning controller, settings configuration, and multi-instance startup management.

## Requirements

### Requirement: Client System Tray Status Visualization
The client application SHALL display a system tray icon reflecting the current operational state: Active (`obs-logo-blue.png`, tooltip "Helper is active"), Paused (`pause.png`, tooltip "Helper is paused"), Server Disconnected (`server-disconnected.png`, tooltip "Server not found"), OBS Disconnected (`obs-disconnected.png`, tooltip "OBS is not running"), OBS Reconnecting (`obs-reconnect.png`, tooltip "OBS reconnecting"), OBS Network Issue (`obs-network-issue.png`, tooltip "OBS network issue"), OBS Render/Encoding Issue (`obs-render-issue.png`, tooltip "OBS render issue"), Microphone Muted (`mic-muted.png`, tooltip "Microphone muted"), Device Disconnected (`device-disconnected.png`), Game Audio Not Selected (`sound-muted.png`, tooltip "Game audio not selected"), Game Audio Not Found (`sound-issue.png`, tooltip "Game audio not found"), OBS Capture Device Muted (`sound-muted.png`, tooltip "OBS capture device muted"), or OBS Sound Capture Issue (`sound-issue.png`, tooltip "OBS sound capture issue"). If more than one status is active simultaneously, the tray icon SHALL cycle through active status icons once per second, listing all active descriptions line-by-line in the tooltip on hover.

#### Scenario: Display muted state in tray
- **WHEN** connected to the server (in Dual PC mode) or monitoring local microphone (in Single PC mode) and the microphone is muted
- **THEN** the system tray shows the microphone muted status icon with tooltip "Microphone muted"

#### Scenario: Display server disconnected in tray
- **WHEN** in Dual PC mode and the target server is unreachable or timed out
- **THEN** the system tray shows the disconnected status icon with tooltip "Server not found"

#### Scenario: Display local microphone disconnected in tray
- **WHEN** in Single PC mode and the selected microphone is disconnected or not found
- **THEN** the system tray shows the device disconnected status icon with tooltip "Microphone disconnected"

#### Scenario: Display client paused state in tray
- **WHEN** the user pauses the client application
- **THEN** the system tray shows the paused status icon with tooltip "Helper is paused"

#### Scenario: Display healthy active state in tray
- **WHEN** connected and no streaming or audio issues are detected
- **THEN** the system tray displays the default blue icon (`obs-logo-blue.png`) with the tooltip "Helper is active"

#### Scenario: Display multiple statuses cycling in tray
- **WHEN** multiple issues are simultaneously active (such as microphone muted and OBS network issue)
- **THEN** the system tray icon cycles through the active icons once every 1 second, and the hover tooltip displays a bulleted line-by-line list of all active issues

### Requirement: Context Menu Navigation
The client application SHALL provide a right-click context menu on the system tray icon with Pause/Resume, Settings, Donate, and Exit options, with Donate positioned immediately below Settings.

#### Scenario: Toggle pause and resume
- **WHEN** the user clicks Pause or Resume in the context menu
- **THEN** the client toggles its paused state, updates the tray icon, hides overlay if paused, saves the paused setting immediately, and suspends or resumes server packet consumption or local monitoring

#### Scenario: Open settings dialog
- **WHEN** the user clicks Settings in the context menu
- **THEN** the movable settings window opens and the overlay switches into interactive WYSIWYG controller mode

#### Scenario: Open donation webpage
- **WHEN** the user clicks Donate in the context menu
- **THEN** the application opens the donation URL from the embedded DONATE resource in the default web browser

#### Scenario: Exit application
- **WHEN** the user clicks Exit in the context menu
- **THEN** the client destroys the overlay window, removes the system tray icon, and exits cleanly

### Requirement: WYSIWYG Overlay Interactive Positioning and Resizing
While the settings dialog is open, the overlay SHALL become visible and function as a direct WYSIWYG manipulator allowing the user to reposition and resize the overlay across screens.

#### Scenario: Drag to reposition overlay
- **WHEN** the settings dialog is open and the user clicks and drags the visible overlay graphic
- **THEN** the overlay moves interactively to follow the cursor, and the new center coordinates are saved upon release

#### Scenario: Resize overlay maintaining aspect ratio
- **WHEN** the user drags a resize handle or edge on the WYSIWYG overlay
- **THEN** the overlay dimensions update while strictly preserving the image's original aspect ratio

#### Scenario: Settings dialog closes
- **WHEN** the settings dialog is closed
- **THEN** the overlay exits WYSIWYG interaction mode, returns to standard click-through behavior, and applies the current live state visibility

### Requirement: Server IP Discovery Dropdown with Offline Fallback
The settings dialog SHALL provide a dropdown list of server IP addresses detected broadcasting on the configured port, rendering an inaccessible saved IP with red strikethrough styling.

#### Scenario: Discovered servers displayed
- **WHEN** the client receives broadcast packets from one or more servers on the configured port
- **THEN** the dropdown lists the detected servers by IP and metadata

#### Scenario: Configured server IP offline
- **WHEN** the settings dialog opens and the server IP stored in settings is not detected on the network
- **THEN** the saved IP is displayed as the first selected item with red strikethrough styling followed by any active servers

#### Scenario: Inaccessible server IP remains in configuration
- **WHEN** the settings dialog is closed without selecting a different server IP
- **THEN** the stored IP address remains saved in `mic-helper-client-settings.json`

### Requirement: Dropdown Alphabetical Ordering with Missing/Offline Precedence
All dropdown selectors in the client settings dialog (including Server IP, Microphone, OBS Audio Source, and Game Audio Output) SHALL sort their displayed items in case-insensitive alphabetical order by display text, EXCEPT when the currently selected option is in a missing or offline state (rendered with red strikethrough text)—in which case that selected item SHALL be placed as the first option in the list, followed by the remaining active items sorted in alphabetical order.

#### Scenario: All available options sorted alphabetically
- **WHEN** a dropdown is populated and the selected option is an active, available device, source, or server
- **THEN** all items in the dropdown are sorted in alphabetical order by display text

#### Scenario: Missing or offline selected option positioned first
- **WHEN** a dropdown is populated and the selected configured option is missing or offline (rendered with red strikethrough text)
- **THEN** the selected missing/offline option is placed as the first item in the list, and all remaining available items are sorted in alphabetical order

### Requirement: Animation Sliders Configuration
The settings dialog SHALL provide sliders for maximum opacity (range 0 to 100, default 100), pulse frequency (range 0.1 to 5.0 seconds, default 1.0s), and animation cycles per status (range 1 to 10 cycles, default 1).

#### Scenario: Adjust maximum opacity
- **WHEN** the user slides the opacity control
- **THEN** the overlay's peak pulse opacity updates immediately to the selected percentage

#### Scenario: Adjust pulse frequency
- **WHEN** the user slides the frequency control
- **THEN** the cycle period of the sine-wave pulse adjusts immediately to the selected duration in seconds

#### Scenario: Adjust animation cycles
- **WHEN** the user slides the animation cycles control
- **THEN** the overlay immediately updates the number of pulse animation cycles each active status visual is shown before rotating to the next alert

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

### Requirement: Path-Unique Windows Startup Registration
The client application SHALL support automatic startup on Windows login by creating an instance-unique entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` based on the executable path.

#### Scenario: Enable run on startup
- **WHEN** the user enables "Run on startup" in the settings dialog
- **THEN** the application adds a registry value named uniquely using the application type and executable path hash, pointing to the exact executable path

#### Scenario: Disable run on startup
- **WHEN** the user disables "Run on startup"
- **THEN** the application removes its specific unique registry value without affecting startup entries belonging to instances in other folders

### Requirement: Per-Folder Single Instance Enforcement
The client application SHALL ensure only one instance runs from a specific directory while allowing concurrent instances from different directories.

#### Scenario: Duplicate launch in same directory
- **WHEN** a user attempts to launch a second client executable from the same directory
- **THEN** the second instance detects the existing instance via a path-scoped system mutex and exits without disrupting the running instance

#### Scenario: Concurrent launches in different directories
- **WHEN** a user launches a client executable from a different directory
- **THEN** the new instance acquires its own path-scoped mutex and starts successfully

### Requirement: Client Settings Application Version Display
The client settings dialog SHALL display the application version embedded during compilation, positioned at the bottom of the dialog between the "View Logs..." button and the "Close" button.

#### Scenario: Display current version in client settings
- **WHEN** the user opens the client settings dialog
- **THEN** the application version (e.g., `v0.1.0`) is displayed centered between the "View Logs..." and "Close" buttons, reflecting the version value defined at build time from the repository `VERSION` file.

### Requirement: Client Operational Mode Selection
The client application SHALL provide an option to switch between "Dual PC" and "Single PC" operational modes, applying the mode change immediately and restoring the active mode upon application startup.

#### Scenario: Switch from Dual PC to Single PC mode
- **WHEN** the user selects "Single PC" mode in the settings dialog
- **THEN** the client immediately transitions to Single PC mode, saves the active mode in settings, deactivates network UDP listening, and activates in-process OBS WebSocket monitoring, local microphone monitoring, and local loopback audio correlation

#### Scenario: Switch from Single PC to Dual PC mode
- **WHEN** the user selects "Dual PC" mode in the settings dialog
- **THEN** the client immediately transitions to Dual PC mode, saves the active mode in settings, stops local OBS and microphone monitors, activates UDP network listening using the saved server and port settings, and dynamically updates server status without requiring an application restart

#### Scenario: Startup restores active mode
- **WHEN** the client application launches
- **THEN** it loads the active mode from `stream-helper-client-settings.json` (defaulting to Dual PC mode if omitted) and activates only the subsystem for that mode

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

### Requirement: Mode Execution Resource Isolation
The client application SHALL completely deactivate the unused mode's subsystems to avoid unnecessary CPU, audio callback, or network resource consumption.

#### Scenario: Dual PC mode halts audio monitoring
- **WHEN** the client is running in Dual PC mode
- **THEN** the client does not initialize local OBS WebSocket connections or Windows Core Audio microphone capture endpoints

#### Scenario: Single PC mode halts network listener
- **WHEN** the client is running in Single PC mode
- **THEN** the client does not bind to UDP network ports, listen for network broadcasts, or process incoming UDP packets

### Requirement: Settings Dialog Layout and Right Padding Guarantees
The client settings dialog SHALL maintain a consistent minimum right margin of 20 pixels for all labels, hints, and controls, wrapping informational text and multi-line notes so no text clips or touches the right window boundary across standard and high-DPI scaling.

#### Scenario: Dual PC guidance note padding
- **WHEN** the settings dialog displays the Dual PC guidance note ("Run server app on remote PC where your Microphone is plugged in")
- **THEN** the text wraps within the dialog layout boundaries and maintains at least 20 pixels of padding from the right edge of the client area

#### Scenario: Interaction hint label padding
- **WHEN** the settings dialog displays the overlay interaction hint ("Drag overlay to move • Scroll mouse wheel or drag corners to resize.")
- **THEN** the hint text remains fully visible with at least 20 pixels of clearance from the right window border without truncation or clipping

### Requirement: Single PC Audio Issue Detection Controls
In Single PC mode, the client settings dialog SHALL provide, directly below the OBS game audio capture source dropdown and in this order from top to bottom, a `Simple audio issue detection` switch and an audio match tolerance slider (labelled in dB, range 0 to 40, default 20). Both values SHALL be persisted in `stream-helper-client-settings.json` and applied live to the in-process audio correlation engine.

#### Scenario: Single PC control order and placement
- **WHEN** the client settings dialog is opened in Single PC mode
- **THEN** the `Simple audio issue detection` switch appears below the OBS audio capture dropdown and the tolerance slider appears below the switch, with no controls overlapping and 20 pixel right margin preserved

#### Scenario: Enabling simple detection disables the slider in Single PC mode
- **WHEN** the user turns on `Simple audio issue detection` in Single PC mode
- **THEN** the slider becomes disabled (grayed out) and is displayed at its maximum position, while the saved tolerance value in settings remains unchanged

#### Scenario: Disabling simple detection restores the slider in Single PC mode
- **WHEN** the user turns off `Simple audio issue detection` in Single PC mode
- **THEN** the slider is enabled and returns to the previously saved tolerance value

#### Scenario: Single PC settings reopened with simple detection on
- **WHEN** the dialog opens in Single PC mode with simple detection saved as enabled
- **THEN** the slider is shown disabled at its maximum position and the saved tolerance value is still preserved in the client settings file

#### Scenario: Single PC tolerance slider adjusted
- **WHEN** the user moves the enabled slider in Single PC mode
- **THEN** the new tolerance is saved immediately, its current dB value is shown in the label, and it is applied to the local audio engine without restart

