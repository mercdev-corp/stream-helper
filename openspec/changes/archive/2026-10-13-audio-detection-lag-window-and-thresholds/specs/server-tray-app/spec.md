## MODIFIED Requirements

### Requirement: Audio Issue Detection Controls
The server settings dialog SHALL provide, directly below the OBS game audio capture source dropdown and in this order from top to bottom:
1. An audio lag compensation window slider (labelled in seconds, range 0.0s to 3.0s, default 1.0s, step 0.1s)
2. `Simple audio issue detection` switch
3. An audio match tolerance slider (labelled in dB, range 0 to 40, default 20)

All three values SHALL be persisted in `stream-helper-server-settings.json` and applied live to the correlation engine. When `Simple audio issue detection` is enabled, the audio lag compensation slider SHALL remain enabled and active, while the audio match tolerance slider SHALL be disabled.

#### Scenario: Control order and placement
- **WHEN** the server settings dialog is opened
- **THEN** the audio lag compensation slider appears below the OBS audio capture dropdown, the `Simple audio issue detection` switch appears below the lag compensation slider, and the tolerance slider appears below the switch, with 20 pixel right margin preserved

#### Scenario: Enabling simple detection disables the slider
- **WHEN** the user turns on `Simple audio issue detection`
- **THEN** the tolerance slider becomes disabled (grayed out) and is displayed at its maximum position, while the saved tolerance value in settings remains unchanged and the audio lag compensation slider remains enabled

#### Scenario: Disabling simple detection restores the slider
- **WHEN** the user turns off `Simple audio issue detection`
- **THEN** the tolerance slider is enabled and returns to the previously saved tolerance value

#### Scenario: Settings reopened with simple detection on
- **WHEN** the dialog opens with simple detection saved as enabled
- **THEN** the tolerance slider is shown disabled at its maximum position, the saved tolerance value is preserved in settings, and the lag compensation slider is shown enabled with its saved value

#### Scenario: Slider adjusted
- **WHEN** the user moves the enabled tolerance slider
- **THEN** the new tolerance is saved immediately, its current dB value is shown in the label, and it is applied to the audio engine without restart

#### Scenario: Lag compensation slider active in simple mode
- **WHEN** the user enables `Simple audio issue detection`
- **THEN** the audio lag compensation slider remains enabled and adjustable, while the tolerance slider becomes disabled

#### Scenario: Lag compensation slider adjusted
- **WHEN** the user moves the audio lag compensation slider
- **THEN** the new lag window is saved immediately, its current value is displayed in seconds, and it is applied to the audio correlation engine without restart
