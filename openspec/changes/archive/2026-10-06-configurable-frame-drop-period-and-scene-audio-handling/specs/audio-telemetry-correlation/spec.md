## MODIFIED Requirements

### Requirement: Alert Suppression Hierarchy
The correlation engine SHALL suppress algorithmic anomaly detection (`sound issue`) whenever the microphone or OBS capture device is explicitly muted or disconnected, or when the target OBS audio capture source is not present in the currently active OBS program scene.

#### Scenario: OBS audio device is muted
- **WHEN** the OBS audio capture source is muted in OBS Studio
- **THEN** the correlation engine suspends silence and cross-correlation evaluations, ensuring only `sound muted` is broadcast rather than conflicting `sound issue` alerts

#### Scenario: Target OBS audio device not in active scene
- **WHEN** the target OBS audio capture source is not present in the currently active OBS program scene
- **THEN** the correlation engine suppresses silence and cross-correlation evaluations, clearing any active `sound issue` alert and preventing spurious sound issue broadcasts
