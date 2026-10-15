## MODIFIED Requirements

### Requirement: OBS Studio WebSocket v5 Connection and Reconnection
The OBS monitor SHALL maintain an authenticated WebSocket connection to OBS Studio v5 using configured host, port, and password parameters, and automatically attempt reconnections on connection failure or drop using the configured retry timeout. Connection retry attempts SHALL be executed using a single-flight mechanism that prevents overlapping tasks, token disposal collisions, or socket leakage. When the application launches before OBS Studio, the monitor SHALL continue periodic single-flight retries every retry timeout period until OBS starts.

#### Scenario: Successful connection to OBS Studio
- **WHEN** the server launches or reconnect timer elapses and OBS Studio is running with WebSocket server enabled
- **THEN** the OBS monitor authenticates, establishes an active session, and clears any `obs disconnected` alerts

#### Scenario: OBS Studio not running or unreachable
- **WHEN** the WebSocket connection attempt fails or disconnects unexpectedly
- **THEN** the monitor marks OBS state as disconnected, broadcasts `obs disconnected` with tooltip "OBS is not running", and schedules a single reconnect attempt based on the retry timeout

#### Scenario: Periodic reconnection retry when launched before OBS Studio
- **WHEN** the application starts before OBS Studio is launched
- **THEN** the OBS monitor periodically executes single-flight connection attempts every retry timeout period without accumulating concurrent tasks, and automatically connects, authenticates, and clears the `obs disconnected` alert once OBS Studio starts

#### Scenario: Runtime adjustment of retry timeout
- **WHEN** the user adjusts the retry timeout setting in application settings
- **THEN** the updated retry timeout is applied immediately to subsequent reconnection intervals without restarting the application, in both Server and Single-PC Client modes

#### Scenario: OBS monitoring suspension during pause
- **WHEN** application monitoring is paused
- **THEN** active connection attempts and polling are suspended, and upon resuming, connection attempts are restarted cleanly
