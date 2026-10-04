## MODIFIED Requirements

### Requirement: State-Driven Overlay Visibility
The client overlay SHALL display the corresponding status visual only during active alert conditions (OBS issues, audio capture alerts, microphone muted, server disconnected, or device disconnected) and remain completely hidden during normal or paused operations. If more than one alert status is active simultaneously, the overlay SHALL cycle through the active alert visuals according to the configured number of animation cycles (1 to 10 cycles, defaulting to 1).

#### Scenario: Microphone is muted
- **WHEN** the client receives a Muted status from the server or local microphone monitor
- **THEN** the overlay displays the pulsing "microphone muted" graphic at the configured screen position

#### Scenario: Server is disconnected
- **WHEN** the server connection is lost or timed out in Dual-PC mode
- **THEN** the overlay displays the pulsing "server disconnected" graphic at the configured screen position

#### Scenario: Microphone is unmuted or client is paused
- **WHEN** the microphone is unmuted and no alerts are active, or the client application is in a paused state
- **THEN** the overlay is completely hidden from view and its render loop is suspended

#### Scenario: Multiple alerts active simultaneously
- **WHEN** more than one alert status is active simultaneously (such as microphone muted and OBS render issue)
- **THEN** the overlay rotates between the active alert graphics, displaying each for the configured number of complete pulse animation cycles (defaulting to 1) before transitioning to the next active alert

### Requirement: Pre-Rendered Asset Resizing and Caching
The client overlay engine SHALL pre-render and cache scaled bitmap images for all overlay status visuals (`mic-muted.png`, `device-disconnected.png`, `server-disconnected.png`, `obs-disconnected.png`, `obs-reconnect.png`, `obs-network-issue.png`, `obs-render-issue.png`, `sound-issue.png`, `sound-muted.png`) upon configuration save to eliminate real-time image scaling and GPU/CPU interpolation overhead during runtime playback.

#### Scenario: Settings dialog closes after overlay resize
- **WHEN** the user resizes the overlay in the settings dialog and closes settings
- **THEN** the overlay engine renders the resized images at the exact pixel dimensions, caches them to disk/memory in the application folder, and utilizes the pre-rendered bitmaps for subsequent overlay display

## ADDED Requirements

### Requirement: WYSIWYG Overlay Interaction and Aspect Ratio Constraint
While the client settings dialog is open, the overlay SHALL enter interactive WYSIWYG edit mode displaying a preview overlay with a dashed outline, four corner resize handles, and a real-time size badge, supporting repositioning via drag-and-drop and resizing from 32 to 1024 pixels with a strictly locked 1:1 aspect ratio.

#### Scenario: Resize via mouse wheel
- **WHEN** the settings dialog is open and the user scrolls the mouse wheel over the overlay
- **THEN** the overlay dimensions scale smoothly up or down while maintaining a locked 1:1 aspect ratio, synchronizing the settings slider and size badge in real time

#### Scenario: Resize via corner drag handles
- **WHEN** the user clicks and drags any of the four corner handles on the edit overlay
- **THEN** the overlay width and height adjust symmetrically, preserving the 1:1 aspect ratio
