# Laptop Power 1.1.0

- Added tray power-mode selection, checked NVIDIA On/Off controls and a dynamic GPU button.
- Added a single live GPU row for the current power source, USB-C notices in Endurance and improved tray-menu dismissal.
- Added ASUS charge-limit controls with explicit accepted-request wording and a single toggle.
- Save and apply now reapplies the active profile, including its GPU rule, and refreshes results.
- Added a per-user Windows x64 installer, portable ZIP and SHA-256 checksums. Installation preserves user settings, restore records and logs.

Verified: build, embedded-backend verification, tray-click checks, GPU retry/cancellation logic, editor/report ordering and configuration compatibility tests. No new live GPU, charge-limit or brightness changes are required by installation.

Known issues: NVIDIA Code 43 can recur after switching; brightness can remain physically unresponsive despite WMI readback; charge-limit threshold readback and startup/resume persistence are unavailable. AC/battery timeouts are independent. See README.md and README.txt. ASUS controls remain model-dependent. Installers are not code-signed.

Installer verification on November 27, 2026: clean install, same-version upgrade and uninstall passed; installed payload hashes matched the build, and uninstall preserved unrelated source files. In-place upgrade into the existing app folder passed, installed Help matched source, and user configuration/restore records were unchanged. Both updated tray apps restarted successfully. AudioSwitch retained read-only inspection; Laptop Power's embedded-resource verifier passed.

The original application is now MIT licensed. LICENSE is included in Setup and the portable ZIP; existing third-party notices are retained.
