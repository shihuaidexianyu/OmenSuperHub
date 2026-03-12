# OmenSuperHub Project Structure

This project now uses a `src/` layout to keep responsibilities separated and reduce coupling.

## Layout

- `src/App/`
  - `Program.cs`: tray app lifecycle, hardware polling loop, persistence, and app-level orchestration.
- `src/UI/`
  - `MainForm.cs`: WPF-based main settings window.
  - `FloatingForm.cs`: WinForms overlay window.
  - `Legacy/HelpForm.cs`: legacy standalone help window (kept for reference, not compiled).
- `src/Hardware/`
  - `OmenHardware.cs`: low-level BIOS/WMI hardware interfaces.
- `src/Core/Models/`
  - `DashboardModels.cs`: shared telemetry and dashboard data models used by UI/app layers.

## Module Boundaries

- `Program` should expose only stable app-facing APIs for UI calls (`Apply*Setting`, `GetDashboardSnapshot`).
- `MainForm` should avoid direct hardware calls; it should consume snapshot data and invoke `Program` APIs.
- `OmenHardware` should remain focused on hardware transport/encoding and not contain UI concerns.

## Maintenance Notes

- Add new UI pages under `src/UI`.
- Add new low-level hardware commands under `src/Hardware`.
- Add cross-layer DTOs under `src/Core/Models`.
- Keep `Legacy` code out of compilation unless intentionally re-enabled.
