## MODIFIED Requirements

### Requirement: Single PC Audio Issue Detection Controls
In Single PC mode, the client settings dialog SHALL provide, directly below the OBS game audio capture source dropdown and in this order from top to bottom:
1. An audio lag compensation window slider (labelled in seconds, range 0.0s to 3.0s, default 1.0s, step 0.1s)
2. `Simple audio issue detection` switch
3. An audio match tolerance slider (labelled in dB, range 0 to 40, default 20)

All three values SHALL be persisted in `stream-helper-client-settings.json` and applied live to the in-process audio correlation engine. When `Simple audio issue detection` is enabled, the audio lag compensation slider SHALL remain enabled and active, while the audio match tolerance slider SHALL be disabled.

#### Scenario: Single PC control order and placement
- **WHEN** the client settings dialog is opened in Single PC mode
- **THEN** the audio lag compensation slider appears below the OBS audio capture dropdown, the `Simple audio issue detection` switch appears below the lag compensation slider, and the tolerance slider appears below the switch, with 20 pixel right margin preserved

#### Scenario: Enabling simple detection disables the slider in Single PC mode
- **WHEN** the user turns on `Simple audio issue detection` in Single PC mode
- **THEN** the tolerance slider becomes disabled (grayed out) and is displayed at its maximum position, while the saved tolerance value in settings remains unchanged and the lag compensation slider remains enabled

#### Scenario: Disabling simple detection restores the slider in Single PC mode
- **WHEN** the user turns off `Simple audio issue detection` in Single PC mode
- **THEN** the tolerance slider is enabled and returns to the previously saved tolerance value

#### Scenario: Single PC settings reopened with simple detection on
- **WHEN** the dialog opens in Single PC mode with simple detection saved as enabled
- **THEN** the tolerance slider is shown disabled at its maximum position, the saved tolerance value is preserved in settings, and the lag compensation slider is shown enabled with its saved value

#### Scenario: Single PC tolerance slider adjusted
- **WHEN** the user moves the enabled slider in Single PC mode
- **THEN** the new tolerance is saved immediately, its current dB value is shown in the label, and it is applied to the local audio engine without restart

#### Scenario: Lag compensation slider active in simple mode
- **WHEN** the user enables `Simple audio issue detection` in Single PC mode
- **THEN** the audio lag compensation slider remains enabled and adjustable, while the tolerance slider becomes disabled

#### Scenario: Single PC lag compensation slider adjusted
- **WHEN** the user moves the audio lag compensation slider in Single PC mode
- **THEN** the new lag window is saved immediately, its current value is displayed in seconds, and it is applied to the local audio engine without restart
