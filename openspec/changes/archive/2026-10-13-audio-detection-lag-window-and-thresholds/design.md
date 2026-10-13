## Context

See [proposal.md](proposal.md) for background. Currently, `AudioCorrelationEngine` evaluates OBS meter history by finding the closest sample at $T_{OBS} \approx T_{Client}$, providing no tolerance for physical transmission latency (such as Realtek Stereo Mix "Listen" to HDMI capture cards). Additionally, identical -48.0 dBFS thresholds in `ConditionalSilenceDetector` cause low-volume scenes to trigger false silence alerts whenever capture levels are slightly attenuated.

## Goals / Non-Goals

**Goals:**
- Provide a configurable audio lag compensation window slider (0.0s to 3.0s, default 1.0s) in both Server Settings (Dual-PC) and Client Settings (Single-PC).
- Support lag compensation in both Simple detection mode and Full correlation mode.
- In Simple Mode, evaluate OBS silence by checking whether OBS captured non-silent audio ($\ge -60.0$ dBFS) anywhere within the lag window $[T, T + \text{lagWindow}]$.
- Prevent low-volume scene false alerts by restoring the hysteresis gap between client active threshold ($-48.0$ dBFS) and OBS silence threshold ($-60.0$ dBFS).
- In Full Mode, scale Pearson correlation lag search steps to cover the configured lag window and decouple full mode from instantaneous silence alerts when correlation matches.
- Immediately flush correlation history and active alerts on OBS audio input device switch.

**Non-Goals:**
- Automatic lag measurement via acoustic watermarking or cross-correlation phase shift (manual slider gives full user control and predictable behavior).
- Modifying WASAPI loopback capture or network UDP packet batching rates.

## Decisions

### 1. Lag Window Lookup Strategy in `AudioCorrelationEngine`
- **Approach**: For each client sample at `sampleTimeMs`, when evaluating whether OBS is silent, check whether any OBS meter reading in the interval $[ \text{sampleTimeMs}, \text{sampleTimeMs} + \text{lagWindowMs} ]$ has $RMS \ge \text{obsSilenceThresholdDbfs}$ ($-60.0$ dBFS).
- **Single-PC mode**: Store incoming OBS readings in `_obsMeterHistory` in Single-PC mode as well, allowing the same windowed search rather than checking only the instantaneous `_latestObsRmsDbfs`.
- **Rationale**: An audio burst on the client reaching OBS anytime within the lag window confirms that sound is flowing to OBS. Searching the forward window completely eliminates the false silence accumulation that occurs when audio takes 400ms–1500ms to reach OBS.

### 2. Threshold Hysteresis Gap
- **Client Active Threshold**: Keep at $-48.0$ dBFS (prevents menus/loading screen ambient noise from triggering monitoring).
- **OBS Silence Threshold**: Move from $-48.0$ dBFS to $-60.0$ dBFS.
- **Rationale**: Provides a 12 dB safety buffer. Low-volume game audio at $-45$ to $-48$ dBFS attenuated by 2–4 dB in Windows "Listen" or OBS input faders arrives at $-47$ to $-52$ dBFS in OBS, which is comfortably above $-60$ dBFS and will not falsely register as dead silence.

### 3. Pearson Correlation Lag Steps Scaling & Full Mode Decoupling
- In `PearsonCorrelationEngine`: dynamically update `_maxLagSteps = Math.Max(5, (int)Math.Ceiling(lagWindowSeconds * 10))` whenever lag window changes (e.g. 1.0s = 10 steps, 3.0s = 30 steps).
- In `AudioCorrelationEngine.EvaluateSoundIssue()`:
  - If `!simpleMode` and Pearson correlation is active with high correlation ($r \ge 0.5$ or difference within tolerance), do not trigger `HasSoundIssue` from `_silenceDetector` alone.
  - Full mode only triggers when signals genuinely diverge (low correlation and high difference) or when OBS is silent across the entire lag window.

### 4. UI Control Layout & `AudioDetectionSettingsController` Extension
- Extend `AudioDetectionSettingsController` to manage the lag window slider (`_trkAudioLagWindow`, `_lblAudioLagWindowVal`), with bounds 0 to 30 (representing 0.0s to 3.0s in 0.1s steps).
- The lag window slider remains **enabled** in both Simple and Full detection modes, because latency compensation is critical for both modes.
- TrackBar position order directly below `OBS Game Audio Capture Source`:
  1. `[x] Simple audio issue detection`
  2. `Audio lag compensation window:` `[======|======] 1.0 s`
  3. `Audio match tolerance:` `[========|====] 20 dB` (disabled when Simple mode is active)
- Increase `ServerSettingsForm` and `ClientSettingsForm` dialog client size height appropriately (by ~50px) to maintain the required 20px padding and prevent control crowding.

### 5. Settings Persistence
- Add `AudioLagWindowSeconds` (double, default `1.0`, clamped `0.0` to `3.0`) to `ServerSettings` and `ClientSettings`.
- Serialize and deserialize with other audio detection settings.

### 6. Correlation Engine Reset on Device Switch
- In `ServerTrayApplicationContext` and `ClientTrayApplicationContext`:
  Call `_correlationEngine.Reset()` inside `onObsAudioDeviceChanged` callback.

## Risks / Trade-offs

- **[Risk] High lag window (e.g. 3.0s) delays legitimate silence alert detection**
  → *Mitigation*: The maximum additional delay before alerting true silence is bounded by the lag window (e.g. 3s consecutive requirement + 1s lag window = 4s total). This is well within typical stream monitoring tolerance. Defaulting to 1.0s provides a sweet spot for capture card setups.
- **[Risk] Dialog layout clipping on DPI scaling**
  → *Mitigation*: Dynamically calculate control Y-positions and expand dialog heights with 20px right margin padding guarantees.
