## MODIFIED Requirements

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

## ADDED Requirements

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
