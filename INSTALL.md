# Installing Laptop Power

Download the Windows x64 Setup executable from [the latest release](https://github.com/cyclicredundancymachine/LaptopPower/releases/latest). Close Laptop Power using its tray Exit command, then run Setup. The installer is not code-signed; verify its SHA-256 against SHA256SUMS.txt from the same release.

Setup installs for the current Windows user, creates a Start menu shortcut and an optional desktop shortcut, and registers an uninstaller in Windows Installed apps. Administrator rights are not required for installation. The default location is `%LOCALAPPDATA%\Programs\LaptopPower`; an existing setup installation keeps its previous folder. For a portable installation, choose the folder containing the existing executable to upgrade it in place. Keep one active installation per PC.

Setup does not enable sign-in startup or change power plans, GPU rules, charge limits or saved configuration. Opening the app retains its normal behavior. Existing startup shortcuts should point at the chosen installation folder; installing over the current folder preserves those paths.

To uninstall, exit the tray app, then use Windows Settings > Apps > Installed apps > Laptop Power. Uninstall removes installed application files and installer-created shortcuts. It preserves `%LOCALAPPDATA%\\PeteLaptopPower` configuration, restore records and logs. Restore any desired Windows settings through the app before uninstalling; uninstall does not reset hardware or power settings.

The portable ZIP remains available and contains the same application files as Setup. Extract the whole ZIP into a writable folder. Do not mix files from different releases.

## Building Setup

Install [Inno Setup 7](https://jrsoftware.org/inno-setup/downloads). Run `Build-Release.ps1 -IsccPath 'C:\path\to\ISCC.exe'` from Windows PowerShell 5.1. The Windows .NET Framework compiler builds the x64 application and its embedded backend. Output is written to the ignored `artifacts` folder: a versioned Setup EXE, a portable ZIP and SHA256SUMS.txt. VERSION supplies the release version.


Both Setup and portable downloads include the application MIT license. AudioSwitch also includes its separate third-party MIT notice.
