## MODIFIED Requirements

### Requirement: Context Menu Navigation
The server application SHALL provide a right-click context menu on the system tray icon with Pause/Resume, Settings, Donate, and Exit options, with Donate positioned immediately below Settings.

#### Scenario: Toggle pause and resume
- **WHEN** the user clicks Pause or Resume in the context menu
- **THEN** the server toggles its paused state, updates the tray icon, saves the paused setting immediately, suspends microphone and OBS monitoring while pausing broadcasting, and resumes monitoring services and broadcasting when unpaused

#### Scenario: Open settings dialog
- **WHEN** the user clicks Settings in the context menu
- **THEN** the settings window opens while background broadcasting continues uninterrupted

#### Scenario: Open donation webpage
- **WHEN** the user clicks Donate in the context menu
- **THEN** the application opens the donation URL from the embedded DONATE resource in the default web browser

#### Scenario: Exit application
- **WHEN** the user clicks Exit in the context menu
- **THEN** the application cleanly unregisters callbacks, closes the tray icon, and terminates
