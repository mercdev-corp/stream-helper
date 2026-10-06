## MODIFIED Requirements

### Requirement: OBS Connection and Health Configuration Controls
The server settings dialog SHALL provide controls for configuring the OBS Studio WebSocket connection (IP address input default `127.0.0.1`, port input default `4455`, password with reveal/hide toggle), a skipped frames threshold field labelled "Skipped frames threshold:" without "(per min)", a numeric input field for the evaluation period in seconds (default 5, range 1 to 300), and a dropdown for selecting the OBS game audio capture device with red strikethrough styling for missing sources.

#### Scenario: Configure OBS connection parameters
- **WHEN** the user updates the OBS IP, port, or password in the server settings dialog
- **THEN** the settings are saved immediately and the OBS monitor attempts to connect or re-authenticate using the new credentials

#### Scenario: Configure skipped frames threshold and evaluation period
- **WHEN** the user modifies the skipped frames threshold or evaluation period seconds in the server settings dialog
- **THEN** the new threshold and period values are saved to `stream-helper-server-settings.json` and applied to the OBS monitor immediately without restarting the application

#### Scenario: Missing OBS audio capture device in settings dropdown
- **WHEN** the server settings dialog opens and the saved OBS audio source is not found in OBS Studio
- **THEN** the missing device is rendered as the first item with red strikethrough styling, followed by all available OBS audio inputs
