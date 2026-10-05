# audio-telemetry-correlation Specification

## Purpose

Captures gaming PC audio via WASAPI Loopback, computes 100ms RMS audio telemetry, and correlates client output with OBS Studio audio capture to detect silence or mismatched sound feeds.

## Requirements

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
The correlation engine SHALL maintain a rolling time-series window of gaming PC telemetry and OBS audio meter RMS values and compute Pearson's correlation coefficient ($r$) between them across the supported lag range. A low correlation SHALL only be treated as a mismatch when the RMS difference between the gaming PC signal and the OBS capture signal also exceeds the configured **audio match tolerance** (in dB). The tolerance SHALL be user-configurable, default to 20 dB, and range from 0 to 40 dB.

#### Scenario: Audio dynamic mismatch detection
- **WHEN** client audio exhibits dynamic variance (variance > 5.0 dB), the maximum Pearson correlation across the lag range remains low ($r < 0.5$) for 5 consecutive seconds, and the RMS difference between gaming PC and OBS signals exceeds the configured tolerance
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Audio signals correlate successfully
- **WHEN** dynamic game audio captured by OBS matches client telemetry with $r \ge 0.5$ within the lag window
- **THEN** the engine marks audio health as normal and clears any active correlation `sound issue` alert

#### Scenario: Low correlation within tolerance
- **WHEN** the maximum Pearson correlation is low ($r < 0.5$) but the RMS difference between gaming PC and OBS signals is within the configured tolerance, such as with low-volume game audio
- **THEN** the engine does not trigger a correlation `sound issue`

#### Scenario: Tolerance changed at runtime
- **WHEN** the user changes the audio match tolerance in the server settings (Dual-PC mode) or client settings (Single-PC mode)
- **THEN** the new value is applied to subsequent evaluations without restarting the application, in both Single-PC and Dual-PC modes

### Requirement: Simple Audio Issue Detection Mode
When `Simple audio issue detection` is enabled, the correlation engine SHALL skip the correlation and RMS-difference evaluation entirely and SHALL report a `sound issue` only when gaming PC audio is active while OBS capture is silent (the conditional silence detection rules). The mode SHALL be disabled by default and applied live in both Single-PC and Dual-PC modes.

#### Scenario: Game audio active while OBS captures nothing in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active (RMS > -45 dBFS) for 3 consecutive seconds and OBS capture remains below -60 dBFS
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Mismatched but non-silent capture in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active and OBS captures any sound above the silence threshold, even if its dynamics differ greatly from the gaming PC signal
- **THEN** the engine does not report a `sound issue`

#### Scenario: Simple mode toggled off
- **WHEN** simple detection is disabled again
- **THEN** correlation and tolerance-based evaluation resume using the saved tolerance value

### Requirement: Alert Suppression Hierarchy
The correlation engine SHALL suppress algorithmic anomaly detection (`sound issue`) whenever the microphone or OBS capture device is explicitly muted or disconnected.

#### Scenario: OBS audio device is muted
- **WHEN** the OBS audio capture source is muted in OBS Studio
- **THEN** the correlation engine suspends silence and cross-correlation evaluations, ensuring only `sound muted` is broadcast rather than conflicting `sound issue` alerts

