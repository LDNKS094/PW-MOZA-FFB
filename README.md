# PW MOZA Flight Feedback

English | [中文](README_zh.md)

Flight force feedback and vibration for Project Wingman through MOZA Cockpit and an AB6 base.

Choose **script installation** or **manual installation**.

## How it works

- **Flight forces:** local flight telemetry is converted to the DCS format. Cockpit calculates the stick forces using PW Default.
- **Vibration:** the game’s native rumble is sent to the base, with additional local cannon, damage and destruction events.

The bridge loads `payload/PWVibration.dll` while the game runs. The game executable and assets stay intact.

## Install

### Requirements

| Dependency | Purpose |
| --- | --- |
| [UE4SS 3.0.1](https://github.com/UE4SS-RE/RE-UE4SS/releases/tag/v3.0.1), x64 | Loads the telemetry script |
| [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), Windows x64 | Runs the bridge |
| [MOZA Cockpit](https://www.mozaracing.com/downloads/) and AB6 | Calculates flight forces and drives the base |

Install these dependencies first. The standard PW layout is:

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

You can also pass the `ProjectWingman/Binaries/Win64` directory. Use `-WhatIf` to preview the changes.

### Script uninstall

Exit PW, Cockpit and the bridge, then run `Uninstall.cmd`. It removes PWTelemetry, its mod-list entries and this project’s Cockpit preset and match entry.

```powershell
.\Uninstall.cmd -GameDirectory 'D:\Games\Project Wingman'
```

Shared loaders, other mods, input bindings and other presets remain installed.

### Manual install

1. Run Cockpit once, then exit Cockpit, PW and the bridge.
2. Copy `mod/PWTelemetry/` into the active UE4SS `Mods/` directory.
3. Add this entry to `Mods/mods.txt`:

   ```text
   PWTelemetry : 1
   ```

4. Add this object to the array in `Mods/mods.json`, keeping existing entries:

   ```json
   { "mod_name": "PWTelemetry", "mod_enabled": true }
   ```

5. Copy `presets/{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset` into your Documents folder’s `MOZA Cockpit/PresetLibrary/`.
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

Delete `Mods/PWTelemetry/` and remove its entries from `mods.txt` and `mods.json`.

In Cockpit’s preset library, delete `{5a70e8ad-f59c-4b37-b598-2d8083691f5f}.preset`, remove its UUID from `PresetIndex.index`, and remove the PW Default mapping pointing to that UUID from `OptimalMatch.vconfig`.

Keep shared dependencies used by other mods.

## Usage

1. Open Cockpit.
2. Run `DCS.exe`; Cockpit uses the PW Default match.
3. Start PW.

The window reports game, telemetry, Cockpit and vibration-module status when it changes. Normal status is not saved to log files. Press Ctrl+C to exit. `DCS.exe --vibration-only` enables vibration without the Cockpit connection.

Restarting the bridge reconnects to the running game. The bridge also waits for the next game session after PW exits. Updating the vibration module takes effect on the next PW launch.

Errors are written only when they occur: bridge errors go to `logs/`, vibration errors to `payload/`, and telemetry errors to the installed script’s `Scripts/telemetry-errors.log`.

## Rumble settings

Global gain is **1.5**, with the output capped at full strength. Sources use the strongest current amplitude rather than adding their strengths.

| Effect | Carrier period | Envelope |
| --- | --- | --- |
| Native rumble | 60/70 ms | Game-controlled |
| Cannon | 28 ms | 80 ms decay, refreshed by each shot |
| Light damage | 35 ms | 200 ms decay |
| Heavy damage | 80 ms | 200 ms peak hold + 600 ms decay |
| Destruction | 80 ms | 200 ms full-strength hold + 600 ms decay |

Heavy damage starts at a single health loss of 20% of the previous health value; its strength follows the loss ratio. Parameters are compiled into the program. Rebuild to change them.

Pausing, leaving the game window or exiting the bridge stops vibration. Hardware requests retain a 150 ms expiration limit.

## Key bindings

Bind controller axes and buttons in PW’s control settings. Cockpit’s preset controls flight forces; PW’s bindings control in-game actions. Installation keeps `Input.ini` intact.

## Known limitations

- Vibration output targets one MOZA AB6 base. All aircraft use PW Default.
- The native module supports the verified Steam Windows game build; game updates may require adaptation.
- Native dynamic sources share mixed channels, so they cannot be tuned using AC7’s individual effect IDs.

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

`DCS.exe` loads the native vibration module from `payload/PWVibration.dll`. Build intermediates and publish staging stay under `development/`. After building, follow the installation instructions above.

To package the current build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

This creates `releases/PW-MOZA-FFB-<version>.zip` and its `.sha256` file. The archive contains the bridge, vibration module, mod, preset, installation scripts, both READMEs and licenses.

## License

This project is licensed under [GPL-3.0](LICENSE). Telemetry mapping, vibration policy and deployment handling draw on the GPL-3.0 AC7-MOZA-FFB reference. MinHook retains its [full license and bundled disassembler notices](licenses/MinHook-LICENSE.txt).
