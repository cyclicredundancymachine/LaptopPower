# Laptop Power

Build or obtain LaptopPower.exe and keep README.txt next to it for the Help button. Open the executable on Windows x64. No installer is required. Saved settings and sign-in startup are configured separately on each PC.

## Everyday controls

- Choose **Endurance**, **Balanced**, or **Boost**. Their default Windows CPU maximum settings are 40%, 80%, and 100%. These are policy values, not measured CPU usage or performance.
- Use **Mode settings** to customize the profiles. **Verify & fix** checks the active Laptop Power mode, reapplies its saved settings when mismatches are found, and verifies NVIDIA. It also clears a temporary GPU override for the current source. If another Windows plan is active, select a Laptop Power mode first. **Restore saved settings** returns to the saved Windows plan and attempts to restore brightness and the Windows display settings.
- Hover or single-click the tray icon for the active mode, battery percentage, power source, and NVIDIA state. Double-click to open the app. Closing the window keeps it in the tray. Choose **Exit** in the tray menu to quit.
- The icon shows the current battery percentage. Its number is green on the ASUS barrel charger, yellow on USB-C, red on battery, and gray when the source cannot be identified. These are charger types, not wattage measurements.

## Power alerts and NVIDIA

Unplugging shows an alert. If Endurance is inactive, the alert offers a 30-second switch. If it is active, the notice is informational. Connecting USB-C can offer the same countdown when Endurance is inactive. Dismiss, open the app, or change the power source to cancel.

Connecting the ASUS barrel charger offers a 30-second switch to Balanced with Switch now, Dismiss, and Open Laptop Power. Already-active Balanced gets a connection notice. Changing the charger cancels its pending countdown. Existing connections at app startup are treated as the starting state.

A separate warning can appear when Windows reports sustained battery drain while plugged in. Ignore silences the episode. Remind me offers 5, 15, 30, or 60 minutes. Switching to Endurance is optional.

Each mode has NVIDIA settings for battery, USB-C, and the ASUS barrel charger. **Keep current** leaves the GPU as it is. **Turn NVIDIA on** beside the live status and the tray GPU commands temporarily override the current rule. The override ends when changing mode or power source. **On (verified)** confirms ASUS firmware and a healthy NVIDIA device in Windows. Close GPU workloads before switching off. The app blocks GPU-off when hybrid graphics or safe display routing cannot be confirmed.

## Compatibility and maintenance

The Windows power-plan features are portable. Charger detection and GPU switching depend on supported ASUS hardware and the ASUS System Control Interface driver. The app was exercised on an ASUS ROG Strix G16 G614PW. NVIDIA on/off was verified in Windows. Keyboard brightness and ASUS thermal presets were removed because their results cannot be reliably read back. The app does not manage custom fan curves. Legacy config and restore fields for those controls are ignored.

Armoury Crate can replace the active Windows plan. The app reports the plan Windows is actually using. The USB-C reconnect test kept Endurance active after Armoury Crate was removed.

Build with Build.ps1 using the Windows .NET Framework compiler. The executable extracts PowerBackend.ps1 and its support files into %LOCALAPPDATA%\PeteLaptopPower. The same folder contains config.json, the restore record, last-report.json, and app.log. Keep the restore record until any pending restore is complete. Test-Configuration.ps1 checks profile persistence and validation.

## Verification and retries

The heading and title bar show the actual active plan. Reports show NVIDIA first, then CPU maximum and boost, screen brightness, efficiency, CPU minimum, and timeouts. NVIDIA rows include the battery, USB-C, and ASUS-charger rules. Inactive rules are marked Not active.

Transient NVIDIA failures are retried up to three times with two-second gaps. Each attempt waits up to ten seconds for Windows to confirm a command. A mode or charger change cancels retries. An unsupported control or unsafe display routing stops immediately. The app logs attempts and displays one final failure notification.

From Windows PowerShell 5.1, run:

```powershell
.\Build.ps1
.\Test-App.ps1
.\Test-Configuration.ps1
```

The checks cover tray clicks, GPU retry behavior, editor ordering and configuration compatibility. Live GPU switching requires a separate hardware test. On the tested Strix G16, replacing NVIDIA 617.42 with ASUS 591.97 and rebooting cleared Code 43, and subsequent switching tests passed. Use the graphics driver for the machine running the app. Start with **Keep current** when trying a different model.

See [AGENTS.md](AGENTS.md) for maintenance instructions.
