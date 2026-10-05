# DisplayFX

[Download the latest Windows installer](https://github.com/AhashR/DisplayFX/releases/latest).

DisplayFX manages per-monitor color profiles and physical backlight brightness on Windows. Gamma-ramp brightness, contrast and gamma work with compatible display drivers; NVIDIA Digital Vibrance requires an NVIDIA GPU. Physical brightness controls require a monitor with DDC/CI enabled.

## Using the app

- Select a monitor tab, then a profile. Create up to five profiles per monitor with **+**. Right-click a profile to duplicate, rename, export or remove it.
- Adjust brightness, contrast, vibrance and gamma. The **play** icon applies a profile or previews edits, **undo** reverts edits, and **save** stores the draft. Profile shortcuts and executable links are optional; all edits remain isolated until saved. Use **Link application** to choose an executable. Its profile applies while that app is in the foreground, then the previously active profile returns when you leave it.
- The toolbar provides **identify displays**, **refresh** and **settings**. Hover an icon for its label. Settings contains complete backup and restore. Restore merges profiles while retaining existing monitor identity, labels and remembered brightness; imports exceeding five profiles fail without changing settings.
- Right-click a monitor tab to rename it. Disconnected displays retain profiles, and drafts survive reconnection during the same session.
- Left-click the tray icon for brightness sliders. The **link** icon links their levels, **open app** opens the main DisplayFX screen, **power** turns displays off, and **gear** opens settings. Normal rows show only the monitor name, percentage and slider; errors appear as a small warning with details in its tooltip.
- Brightness writes run outside the UI thread and coalesce rapid input to the latest requested value. Requests to one monitor are serialized and use its working API and cached range. Transient failed writes get one automatic retry. Closing the popup flushes pending input, and only successful writes are remembered. A sleeping or temporarily unreadable monitor keeps its last known level. Controls reconnect automatically after wake; a completely unknown level displays a dash instead of an invented 50%.
- The main app and Settings use the standard Windows title bar: drag the title bar to move either window. Settings has a compact layout and no scrolling. Large daily-time lists use small pages. Optionally enable brightness shortcuts, set their step, and add daily `HH:mm` times and brightness levels. Save these controls with the save icon inside that section. Startup and tray preferences save immediately. Default shortcuts are Ctrl+Alt+Page Up / Page Down with a 10% step; shortcuts and schedules are off initially. After sleep, only the latest due scheduled level applies.
- Shortcut conflicts appear beside the controls. Shortcuts are temporarily released while recording. Individual profile import is removed. Old executable links remain readable; transient app-switch state restores the manual profile on startup.

## Requirements

- Windows 10 or 11, x64.
- For source builds, the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) with the Windows desktop runtime. `global.json` selects SDK 10.0.401 or a newer .NET 10 feature band.
- The installer contains the .NET desktop runtime. No separate runtime installation is needed.
- A compatible external monitor and enabled DDC/CI for physical brightness; an NVIDIA GPU for Digital Vibrance.

## Build and verify

From the repository root:

```powershell
dotnet restore DisplayFX.sln
dotnet build DisplayFX.sln -c Release
dotnet run --project tests\DisplayFX.RegressionTests -c Release --no-build
```

The regression harness uses fake displays and isolated settings files. Window-movement checks create transparent windows off screen, verify native title-bar hit testing and the Windows Move command, then verify a real position change; they do not move the pointer or operate the running app. It covers migration and recovery, draft isolation, backup validation and capacity, migration of removed switching metadata, hotkey conflicts, brightness range conversion, transport fallback, concurrent writes and rapid slider input, daily schedules, label persistence, topology metadata, native structure layouts, restart rollback, tray text limits, detached WPF rendering, slider interaction and asynchronous popup closure. It does not change physical monitor settings, write startup registry values, or launch the live app.

To save view previews for layout inspection:

```powershell
dotnet run --project tests\DisplayFX.RegressionTests -c Release --no-build -- --render-ui artifacts\ui
```

For optional read-only diagnostics on attached hardware:

```powershell
dotnet run --project tests\DisplayFX.RegressionTests -c Release --no-build -- --diagnose-brightness
```

This separate mode reads brightness and the reported API ranges without changing display settings.

Run the application after building:

```powershell
dotnet run --project DisplayFX\DisplayFX.csproj -c Release --no-build
```

Before releasing, verify physical DDC/CI commands, GPU color updates, global shortcuts, screen overlays and docking/undocking on supported hardware.

## Build the installer

Install Inno Setup 6 and run:

```powershell
.\build_installer.ps1
```

The script publishes fresh self-contained x64 binaries to a temporary staging directory, builds `artifacts\DisplayFX_Setup.exe`, and removes staging even on failure. It never packages local settings or stale binaries. Optional parameters:

```powershell
.\build_installer.ps1 -DotNetPath 'C:\path\to\dotnet.exe' -InnoCompiler 'C:\path\to\ISCC.exe' -OutputDirectory 'D:\output'
```

The installer offers an optional desktop shortcut. Start with Windows is controlled in the app rather than enabled by installation.

## Settings and recovery

Settings live at `%LOCALAPPDATA%\DisplayFX\Data.json`. Saves are atomic and keep the previous save as `Data.json.bak`. A corrupt save with a valid backup is preserved as `Data.json.corrupt-<id>` before recovery. If neither file is readable, startup reports the error and preserves the files.

The first launch imports a nonempty `<AppDirectory>\Data\Data.json` when no per-user save exists, leaving the legacy file intact. Newly detected monitors receive a default profile.

App autostart uses `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DisplayFX`. A session-local mutex prevents duplicate instances.

## Repository

- `DisplayFX/`: WPF app, MVVM views, entities, factories and controllers.
- `WindowsDisplayAPI-master/WindowsDisplayAPI/`: vendored Win32 display wrapper with local correctness fixes.
- `tests/DisplayFX.RegressionTests/`: hardware-independent regression harness.
- `DisplayFX.sln`, `global.json`: solution and SDK selection.
- `DisplayFX.iss`, `build_installer.ps1`: installer packaging.
- `artifacts/`: ignored installers and optional view previews.

Tracked binaries, old installers, unused store assets, empty configuration templates and Freebuff tracking files have been removed. Build output, editor state and local tool data are ignored. The display wrapper source and its original license remain part of the app.

The app uses Caliburn.Micro, MahApps.Metro, Microsoft.Extensions.DependencyInjection, Newtonsoft.Json, NHotkey.Wpf, NLog and NvAPIWrapper.

## Troubleshooting

- **Brightness unavailable:** enable DDC/CI in the monitor's menu, check the cable/dock, then reopen brightness controls. Some displays do not support physical brightness commands.
- **Digital Vibrance unavailable:** verify the monitor is connected to a supported NVIDIA GPU and the driver is installed. Gamma and DDC/CI controls can also work with other GPUs.
- **Shortcut unavailable:** choose another combination and save. Check both other DisplayFX profiles and other apps.
- **Import fails:** select a version-1 DisplayFX settings backup. Invalid values, duplicate JSON properties, oversized files and capacity conflicts are rejected before committing.

## License

WindowsDisplayAPI retains its [original license](WindowsDisplayAPI-master/LICENSE). All rights reserved for DisplayFX.
