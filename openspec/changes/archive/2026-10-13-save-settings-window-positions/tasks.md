## 1. Shared Window Position Helper

- [x] 1.1 Implement `WindowPositionHelper` in `StreamHelper.Shared.UI` with display boundary validation (`IsOnScreen`) and verify with unit tests covering valid coordinates, multi-monitor coordinates, and off-screen coordinates.

## 2. Configuration Models and Serialization

- [x] 2.1 Add `SettingsWindowX` and `SettingsWindowY` nullable properties to `ClientSettings` and update `ClientSettingsJsonContext` to ensure serialization and deserialization round-trip cleanly.
- [x] 2.2 Add `SettingsWindowX` and `SettingsWindowY` nullable properties to `ServerSettings` and update `ServerSettingsJsonContext` to ensure serialization and deserialization round-trip cleanly.

## 3. Client Settings Window Position Management

- [x] 3.1 Update `ClientSettingsForm` to restore saved window coordinates on initialization using `WindowPositionHelper` (falling back to screen center if missing or off-screen), and persist coordinates to `stream-helper-client-settings.json` on `LocationChanged` when loaded.
- [x] 3.2 Add unit tests in `ClientOverlayAndSettingsTests` verifying `ClientSettingsForm` position restoration, immediate persistence upon repositioning, and off-screen fallback behavior.

## 4. Server Settings Window Position Management

- [x] 4.1 Update `ServerSettingsForm` to restore saved window coordinates on initialization using `WindowPositionHelper` (falling back to screen center if missing or off-screen), and persist coordinates to `stream-helper-server-settings.json` on `LocationChanged` when loaded.
- [x] 4.2 Add unit tests in `ServerNetworkingAndSettingsTests` verifying `ServerSettingsForm` position restoration, immediate persistence upon repositioning, and off-screen fallback behavior.

## 5. Verification and Integration

- [x] 5.1 Build the solution in Release mode and run unit test suite to verify all client and server settings tests pass without regression.
