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
The correlation engine SHALL trigger a `sound issue` alert if the gaming PC detects active audio (`RMS > -48 dBFS`) continuously for 3 to 5 seconds while the corresponding OBS audio source remains below the ambient silence threshold (`RMS < -60 dBFS`) within the configured **audio lag compensation window**. If the gaming PC audio volume is below active threshold (`RMS <= -48 dBFS`), the silence detector SHALL suppress alert generation.

#### Scenario: Game audio active while OBS is silent
- **WHEN** client telemetry indicates RMS exceeds -48 dBFS for 3 consecutive seconds and the OBS capture source volume remains below -60 dBFS across the entire configured lag compensation window
- **THEN** the server broadcasts `sound issue` with tooltip "OBS sound capture issue" and activates the sound issue tray and overlay visuals

#### Scenario: Game audio is naturally quiet
- **WHEN** game audio volume on the gaming PC is below -48 dBFS (such as during menus or loading screens)
- **THEN** the silence detector suppresses alert generation, preventing false positive alarms

#### Scenario: OBS audio arrives within lag compensation window
- **WHEN** client audio is active (> -48 dBFS) and OBS audio reaches or exceeds the non-silent threshold (>= -60 dBFS) within the configured lag compensation window
- **THEN** the silence detector treats the audio as captured and does not trigger a silence alert

### Requirement: Pearson Cross-Correlation Anomaly Detection
The correlation engine SHALL maintain a rolling time-series window of gaming PC telemetry and OBS audio meter RMS values and compute Pearson's correlation coefficient ($r$) between them across the configured audio lag compensation range. A low correlation SHALL only be treated as a mismatch when the RMS difference between the gaming PC signal and the OBS capture signal also exceeds the configured **audio match tolerance** (in dB). The tolerance SHALL be user-configurable, default to 20 dB, and range from 0 to 40 dB. When signals correlate successfully ($r \ge 0.5$) or difference is within tolerance, any active correlation issue alert SHALL be cleared and instantaneous silence alerts SHALL be suppressed.

#### Scenario: Audio dynamic mismatch detection
- **WHEN** client audio exhibits dynamic variance (variance > 1.0 dB), the maximum Pearson correlation across the lag range remains low ($r < 0.5$) for 5 consecutive seconds, and the RMS difference between gaming PC and OBS signals exceeds the configured tolerance
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Audio signals correlate successfully
- **WHEN** dynamic game audio captured by OBS matches client telemetry with $r \ge 0.5$ within the configured lag window
- **THEN** the engine marks audio health as normal and clears any active `sound issue` alert

#### Scenario: Low correlation within tolerance
- **WHEN** the maximum Pearson correlation is low ($r < 0.5$) but the RMS difference between gaming PC and OBS signals is within the configured tolerance, such as with low-volume game audio
- **THEN** the engine does not trigger a correlation `sound issue`

#### Scenario: Tolerance changed at runtime
- **WHEN** the user changes the audio match tolerance in the server settings (Dual-PC mode) or client settings (Single-PC mode)
- **THEN** the new value is applied to subsequent evaluations without restarting the application, in both Single-PC and Dual-PC modes

### Requirement: Simple Audio Issue Detection Mode
When `Simple audio issue detection` is enabled, the correlation engine SHALL skip the correlation and RMS-difference evaluation entirely and SHALL report a `sound issue` only when gaming PC audio is active while OBS capture remains silent across the configured audio lag compensation window (the conditional silence detection rules). The mode SHALL be disabled by default and applied live in both Single-PC and Dual-PC modes.

#### Scenario: Game audio active while OBS captures nothing in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active (RMS > -48 dBFS) for 3 consecutive seconds and OBS capture remains below -60 dBFS across the configured lag compensation window
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Mismatched but non-silent capture in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active and OBS captures sound at or above -60 dBFS within the configured lag compensation window, even if its dynamics differ from the gaming PC signal
- **THEN** the engine does not report a `sound issue`

#### Scenario: Simple mode toggled off
- **WHEN** simple detection is disabled again
- **THEN** correlation and tolerance-based evaluation resume using the saved tolerance value

### Requirement: Audio Lag Compensation Window
The correlation engine SHALL support a user-configurable audio lag compensation window (ranging from 0.0 to 3.0 seconds, default 1.0 second, in 0.1s increments). The lag window SHALL define the lookahead/lookback horizon when resolving OBS meter samples relative to gaming PC timestamps in both Simple and Full detection modes, and in both Single-PC and Dual-PC modes.

#### Scenario: Runtime adjustment of lag compensation window
- **WHEN** the user updates the audio lag compensation window in settings
- **THEN** the new lag window is applied immediately to subsequent evaluations without restarting the application

#### Scenario: Delay compensation for capture card latency
- **WHEN** OBS capture audio is delayed behind gaming PC audio by 800ms and the lag compensation window is configured to 1.0s or higher
- **THEN** the correlation engine detects OBS capture within the lag window and does not trigger spurious silence alerts

### Requirement: Audio Correlation Engine State Reset on Capture Device Change
The audio correlation engine SHALL immediately reset its rolling OBS meter history, sample queues, and active alert flags whenever the configured OBS Game Audio Capture Source is changed in application settings or reconnects.

#### Scenario: Device switch flushes historical samples
- **WHEN** the user selects a different OBS Game Audio Capture Source in settings
- **THEN** historical meter readings from the previous source are discarded immediately and alert states are cleared

### Requirement: Alert Suppression Hierarchy
The correlation engine SHALL suppress algorithmic anomaly detection (`sound issue`) whenever the microphone or OBS capture device is explicitly muted or disconnected, or when the target OBS audio capture source is not present in the currently active OBS program scene.

#### Scenario: OBS audio device is muted
- **WHEN** the OBS audio capture source is muted in OBS Studio
- **THEN** the correlation engine suspends silence and cross-correlation evaluations, ensuring only `sound muted` is broadcast rather than conflicting `sound issue` alerts

#### Scenario: Target OBS audio device not in active scene
- **WHEN** the target OBS audio capture source is not present in the currently active OBS program scene
- **THEN** the correlation engine suppresses silence and cross-correlation evaluations, clearing any active `sound issue` alert and preventing spurious sound issue broadcasts

### Requirement: Sound Issue Alert Expiration on Retry Timeout
The audio correlation engine SHALL record and update the timestamp of the most recent sound issue detection whenever a sound issue condition is confirmed (via conditional silence detection or cross-correlation mismatch). The active `sound issue` alert state SHALL remain active only during the configured **retry timeout** duration. If the issue is not re-confirmed according to established detection logic within the retry timeout period, the correlation engine SHALL automatically dismiss and clear the `sound issue` alert. The expiration check SHALL operate reliably even during complete silence when no loopback audio telemetry or OBS audio meters are produced.

#### Scenario: Sound issue expires after retry timeout during source silence
- **WHEN** a `sound issue` alert is triggered and the audio source subsequently remains silent without generating further issue confirmations
- **THEN** the correlation engine automatically clears the `sound issue` alert once the configured retry timeout duration has elapsed from the last issue detection time

#### Scenario: Sound issue refreshed by repeated issue detection within retry timeout
- **WHEN** a `sound issue` alert is active and an audio mismatch or uncaptured active audio condition is detected again before the retry timeout expires
- **THEN** the engine updates the last issue detection timestamp and extends the active alert duration for another full retry timeout period

#### Scenario: Sound issue cleared immediately when audio is detected in OBS
- **WHEN** a `sound issue` alert is active and valid matching audio is detected on the OBS capture source before the retry timeout expires
- **THEN** the correlation engine clears the `sound issue` alert immediately without waiting for the retry timeout duration to elapse

#### Scenario: Runtime adjustment of retry timeout
- **WHEN** the user adjusts the retry timeout in application settings
- **THEN** the new timeout value is applied immediately to subsequent expiration evaluations without requiring an application restart, in both Single-PC and Dual-PC modes

