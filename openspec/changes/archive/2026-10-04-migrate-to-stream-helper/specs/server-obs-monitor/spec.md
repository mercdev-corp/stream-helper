## Purpose

Connects to the OBS Studio WebSocket v5 server, monitors stream and recording state, tracks reconnect attempts and dropped frame thresholds, and ingests live audio volume meters.

## ADDED Requirements

### Requirement: OBS Studio WebSocket v5 Connection and Reconnection
The OBS monitor SHALL maintain an authenticated WebSocket connection to OBS Studio v5 using configured host, port, and password parameters, and automatically attempt reconnections on connection failure or drop using the configured retry timeout.

#### Scenario: Successful connection to OBS Studio
- **WHEN** the server launches or reconnect timer elapses and OBS Studio is running with WebSocket server enabled
- **THEN** the OBS monitor authenticates, establishes an active session, and clears any `obs disconnected` alerts

#### Scenario: OBS Studio not running or unreachable
- **WHEN** the WebSocket connection attempt fails or disconnects unexpectedly
- **THEN** the monitor marks OBS state as disconnected, broadcasts `obs disconnected` with tooltip "OBS is not running", and schedules a reconnect attempt based on the retry timeout

### Requirement: Stream and Output Health Monitoring
The OBS monitor SHALL periodically query `GetStreamStatus` and `GetStats` each second when an output is active, and evaluate reconnection attempts and dropped frame rates against the configured threshold over a rolling 60-second window.

#### Scenario: Stream reconnection detection
- **WHEN** streaming is active and OBS reports an increasing reconnect attempts counter
- **THEN** the monitor broadcasts `obs reconnecting` status with tooltip "OBS reconnecting", clearing when reconnection completes or streaming stops

#### Scenario: Network dropped frames threshold exceeded
- **WHEN** the number of dropped frames due to network congestion in the rolling 60-second window exceeds the configured threshold
- **THEN** the monitor broadcasts `obs network issue` status with tooltip "OBS network issue"

#### Scenario: Network dropped frames recovery
- **WHEN** the network dropped frame rate drops and remains below the threshold for at least 5 consecutive seconds
- **THEN** the monitor clears the `obs network issue` alert

#### Scenario: Render or encoder lag dropped frames exceeded
- **WHEN** skipped frames due to rendering lag or encoder overload exceed the configured threshold in the rolling 60-second window
- **THEN** the monitor broadcasts `obs render issue` status with tooltip "OBS render issue"

#### Scenario: Render or encoder lag recovery
- **WHEN** render and encoder dropped frame rates remain below the threshold for at least 5 consecutive seconds
- **THEN** the monitor clears the `obs render issue` alert

#### Scenario: Stream or recording stops
- **WHEN** active stream or recording outputs stop
- **THEN** output-specific alerts (`obs reconnecting` and `obs network issue`) are cleared immediately

### Requirement: OBS Audio Metering Subscription and Device Enumeration
The OBS monitor SHALL enumerate available audio input sources in OBS Studio and subscribe to `EventSubscription::InputVolumeMeters` to retrieve real-time peak and RMS audio levels for the configured capture source.

#### Scenario: Ingest volume meter events
- **WHEN** OBS Studio emits `InputVolumeMeters` events for the configured audio capture input
- **THEN** the monitor extracts the current RMS (dBFS) and peak levels and delivers them to the audio correlation engine

#### Scenario: Configured OBS audio device missing
- **WHEN** the audio capture device configured in settings is not found among active OBS input sources
- **THEN** the monitor reports `device disconnected` with tooltip "OBS capture device disconnected"

#### Scenario: Configured OBS audio device muted
- **WHEN** the target audio input source in OBS is muted
- **THEN** the monitor broadcasts `sound muted` with tooltip "OBS capture device muted"
