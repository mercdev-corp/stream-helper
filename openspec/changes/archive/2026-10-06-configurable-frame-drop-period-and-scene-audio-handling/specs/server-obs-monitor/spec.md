## MODIFIED Requirements

### Requirement: Stream and Output Health Monitoring
The OBS monitor SHALL periodically query `GetStreamStatus` and `GetStats` each second when an output is active, and evaluate reconnection attempts and dropped frame rates against the configured threshold over a user-configured evaluation window (defaulting to 5 seconds, configurable from 1 to 300 seconds).

#### Scenario: Stream reconnection detection
- **WHEN** streaming is active and OBS reports an increasing reconnect attempts counter
- **THEN** the monitor broadcasts `obs reconnecting` status with tooltip "OBS reconnecting", clearing when reconnection completes or streaming stops

#### Scenario: Network dropped frames threshold exceeded
- **WHEN** the number of dropped frames due to network congestion in the configured evaluation window exceeds the configured threshold
- **THEN** the monitor broadcasts `obs network issue` status with tooltip "OBS network issue"

#### Scenario: Network dropped frames recovery
- **WHEN** the network dropped frame rate drops and remains below the threshold for at least 5 consecutive seconds
- **THEN** the monitor clears the `obs network issue` alert

#### Scenario: Render or encoder lag dropped frames exceeded
- **WHEN** skipped frames due to rendering lag or encoder overload exceed the configured threshold in the configured evaluation window
- **THEN** the monitor broadcasts `obs render issue` status with tooltip "OBS render issue"

#### Scenario: Render or encoder lag recovery
- **WHEN** render and encoder dropped frame rates remain below the threshold for at least 5 consecutive seconds
- **THEN** the monitor clears the `obs render issue` alert

#### Scenario: Stream or recording stops
- **WHEN** active stream or recording outputs stop
- **THEN** output-specific alerts (`obs reconnecting` and `obs network issue`) are cleared immediately

#### Scenario: Evaluation period changed at runtime
- **WHEN** the user updates the evaluation period in settings
- **THEN** the rolling counter updates its evaluation window duration dynamically to the new value without reconnecting to OBS

### Requirement: OBS Audio Metering Subscription and Device Enumeration
The OBS monitor SHALL enumerate available audio input sources in OBS Studio, subscribe to `EventSubscription::InputVolumeMeters` to retrieve real-time peak and RMS audio levels for the configured capture source, and track the active program scene to determine whether the configured capture source is present in the current scene.

#### Scenario: Ingest volume meter events
- **WHEN** OBS Studio emits `InputVolumeMeters` events for the configured audio capture input
- **THEN** the monitor extracts the current RMS (dBFS) and peak levels and delivers them to the audio correlation engine

#### Scenario: Configured OBS audio device missing
- **WHEN** the audio capture device configured in settings is not found among active OBS input sources
- **THEN** the monitor reports `device disconnected` with tooltip "OBS capture device disconnected"

#### Scenario: Configured OBS audio device muted
- **WHEN** the target audio input source in OBS is muted
- **THEN** the monitor broadcasts `sound muted` with tooltip "OBS capture device muted"

#### Scenario: Target audio device present in active scene or global
- **WHEN** the target audio device is a global special input or is present within the current program scene (directly or within a scene group/nested scene)
- **THEN** the monitor reports that the target audio device is active in the current scene

#### Scenario: Target audio device not present in active scene
- **WHEN** the target audio device is not a global input and is not present within the current program scene (or any of its groups/nested scenes)
- **THEN** the monitor reports that the target audio device is not active in the current scene and suppresses sound issue alerts
