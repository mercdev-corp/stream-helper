## ADDED Requirements

### Requirement: Audio Issue Detection Controls
The server settings dialog SHALL provide, directly below the OBS game audio capture source dropdown and in this order from top to bottom, a `Simple audio issue detection` switch and an audio match tolerance slider (labelled in dB, range 0 to 40, default 20). Both values SHALL be persisted in `stream-helper-server-settings.json` and applied live.

#### Scenario: Control order and placement
- **WHEN** the server settings dialog is opened
- **THEN** the `Simple audio issue detection` switch appears below the OBS audio capture dropdown and the tolerance slider appears below the switch, with no controls overlapping and 20 pixel right margin preserved

#### Scenario: Enabling simple detection disables the slider
- **WHEN** the user turns on `Simple audio issue detection`
- **THEN** the slider becomes disabled (grayed out) and is displayed at its maximum position, while the saved tolerance value in settings remains unchanged

#### Scenario: Disabling simple detection restores the slider
- **WHEN** the user turns off `Simple audio issue detection`
- **THEN** the slider is enabled and returns to the previously saved tolerance value

#### Scenario: Settings reopened with simple detection on
- **WHEN** the dialog opens with simple detection saved as enabled
- **THEN** the slider is shown disabled at its maximum position and the saved tolerance value is still preserved in the settings file

#### Scenario: Slider adjusted
- **WHEN** the user moves the enabled slider
- **THEN** the new tolerance is saved immediately, its current dB value is shown in the label, and it is applied to the audio engine without restart
