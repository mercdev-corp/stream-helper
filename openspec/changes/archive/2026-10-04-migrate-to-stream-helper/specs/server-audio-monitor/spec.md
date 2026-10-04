## MODIFIED Requirements

### Requirement: Independent Device Targeting per Instance
The audio monitor engine SHALL allow independent instances of the application (whether running as Server or Client in Single-PC mode) to bind and monitor microphone capture endpoints simultaneously without cross-instance interference.

#### Scenario: Multiple servers monitoring different microphones
- **WHEN** two server instances run from different directories on the same host
- **THEN** each instance binds to its own configured microphone without interfering with the other instance's event callbacks

#### Scenario: Single-PC client activates local microphone monitor
- **WHEN** the user selects Single-PC mode in StreamHelper.Client
- **THEN** the client instantiates the audio monitor locally to track the selected microphone's mute and hotplug events in-process
