# DisplayFX

[Download the latest Windows installer](https://github.com/AhashR/DisplayFX/releases/latest).

DisplayFX manages per-monitor color profiles and physical backlight brightness on Windows. Gamma-ramp brightness, contrast and gamma work with compatible display drivers; NVIDIA Digital Vibrance requires an NVIDIA GPU. External physical brightness requires DDC/CI; compatible internal laptop panels use a separate WMI route.

This checkout is **2.2.14**. Use Settings to import or export a profile, check for updates, or view the version. Right-click a profile for its icon and other profile actions. The [implementation matrix](docs/feature-implementation-status.md) maps the original proposals to this build. Local builds may be unsigned. Extra AMD/Intel saturation backends are not implemented, and hardware-dependent write paths still need physical qualification.

The editor uses sliders for brightness, contrast, vibrance and gamma. Revert discards draft changes, Apply changes the visible colors, and Save stores the edited values. Detailed status stays in the status icon tooltip. Click a value beside a slider to type it exactly. Press Enter to accept or Esc to cancel. The interface is in English.

## Using the app

- Select a monitor tab, then a profile. Create up to 100 profiles per monitor with **+** or import a single profile from the main window’s menu. Imported colors receive a unique name without reserving shortcuts or application links. Right-click a profile to mark it as a favorite, reorder, duplicate, rename, export or remove it. Favorites are profile metadata and do not change the order.
- Adjust brightness, contrast, vibrance and gamma. The **play** icon applies the current colors, **undo** reverts edits, and **save** stores the draft. Profile shortcuts and executable links are optional; all edits remain isolated until saved. Use **Link application** to choose an executable. Its profile applies while that app is in the foreground, then the previously active profile returns when you leave it.
- Color brightness adjusts the GPU gamma ramp. Profiles never send physical brightness commands. Profile application runs in the background; failed automatic updates show nonmodal feedback and retry after 30 seconds. A display-mode change or reconnect invalidates the previous application and reapplies the intended colors.
- The toolbar provides **identify displays**, **refresh**, **settings** and **Workspace**. Hover an icon for its label. The main window’s menu imports or exports one profile at a time to a path you choose.
- Right-click a monitor tab to rename it. Disconnected displays retain profiles, and drafts survive reconnection during the same session. When a valid, unique EDID serial is available, profiles also follow the same panel across changed ports/docks. Ambiguous identities are never guessed.
- Left-click the tray icon for brightness sliders. The **link** icon links their levels, **open app** opens the main DisplayFX screen, **power** turns displays off, and **gear** opens settings. Normal rows show only the monitor name, percentage and slider; errors appear as a small warning with details in its tooltip.
- Brightness writes run outside the UI thread and coalesce rapid input to the latest requested value. Requests to one monitor are serialized and use its working API and cached range. Transient failed writes get one automatic retry. Closing the popup flushes pending input, and only successful writes are remembered. On startup, DisplayFX restores each monitor’s last saved brightness and retries briefly if the monitor is still waking. Moving a slider gives that monitor’s new setting priority. A sleeping or temporarily unreadable monitor keeps its last known level. Controls reconnect automatically after wake; a completely unknown level displays a dash instead of an invented 50%.
- Settings contains startup and tray preferences and optional global brightness shortcuts.
- Shortcut and application-link conflicts appear beside the controls. Shortcuts are temporarily released while recording. Old executable links remain readable; transient app-switch state restores the manual profile on startup. Settings restore warns before discarding unsaved profile drafts and reconciles the resulting active colors.

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

This separate mode reads brightness and the reported API ranges without changing display settings. `--diagnose-hardware` also reads advanced-color state, NVIDIA vibrance and advertised VCP controls. Neither mode sends monitor write commands.

Run the application after building:

```powershell
dotnet run --project DisplayFX\DisplayFX.csproj -c Release --no-build
```

Before releasing, verify physical DDC/CI commands, GPU color updates, global shortcuts, screen overlays and docking/undocking on supported hardware.

The [15 November 2026 app audit](docs/app-audit-2026-11-15.md) records the additional recovery, in-flight profile deletion, import feedback and diagnostics checks. The [feature implementation matrix](docs/feature-implementation-status.md) maps the [original 80 proposals](docs/feature-backlog-nl.md) to this build. See [hardware qualification](docs/hardware-test-matrix.md) before claiming support for an untested configuration.

Windows CI runs the regression suite on pushes and pull requests. A main-branch version change triggers the release workflow, which tests, builds the installer with a signature-verified compiler, verifies uploaded checksums, and publishes the release. Published versions are never overwritten. See [the 2.1.2 review resolution](docs/audit-resolution-2.1.2.md).

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

The installer includes `DisplayFX.Cli.exe`. While DisplayFX is running in the same Windows session:

```powershell
.\DisplayFX.Cli.exe list
.\DisplayFX.Cli.exe apply <profile-guid-from-list>
```

The bounded local pipe accepts only listing and profile-ID activation, and is restricted to the current Windows user/session. Profile activation is unavailable while a settings dialog is open.

Signing preparation uses an existing certificate in the Windows certificate store, with a code-signing EKU and accessible private key. Pass `-SigningCertificateThumbprint <thumbprint> -RequireSignature` to the installer script. No private key or password belongs in source control. Local builds can be unsigned. Publication requires valid Authenticode and actual hardware qualification evidence. Building locally does not publish a release.

## Local settings

DisplayFX stores its working data in Settings/Data.json alongside the application when writable, or in your Documents folder. Saves are atomic. If that file is missing on upgrade, the app reads its previous AppData settings and migrates them without changing the original files. It does not create automatic backups or diagnostic files. Export each profile manually from the main window if you want a separate copy.

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
- **Import fails:** select a version-1 or version-2 DisplayFX settings backup. Invalid values, duplicate JSON properties, oversized files and capacity conflicts are rejected before committing.

## License

WindowsDisplayAPI retains its [original license](WindowsDisplayAPI-master/LICENSE). All rights reserved for DisplayFX.
