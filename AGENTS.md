# Working on Laptop Power

- Build on Windows x64 with Windows PowerShell 5.1. Run `Build.ps1`, `Test-App.ps1` and `Test-Configuration.ps1` from this folder.
- `NativeApp.cs` owns startup and the tray. `PowerWindow.cs` and `SettingsWindow.cs` own the UI. `PowerWatch.cs` manages live status and GPU requests. `PowerBackend.ps1` manages Windows plans and brightness. The build embeds the backend and its support files.
- Reuse the GPU override and verification paths. A firmware response alone cannot establish success. Preserve the power-source, mode and display-routing guards. Coordinate live GPU tests with the user before changing the hardware state.
- Keep README.md and GUI Help (README.txt beside the EXE) in sync. Preserve compatibility with older configuration files.
- Keep source, build scripts and tests in version control. Keep user settings, restore records, logs, binaries and driver packages out of the repository.

- VERSION is the release version. Build-Release.ps1 creates Setup, portable ZIP and SHA256SUMS.txt using Inno Setup 7; see INSTALL.md. Run release scripts with Windows PowerShell 5.1. Test clean install, upgrade and uninstall before publishing. Keep installed Help synchronized with README.txt.
- Never publish local diagnostics, device identifiers, user configuration, credentials or the internal handoff. Check tracked history before changing repository visibility. Release publication and visibility follow the user's current authorization.
