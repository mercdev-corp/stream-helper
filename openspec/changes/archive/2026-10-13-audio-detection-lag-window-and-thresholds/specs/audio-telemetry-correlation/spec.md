## MODIFIED Requirements

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

## ADDED Requirements

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
