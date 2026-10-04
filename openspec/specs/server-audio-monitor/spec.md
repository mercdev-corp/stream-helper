# server-audio-monitor Specification

## Purpose

Specifies the Windows Core Audio subsystem integration on the server host, delivering event-driven microphone mute detection, hardware plug/unplug notification, and isolated per-instance device targeting.

## Requirements

### Requirement: Windows Core Audio Device Enumeration
The server audio monitor SHALL enumerate all active audio capture endpoints on the system and expose their system identifiers and human-readable device names.

#### Scenario: Enumerate active capture devices
- **WHEN** the audio subsystem queries system capture devices
- **THEN** it retrieves all active microphones with their unique endpoint ID strings and friendly names

### Requirement: Event-Driven Zero-Polling Mute Detection
The server audio monitor SHALL register for native volume and mute event notifications on the selected audio endpoint to detect state changes instantaneously without continuous CPU polling.

#### Scenario: Hardware or software mute toggle
- **WHEN** the user mutes or unmutes the selected microphone via hardware switch, Windows volume mixer, or hotkey
- **THEN** the system receives an immediate audio notification callback and notifies the server state engine without consuming idle CPU cycles

### Requirement: Device Hotplug and Unplug Tracking
The server audio monitor SHALL monitor device state notifications to detect when the selected microphone is removed or reattached.

#### Scenario: Monitored microphone is disconnected
- **WHEN** the physical microphone is unplugged from the host machine
- **THEN** the audio monitor marks the device as unavailable, transitions state to disconnected, and activates a periodic probe loop based on the configured retry timeout

#### Scenario: Monitored microphone is reconnected
- **WHEN** the selected microphone is re-plugged into the host machine
- **THEN** the audio monitor re-establishes volume change callbacks, reads current mute status, and resumes active status reporting

### Requirement: Default Device Fallback and Saved Selection Preservation
The server audio monitor SHALL resolve the system default capture endpoint by prioritizing the standard general/console default device (`ERole.eConsole`), falling back to the default communication endpoint (`ERole.eCommunications`) or multimedia endpoint (`ERole.eMultimedia`) only when a general console default is unavailable, and preserve a missing saved device in settings when it is temporarily unplugged.

#### Scenario: First application launch
- **WHEN** the server starts with no configured microphone in settings
- **THEN** the system resolves the Windows default console capture device (`ERole.eConsole`), binds to it, saves its identity in settings, and falls back to `ERole.eCommunications` or `ERole.eMultimedia` if console is unavailable

#### Scenario: Configured microphone not found on startup
- **WHEN** the server launches but the microphone specified in settings is not attached
- **THEN** the server retains the configured device identifier in settings, marks device state as disconnected, and does not overwrite the setting with an arbitrary device

### Requirement: Independent Device Targeting per Instance
The audio monitor engine SHALL allow independent instances of the application (whether running as Server or Client in Single-PC mode) to bind and monitor microphone capture endpoints simultaneously without cross-instance interference.

#### Scenario: Multiple servers monitoring different microphones
- **WHEN** two server instances run from different directories on the same host
- **THEN** each instance binds to its own configured microphone without interfering with the other instance's event callbacks

#### Scenario: Single-PC client activates local microphone monitor
- **WHEN** the user selects Single-PC mode in StreamHelper.Client
- **THEN** the client instantiates the audio monitor locally to track the selected microphone's mute and hotplug events in-process
