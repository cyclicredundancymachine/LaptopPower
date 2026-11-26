LAPTOP POWER

Endurance saves battery. Balanced is for everyday use. Boost favors performance.
Use Mode settings to customize the profiles and Verify & fix to check and reapply the active mode when settings differ.
Save and apply immediately applies the active Laptop Power profile and refreshes
the results. Changes to other profiles take effect when you select them.

The tray number is the current battery percentage.
Tray colors: green = ASUS barrel adapter, yellow = USB-C power, red = on battery,
gray = source unrecognized.
Colors identify charger type.

Unplugging or connecting USB-C power offers a 30-second switch to Endurance.
Dismiss or open the app to cancel. If Endurance is already active, unplugging
shows a notice without a countdown.
Connecting USB-C while Endurance is already active also shows a connection notice.
Connecting the ASUS charger offers a 30-second switch to Balanced. Dismiss or
open the app to cancel. If Balanced is already active, it shows a connection notice.
If the battery keeps draining while plugged in, ignore, snooze, or switch to Endurance.

Hover or single-click for mode, battery percentage, power source, and NVIDIA state.
Double-click to open Laptop Power.
Right-click and choose Power mode to switch profiles. A check marks the active mode.
Close the window to keep the app in the tray. Right-click the icon and choose
Exit Laptop Power to quit.

BATTERY CHARGE LIMIT
The main window has one button that alternates between Enable 80% limit and
Disable 80% limit based on the last accepted request this session. The Battery
charge limit tray submenu offers both commands. Disabling requests up to 100%.
ASUS accepted means the firmware acknowledged your request. The current limit
cannot be read back on the Strix. The menu check marks the last accepted request
in this session. The app does not reapply it at startup. If already over 80%,
the battery may hold that level until you use it unplugged.

NVIDIA GPU
Use Turn NVIDIA on or Turn NVIDIA off beside the live GPU status to override
the current mode and power source. The button offers the opposite action when
the GPU state is confirmed. The window shows On (verified) after ASUS and Windows verify it.
If verification fails, the main window shows the reason.
In Mode settings, choose Keep current, On, or Off for each power source.
The NVIDIA GPU tray submenu lists On and Off. A check marks the verified state.
New profiles default to Keep current. The button and tray commands temporarily
override the profile until you change modes or power sources.
Close GPU workloads before switching off. GPU-off requires hybrid graphics and
a display that does not use NVIDIA. Laptop Power does not close apps for you.

To share, copy LaptopPower.exe and this README. ASUS controls are model-dependent.
Results list NVIDIA first, followed by CPU maximum/boost, screen brightness,
efficiency, CPU minimum, and timeouts. The NVIDIA row shows the current power
source and GPU state. The row turns red if the power source is unknown.
Verify & fix also restores the saved NVIDIA rule if a tray override was active.
NVIDIA switching retries transient failures up to three times, then warns once.
Changing mode or charger cancels retries. Unsupported display routing is not retried.
Keyboard brightness and ASUS thermal presets are no longer managed.
If Windows reports an NVIDIA driver problem, the live status shows its error code.
A firmware On request is verified only when the Windows device is healthy.

INSTALLATION AND UPDATES
Windows x64 Setup and a portable ZIP are available at:
https://github.com/cyclicredundancymachine/LaptopPower/releases/latest
Exit Laptop Power before updating. Setup adds a Start menu shortcut and an
uninstall entry. It preserves configuration and restore records, does not apply
profiles, and does not enable sign-in startup. Choose the current app folder to
upgrade a portable copy. Uninstall through Windows Installed apps; settings remain.

KNOWN ISSUES
NVIDIA Code 43 has recurred after GPU switching, including with ASUS 591.97.
Reboot recovery does not establish a permanent fix. Physical brightness can fail
even when WMI reports a change. Charge-limit requests are not reapplied at startup
or resume; the firmware threshold cannot be read back. Charge above 80% after
wake remains unexplained. AC and battery timeouts are independent: -1 keeps the
Windows value, 0 means Never. Installation does not replace your configuration.
