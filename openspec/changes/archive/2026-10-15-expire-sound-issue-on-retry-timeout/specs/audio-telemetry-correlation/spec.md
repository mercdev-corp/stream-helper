## ADDED Requirements

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
