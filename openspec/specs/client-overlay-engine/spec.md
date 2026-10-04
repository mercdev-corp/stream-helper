# client-overlay-engine Specification

## Purpose

Specifies the anti-cheat-safe, ultra-low-overhead topmost click-through overlay window, pulsing opacity animation engine, and pre-rendered asset caching mechanism for displaying microphone and server statuses.

## Requirements

### Requirement: Anti-Cheat Safe Click-Through Overlay Window
The client overlay SHALL be implemented as an external desktop layered window with `WS_EX_TOPMOST`, `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_NOACTIVATE`, and `WS_EX_TOOLWINDOW` styles, without hooking game APIs or inspecting foreign processes, and SHALL remain visibly positioned on top of borderless windowed and fullscreen games.

#### Scenario: User clicks game screen beneath overlay
- **WHEN** the overlay is visible on top of an active borderless or fullscreen game window and the user clicks over the overlay region
- **THEN** all mouse and keyboard inputs pass directly through to the underlying application or game without interception or latency

#### Scenario: Overlay displays over fullscreen applications
- **WHEN** a game or application runs in fullscreen or borderless windowed mode
- **THEN** the overlay remains rendered on top of the display without stealing focus or activating a window title bar

### Requirement: Proactive Topmost Z-Order Enforcement
The client overlay engine SHALL proactively maintain topmost Z-order over all desktop applications and games (including borderless windowed games that assert topmost Z-order) by reasserting its topmost position upon visibility changes, external window foreground transitions, and active animation ticks without stealing input focus.

#### Scenario: Borderless game activates or gains focus
- **WHEN** an external application or borderless windowed game (such as Hunt: Showdown 1896) gains focus or activates
- **THEN** the overlay receives the foreground change event and reasserts `HWND_TOPMOST` without stealing input focus

#### Scenario: Overlay occluded by another topmost window during active alert
- **WHEN** the overlay is visible in an alert state and another window is positioned above it in the Z-order
- **THEN** the overlay detects the occlusion during its active update cycle and immediately repositions itself to the top of the Z-order

#### Scenario: Overlay transitions from hidden to visible
- **WHEN** the overlay transitions from hidden to visible upon entering an alert state
- **THEN** the overlay immediately reasserts `HWND_TOPMOST` to ensure it is displayed above currently running borderless games

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

### Requirement: Sine-Wave Pulsing Opacity and Zero-CPU Idle
The client overlay engine SHALL calculate transparency using a smooth sine-wave function between 0 and the configured maximum opacity, and SHALL halt animation timers completely when the overlay is hidden.

#### Scenario: Pulsing active overlay
- **WHEN** the overlay is visible in an alert state (muted or disconnected)
- **THEN** the opacity smoothly oscillates from 0 to the configured maximum opacity (0-100%) according to the configured pulse period (0.1 to 5.0 seconds)

#### Scenario: Zero CPU consumption when hidden
- **WHEN** the overlay transitions to hidden
- **THEN** the animation timer is disabled and no render updates or CPU ticks occur for overlay drawing

### Requirement: Pre-Rendered Asset Resizing and Caching
The client overlay engine SHALL pre-render and cache scaled bitmap images for all overlay status visuals (`mic-muted.png`, `device-disconnected.png`, `server-disconnected.png`, `obs-disconnected.png`, `obs-reconnect.png`, `obs-network-issue.png`, `obs-render-issue.png`, `sound-issue.png`, `sound-muted.png`) upon configuration save to eliminate real-time image scaling and GPU/CPU interpolation overhead during runtime playback.

#### Scenario: Settings dialog closes after overlay resize
- **WHEN** the user resizes the overlay in the settings dialog and closes settings
- **THEN** the overlay engine renders the resized images at the exact pixel dimensions, caches them to disk/memory in the application folder, and utilizes the pre-rendered bitmaps for subsequent overlay display

### Requirement: Independent Overlay Positioning per Instance
The client overlay engine SHALL permit multiple client instances running from different folders to display separate overlays at independent screen coordinates simultaneously.

#### Scenario: Concurrent client overlays on screen
- **WHEN** two client instances run from different directories tracking distinct server ports
- **THEN** each client renders its own independent overlay at its own configured screen coordinates without visual collision or coordinate overwrites

### Requirement: WYSIWYG Overlay Interaction and Aspect Ratio Constraint
While the client settings dialog is open, the overlay SHALL enter interactive WYSIWYG edit mode displaying a preview overlay with a dashed outline, four corner resize handles, and a real-time size badge, supporting repositioning via drag-and-drop and resizing from 32 to 1024 pixels with a strictly locked 1:1 aspect ratio.

#### Scenario: Resize via mouse wheel
- **WHEN** the settings dialog is open and the user scrolls the mouse wheel over the overlay
- **THEN** the overlay dimensions scale smoothly up or down while maintaining a locked 1:1 aspect ratio, synchronizing the settings slider and size badge in real time

#### Scenario: Resize via corner drag handles
- **WHEN** the user clicks and drags any of the four corner handles on the edit overlay
- **THEN** the overlay width and height adjust symmetrically, preserving the 1:1 aspect ratio
