#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif

[Setup]
AppId=LaptopPower
AppName=Laptop Power
AppVersion={#AppVersion}
AppPublisher=cyclicredundancymachine
AppPublisherURL=https://github.com/cyclicredundancymachine/LaptopPower
AppSupportURL=https://github.com/cyclicredundancymachine/LaptopPower/issues
AppUpdatesURL=https://github.com/cyclicredundancymachine/LaptopPower/releases/latest
DefaultDirName={localappdata}\Programs\LaptopPower
DefaultGroupName=Laptop Power
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
AppMutex=Local\PeteLaptopPowerNativeV2
SetupMutex=Local\LaptopPowerSetup
OutputDir={#ReleaseDir}
OutputBaseFilename=LaptopPower-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\LaptopPower.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\LaptopPower.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Laptop Power"; Filename: "{app}\LaptopPower.exe"
Name: "{group}\Help"; Filename: "{app}\README.txt"
Name: "{autodesktop}\Laptop Power"; Filename: "{app}\LaptopPower.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LaptopPower.exe"; Description: "Open Laptop Power"; Flags: nowait postinstall skipifsilent

; Only explicitly installed files are removed. User settings, source, evidence,
; and unrelated files in a custom installation directory are never deleted.
