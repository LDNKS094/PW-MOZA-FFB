# PW MOZA Flight Feedback

English | [中文](README_zh.md)

Flight force feedback and vibration for Project Wingman through MOZA Cockpit and an AB6 base.

Choose **script installation** or **manual installation**.

## How it works

- **Flight forces:** local flight telemetry is converted to the DCS format. Cockpit App calculates the stick forces.
- **Vibration:** the game’s native rumble is sent to the base, with additional local cannon, damage and destruction events.

The bridge loads the vibration module while the game runs. The game executable and assets stay intact.

## Install

### Requirements

| Dependency | Purpose |
| --- | --- |
| [UE4SS 3.0.1](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/v3.0.1), x64 | Loads the telemetry script |
| [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), Windows x64 | Runs the bridge |
| [MOZA Cockpit](https://www.mozaracing.com/downloads/) and AB6 | Calculates flight forces and drives the base |

Install and configure these dependencies yourself.

After installation, the standard PW directory layout should be:

```text
Project Wingman/ProjectWingman/Binaries/Win64/
  ProjectWingman-Win64-Shipping.exe
  dwmapi.dll
  UE4SS.dll
  Mods/
```

UE4SS may instead be inside `Win64/ue4ss/`; the scripts locate either layout.

### Script install

1. Run Cockpit once to create its preset library, then exit it.
2. Exit PW and this project’s bridge.
3. Run `Install.cmd`.

The script finds the Steam game directory, installs PWTelemetry, merges the mod lists and writes PW Default into Cockpit’s preset library. Reinstalling overwrites this project’s script and preset.

To select the game directory:

```powershell
.\Install.cmd -GameDirectory 'D:\Games\Project Wingman'
```

### Script uninstall

Exit PW, Cockpit and the bridge, then run `Uninstall.cmd`. It removes PWTelemetry, its mod-list entries and this project’s Cockpit preset and match entry.

```powershell
.\Uninstall.cmd -GameDirectory 'D:\Games\Project Wingman'
```

### Manual install

Copy `payload/PWVibration.dll` beside the game executable. DLL loading is independent of the game directory; this project places it there for convenient management.

1. Run Cockpit once to create its preset library, then exit it.
2. Copy `mod/PWTelemetry/` into the active UE4SS `Mods/` directory.
3. Add this entry to `Mods/mods.txt`:

   ```text
   PWTelemetry : 1
   ```

4. Add this object to the array in `Mods/mods.json`, keeping existing entries:

   ```json
   { "mod_name": "PWTelemetry", "mod_enabled": true }
   ```

5. Copy `presets/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset` into `%USERPROFILE%\Documents\MOZA Cockpit\PresetLibrary\`.
6. Add this record to the `presets` array in `PresetIndex.index`. Replace `file_path` with the actual full path:

   ```json
   {
     "date": "1791976000",
     "device_type": "Base",
     "devices": ["AB6"],
     "file_path": "C:/Users/YOUR_USER_NAME/Documents/MOZA Cockpit/PresetLibrary/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset",
     "games": ["dcs_world"],
     "name": "PW Default",
     "product_type": "AS23",
     "uuid": "{5a70e8ad-f59c-4b37-b598-2d8083691f5f}",
     "vehicles": "PW Default",
     "version": "1.0.1"
   }
   ```

7. Add this mapping under `Base.AB6.dcs_world` in `OptimalMatch.vconfig`:

   ```json
   "PW Default": "{5a70e8ad-f59c-4b37-b598-2d8083691f5f}"
   ```

### Manual uninstall

Reverse the manual installation steps.

Keep shared dependencies used by other mods.

## Usage

1. Open Cockpit.
2. Run `DCS.exe`; Cockpit uses the PW Default match.
3. Start PW.

## Key bindings

Bind controller axes and buttons in PW’s control settings. Cockpit’s preset controls flight forces; PW’s bindings control in-game actions.

## Known limitations

All aircraft share one default preset.

Testing covers the AB6 + MHG + MTQ combination.

## Building from source

Requirements: .NET 8 SDK, x64 MSVC and CMake.

Run from the project directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Compiled output:

```text
DCS.exe
payload/
  PWVibration.dll
```

After building, follow the standard installation instructions.

## License

This project is licensed under [GPL-3.0](LICENSE). Bundled third-party components retain their respective licenses.
