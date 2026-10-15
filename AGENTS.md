# Agent Instructions & Project Requirements

## Core Architecture & Settings Requirements
- **Immediate Save & Apply**: All settings adjusted in settings forms or dialogs (including sliders/trackbars, checkboxes, dropdowns, numeric up/downs, and text inputs) must be saved to disk/configuration and applied immediately to the active running services, monitors, and engines upon user interaction. There is no separate "Apply" or "Save" button; all changes must take effect live in real time.

## Documentation Maintenance Requirements
- **Mandatory README Updates for Business Logic Changes**: Whenever application business logic, detection algorithms, timeout behaviors, threshold definitions, or core architecture workflows are added, modified, or removed, agents MUST update `README.md` to accurately document the changes. Code changes that alter business logic without updating the corresponding sections of `README.md` are strictly incomplete.
