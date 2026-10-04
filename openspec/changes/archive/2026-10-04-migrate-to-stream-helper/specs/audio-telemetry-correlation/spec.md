## Purpose

Captures gaming PC audio via WASAPI Loopback, computes 100ms RMS audio telemetry, and correlates client output with OBS Studio audio capture to detect silence or mismatched sound feeds.

## ADDED Requirements

### Requirement: WASAPI Loopback Audio Capture and Sub-Sampling
The audio telemetry engine SHALL capture audio from the configured output device using `WASAPI Loopback Capture`, calculating RMS volume (in dBFS) and peak values across 100ms sub-sampling windows (10 Hz).

#### Scenario: Continuous loopback sub-sampling
- **WHEN** Game Audio Monitoring is enabled and game audio plays through the configured sound output
- **THEN** the worker calculates RMS dBFS and peak levels for each consecutive 100ms window without dropping samples or exceeding 0.05% CPU usage

#### Scenario: Audio output endpoint not configured
- **WHEN** Game Audio Monitoring is enabled but no output device is selected in client settings
- **THEN** the client displays the `sound muted` icon in the system tray and overlay with tooltip "Game audio not selected", suspending telemetry generation

#### Scenario: Configured audio output device disconnected
- **WHEN** Game Audio Monitoring is enabled but the configured sound output device is missing from Windows Core Audio
- **THEN** the client displays the `sound issue` icon in the system tray and overlay with tooltip "Game audio not found", suspending telemetry generation

### Requirement: Reverse UDP Telemetry Streaming
In Dual-PC mode, the client SHALL batch ten 100ms RMS readings and transmit them over UDP to the server once per second: `[timestamp_ms, [rms_0, ... rms_9]]`. In Single-PC mode, the telemetry pipeline feeds the correlation engine directly in-memory without UDP sockets.

#### Scenario: Dual-PC telemetry transmission
- **WHEN** running in Dual-PC mode with Game Audio Monitoring enabled
- **THEN** the client transmits a UDP datagram once per second containing timestamp, sequence number, and the array of ten 100ms RMS values to the configured server IP and port

#### Scenario: Telemetry paused during client pause
- **WHEN** the client application is paused by the user
- **THEN** WASAPI loopback capture is halted and UDP telemetry packet transmission ceases immediately

### Requirement: Conditional Silence Detection
The correlation engine SHALL trigger a `sound issue` alert if the gaming PC detects active audio (`RMS > -45 dBFS`) continuously for 3 to 5 seconds while the corresponding OBS audio source remains below ambient threshold (`RMS < -60 dBFS`).

#### Scenario: Game audio active while OBS is silent
- **WHEN** client telemetry indicates RMS exceeds -45 dBFS for 3 consecutive seconds and the OBS capture source volume remains below -60 dBFS
- **THEN** the server broadcasts `sound issue` with tooltip "OBS sound capture issue" and activates the sound issue tray and overlay visuals

#### Scenario: Game audio is naturally quiet
- **WHEN** game audio volume on the gaming PC is below -45 dBFS (such as during menus or loading screens)
- **THEN** the silence detector suppresses alert generation, preventing false positive alarms

### Requirement: Pearson Cross-Correlation Anomaly Detection
The correlation engine SHALL maintain a rolling 10-second time-series window (100 samples at 100ms resolution) and compute Pearson's correlation coefficient ($r$) between gaming PC telemetry and OBS audio meters across a lag range of 0 to 500 ms in 100 ms steps.

#### Scenario: Audio dynamic mismatch detection
- **WHEN** client audio exhibits dynamic variance (variance > 5.0 dB) and the maximum Pearson correlation across the lag range remains low ($r < 0.5$) for 5 consecutive seconds
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Audio signals correlate successfully
- **WHEN** dynamic game audio captured by OBS matches client telemetry with $r \ge 0.5$ within the 0 to 500 ms lag window
- **THEN** the engine marks audio health as normal and clears any active correlation `sound issue` alert

### Requirement: Alert Suppression Hierarchy
The correlation engine SHALL suppress algorithmic anomaly detection (`sound issue`) whenever the microphone or OBS capture device is explicitly muted or disconnected.

#### Scenario: OBS audio device is muted
- **WHEN** the OBS audio capture source is muted in OBS Studio
- **THEN** the correlation engine suspends silence and cross-correlation evaluations, ensuring only `sound muted` is broadcast rather than conflicting `sound issue` alerts
