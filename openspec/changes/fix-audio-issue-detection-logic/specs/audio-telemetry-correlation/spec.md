## MODIFIED Requirements

### Requirement: Reverse UDP Telemetry Streaming
In Dual-PC mode, the client SHALL batch ten 100ms RMS readings and transmit them over UDP to the server once per second: `[timestamp_ms, [rms_0, ... rms_9]]`. The correlation engine SHALL continuously estimate and apply the clock offset between the client packet timestamp and the server clock so that cross-machine clock drift does not shift client sample timestamps relative to OBS audio meter timestamps. In Single-PC mode, the telemetry pipeline feeds the correlation engine directly in-memory without UDP sockets, sharing the same unified sample evaluation pipeline.

#### Scenario: Dual-PC telemetry transmission
- **WHEN** running in Dual-PC mode with Game Audio Monitoring enabled
- **THEN** the client transmits a UDP datagram once per second containing timestamp, sequence number, and the array of ten 100ms RMS values to the configured server IP and port

#### Scenario: Cross-PC clock drift compensation
- **WHEN** the gaming PC clock and streaming PC clock differ (for example, by 1 to 3 seconds or more) during Dual-PC telemetry streaming
- **THEN** the server aligns incoming batch sample timestamps to the server timeline using the tracked client-to-server clock offset before matching against OBS meter history

#### Scenario: Telemetry paused during client pause
- **WHEN** the client application is paused by the user or Game Audio Monitoring is disabled in Single-PC or Dual-PC mode
- **THEN** WASAPI loopback capture is halted, UDP telemetry packet transmission ceases immediately, and any active correlation engine state is reset

### Requirement: Conditional Silence Detection
The correlation engine SHALL trigger a `sound issue` alert if the gaming PC detects active audio (`RMS > -60 dBFS`, including quieter game sounds such as footsteps at `-48` to `-55 dBFS`) continuously for 1 second (10 readings at 100ms) while the corresponding OBS audio source remains below the ambient silence threshold (`RMS < -60 dBFS`) across the configured **audio lag compensation window**. Whenever the OBS audio source captures non-silent audio (`RMS >= -60 dBFS`), the silence detector SHALL immediately reset its consecutive counter and clear any active silence alert regardless of whether the gaming PC audio is currently above or below `-60 dBFS`. If the gaming PC audio volume is below active threshold (`RMS <= -60 dBFS`) while OBS is also silent, the silence detector SHALL suppress alert generation and decay any partial counter (`-2` per silent reading).

#### Scenario: Game audio active while OBS is silent
- **WHEN** client telemetry indicates RMS exceeds -60 dBFS (e.g. footsteps at -48 to -55 dBFS or gunfire at -14 dBFS) for 1 consecutive second (10 readings) and the OBS capture source volume remains below -60 dBFS across the entire configured lag compensation window
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue" and activates the sound issue tray and overlay visuals in both Single-PC and Dual-PC modes

#### Scenario: Game audio is naturally quiet
- **WHEN** game audio volume on the gaming PC is at or below -60 dBFS (such as during silent menus or loading screens)
- **THEN** the silence detector suppresses alert generation, preventing false positive alarms

#### Scenario: OBS audio arrives within lag compensation window
- **WHEN** client audio is active (> -60 dBFS) and OBS audio reaches or exceeds the non-silent threshold (>= -60 dBFS) within the configured lag compensation window
- **THEN** the silence detector treats the audio as captured and does not trigger a silence alert

#### Scenario: Active silence alert clears when OBS captures audio during quiet client periods
- **WHEN** a silence alert is active and OBS captures non-silent audio (`RMS >= -60 dBFS`)
- **THEN** the silence detector immediately resets its counter and clears the `sound issue` alert

### Requirement: Pearson Cross-Correlation Anomaly Detection
The correlation engine SHALL maintain a rolling time-series window of gaming PC telemetry and OBS audio meter RMS values (clamping digital silence floors at `-60.0 dBFS` for correlation metrics) and compute Pearson's correlation coefficient ($r$) between them across the configured audio lag compensation range. A low correlation SHALL only be treated as a mismatch when the directional attenuation (how much quieter OBS is relative to the gaming PC on active samples `> -60 dBFS`, with silent OBS samples `< -60 dBFS` evaluated as full signal loss relative to `-100 dBFS`) also exceeds the configured **audio match tolerance** (in dB) and OBS is not actively clamped against a limiter ceiling (`>= -15 dBFS`) during loud client audio (`>= -25 dBFS`). The tolerance SHALL be user-configurable, default to 20 dB, and range from 0 to 40 dB. When OBS is capturing non-silent audio (`>= -60 dBFS`) and signals either correlate ($r \ge 0.5$), remain within the configured attenuation tolerance (including when OBS is louder than the gaming PC), or reflect limiter compression at high volume, any active correlation issue alert SHALL be cleared and instantaneous silence alerts SHALL be suppressed.

#### Scenario: Audio dynamic mismatch detection
- **WHEN** client audio exhibits dynamic variance (variance > 1.0 dB), the maximum Pearson correlation across the lag range remains low ($r < 0.5$) for 1 consecutive second (10 active readings), and the OBS signal is attenuated below the gaming PC signal by more than the configured tolerance (or OBS is completely silent `< -60 dBFS` while client audio is `> -60 dBFS`)
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Audio signals correlate successfully
- **WHEN** dynamic game audio captured by OBS matches client telemetry with $r \ge 0.5$ within the configured lag window
- **THEN** the engine marks audio health as normal and clears any active `sound issue` alert

#### Scenario: Low correlation within tolerance
- **WHEN** the maximum Pearson correlation is low ($r < 0.5$)—such as with low-volume game audio or when an OBS Limiter filter clamps loud gunshots at `-8.4 dBFS` while quieter game sounds have a `+12` to `+20 dB` positive gain in OBS—and OBS active levels are either louder than the gaming PC or attenuated by no more than the configured tolerance
- **THEN** the engine does not trigger a correlation `sound issue` and clears any active alert

#### Scenario: Silence alert not suppressed when OBS is completely silent
- **WHEN** Full detection mode is active, client audio is active (`> -60 dBFS`), and OBS remains below the silence threshold (`< -60 dBFS`)
- **THEN** correlation evaluation does not suppress the conditional silence detector's `sound issue` alert

#### Scenario: Tolerance changed at runtime
- **WHEN** the user changes the audio match tolerance in the server settings (Dual-PC mode) or client settings (Single-PC mode)
- **THEN** the new value is applied to subsequent evaluations without restarting the application, in both Single-PC and Dual-PC modes

### Requirement: Simple Audio Issue Detection Mode
When `Simple audio issue detection` is enabled, the correlation engine SHALL skip the correlation and RMS-difference evaluation entirely and SHALL report a `sound issue` only when gaming PC audio is active while OBS capture remains silent across the configured audio lag compensation window (the conditional silence detection rules). Toggling `Simple audio issue detection` on or off SHALL immediately reset both the conditional silence detector and Pearson correlation engine states, pending sample buffers, and active alert timestamps. The mode SHALL be disabled by default and applied live in both Single-PC and Dual-PC modes.

#### Scenario: Game audio active while OBS captures nothing in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active (RMS > -60 dBFS) for 1 consecutive second (10 readings) and OBS capture remains below -60 dBFS across the configured lag compensation window
- **THEN** the engine triggers `sound issue` with tooltip "OBS sound capture issue"

#### Scenario: Mismatched but non-silent capture in simple mode
- **WHEN** simple detection is enabled, gaming PC audio is active and OBS captures sound at or above -60 dBFS within the configured lag compensation window, even if its dynamics differ from the gaming PC signal
- **THEN** the engine does not report a `sound issue`

#### Scenario: Simple mode toggled off
- **WHEN** simple detection is disabled again
- **THEN** both silence and correlation detectors, pending buffers, and the last issue timestamp are reset immediately, and correlation and tolerance-based evaluation resume using the saved tolerance value

### Requirement: Audio Lag Compensation Window
The correlation engine SHALL support a user-configurable audio lag compensation window (ranging from 0.0 to 3.0 seconds, default 1.0 second, in 0.1s increments). The lag window SHALL define the lookback and lookahead horizon when resolving OBS meter samples relative to gaming PC timestamps in both Simple and Full detection modes, and in both Single-PC and Dual-PC modes. When a client sample arrives before its forward lag horizon (`sampleTime + lagWindow`) has elapsed on the local clock and no non-silent OBS meter reading has yet been observed, the engine SHALL defer final silence failure for that sample until the lag window elapses or until a non-silent OBS meter reading arrives.

#### Scenario: Runtime adjustment of lag compensation window
- **WHEN** the user updates the audio lag compensation window in settings
- **THEN** the new lag window is applied immediately to subsequent evaluations without restarting the application

#### Scenario: Delay compensation for capture card latency
- **WHEN** OBS capture audio is delayed behind gaming PC audio by 800ms in real time and the lag compensation window is configured to 1.0s or higher
- **THEN** the correlation engine waits for or matches the delayed OBS meter reading within the lag window and does not trigger spurious silence alerts in either Single-PC or Dual-PC mode

### Requirement: Sound Issue Alert Expiration on Retry Timeout
The audio correlation engine SHALL record and update the timestamp of the most recent sound issue detection whenever a full sound issue condition is actively confirmed (via conditional silence detection or cross-correlation mismatch). Brief single-reading spikes following quiet periods that do not satisfy a full confirmation window SHALL NOT refresh the issue timestamp. The active `sound issue` alert state SHALL remain active only during the configured **retry timeout** duration. If the issue is not re-confirmed according to established detection logic within the retry timeout period, the correlation engine SHALL automatically dismiss and clear the `sound issue` alert. Whenever valid non-silent OBS audio (`>= -60 dBFS`) is ingested in Simple mode (or in Full mode without an active correlation mismatch), the active `sound issue` alert and issue timestamp SHALL be cleared immediately. The expiration check SHALL operate reliably even during complete silence when no loopback audio telemetry or OBS audio meters are produced.

#### Scenario: Sound issue expires after retry timeout during source silence
- **WHEN** a `sound issue` alert is triggered and the audio source subsequently remains silent or below active confirmation thresholds without generating further full issue confirmations
- **THEN** the correlation engine automatically clears the `sound issue` alert once the configured retry timeout duration has elapsed from the last issue detection time

#### Scenario: Sound issue refreshed by repeated issue detection within retry timeout
- **WHEN** a `sound issue` alert is active and an audio mismatch or uncaptured active audio condition continues to be actively confirmed before the retry timeout expires
- **THEN** the engine updates the last issue detection timestamp and extends the active alert duration for another full retry timeout period

#### Scenario: Sound issue cleared immediately when audio is detected in OBS
- **WHEN** a `sound issue` alert is active and valid non-silent audio (`>= -60 dBFS`) is detected on the OBS capture source before the retry timeout expires (even if client audio has already dropped below `-60 dBFS`)
- **THEN** the correlation engine resets the silence detector and clears the `sound issue` alert immediately without waiting for the retry timeout duration to elapse

#### Scenario: Runtime adjustment of retry timeout
- **WHEN** the user adjusts the retry timeout in application settings
- **THEN** the new timeout value is applied immediately to subsequent expiration evaluations without requiring an application restart, in both Single-PC and Dual-PC modes
