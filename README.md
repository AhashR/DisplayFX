# DisplayFX

DisplayFX is a Windows WPF application designed for display profile management, hardware color calibration, and process-based profile switching.

It provides per-monitor display profile management, integration with NVIDIA GPU hardware color controls (Digital Vibrance, Saturation, Gamma, Contrast, Brightness, RGB), global hotkey keybindings, and automatic profile switching based on foreground application execution.

---

## Table of Contents

- [Features](#features)
- [Architecture and Tech Stack](#architecture-and-tech-stack)
- [Repository Structure](#repository-structure)
- [System Requirements](#system-requirements)
- [Installation and Setup](#installation-and-setup)
- [Building from Source](#building-from-source)
- [Building the Windows Installer](#building-the-windows-installer)
- [Configuration and Administration](#configuration-and-administration)
  - [Data Storage](#data-storage)
  - [Windows Autostart](#windows-autostart)
  - [Foreground Process Monitoring](#foreground-process-monitoring)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Features

- **Per-Monitor Profile Management**: Supports up to 5 customizable display profiles per connected monitor.
- **NVIDIA Hardware Color Control**: Direct integration via `NvAPIWrapper` for hardware-level Digital Vibrance, Saturation, Gamma, Contrast, Brightness, and RGB channel tuning.
- **Automated Profile Switching**: Link display profiles to target application executables (e.g. `cs2.exe`, `photoshop.exe`). Profiles automatically activate when the designated process enters the foreground and revert when focus changes.
- **Global Keybindings**: Register global system hotkeys (`NHotkey.Wpf`) to switch display profiles instantly from any application.
- **Startup and Persistence**: Configurable Windows registry startup (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) and automatic profile restoration on boot.
- **Process Isolation and Memory Optimization**: Single-instance mutex protection (`Global\DisplayFX_SingleInstance_Mutex`) and background memory trimming.

---

## Architecture and Tech Stack

| Component | Technology / Library | Description |
| :--- | :--- | :--- |
| **Framework** | .NET 7.0 (WPF), C# 11 | Core application framework |
| **UI Pattern** | Caliburn.Micro | MVVM architecture and EventAggregator messaging |
| **Dependency Injection** | Microsoft.Extensions.DependencyInjection | Inversion of Control container |
| **UI Components** | MahApps.Metro | Modern WPF window styling and controls |
| **Display Management** | WindowsDisplayAPI | Native Win32 CCD (Connecting and Configuring Displays) API wrapper |
| **GPU API** | NvAPIWrapper | NVIDIA GPU API wrapper for color calibration and Digital Vibrance |
| **Global Hotkeys** | NHotkey.Wpf | Windows global keybinding management |
| **Logging** | NLog | Diagnostics and error logging |

---

## Repository Structure

```
DisplayFX/
├── DisplayFX.sln                         # Main Visual Studio Solution
├── DisplayFX.iss                         # Inno Setup Script for generating Windows Installer
├── build_installer.ps1                   # Automated installer build script
├── DisplayFX/                            # Primary WPF Application Project
│   ├── Bootstrap/                        # Application entry point, DI, single-instance mutex
│   ├── Global/                           # System controllers (Data, Display, Process, Registry)
│   ├── Interface/                        # Views and ViewModels (Shell, Monitors, Profiles, Settings)
│   ├── Objects/                          # Domain entities, factories, and event handlers
│   ├── Resources/                        # Application icons and WPF resource dictionaries
│   └── Data/                             # Default data storage directory (Data.json)
└── WindowsDisplayAPI-master/             # Sub-project library
    └── WindowsDisplayAPI/                # Win32 display configuration wrapper library
```

---

## System Requirements

- **Operating System**: Windows 10 or Windows 11 (64-bit)
- **Architecture**: x64
- **Graphics Hardware**: NVIDIA Graphics Processing Unit (Required for Digital Vibrance and hardware color controls)
- **Runtime**: .NET 7.0 Desktop Runtime

---

## Installation and Setup

To install DisplayFX using the Windows Setup Wizard:

1. Download or locate `DisplayFX_Setup.exe` in `installer_output\`.
2. Run `DisplayFX_Setup.exe`.
3. Choose your desired installation folder (e.g. `C:\Program Files\DisplayFX` or a custom directory).
4. Select optional tasks (Desktop shortcut, Windows autostart).
5. Click **Install**.

The setup wizard will install the application, create Start Menu and Desktop shortcuts, register an uninstaller in Windows Control Panel, and launch the application.

---

## Building from Source

### Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) (with .NET desktop development workload) or [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0).

### Build Steps

1. Clone the repository:
   ```cmd
   git clone https://github.com/AhashR/DisplayFX.git
   cd DisplayFX
   ```

2. Build the solution using the .NET CLI:
   ```cmd
   dotnet build DisplayFX.sln -c Release
   ```

3. Run the application:
   ```cmd
   dotnet run --project DisplayFX\DisplayFX.csproj
   ```

---

## Building the Windows Installer

DisplayFX includes an Inno Setup script (`DisplayFX.iss`) and build script (`build_installer.ps1`) to generate a standalone Windows installer setup program.

### Build Installer Command

Run the PowerShell build script:

```powershell
.\build_installer.ps1
```

The script publishes the application binaries and invokes the Inno Setup compiler (`ISCC.exe`), producing the installer package:
- Output Location: `installer_output\DisplayFX_Setup.exe`

---

## Configuration and Administration

### Data Storage

User profiles, monitor configurations, hotkey bindings, and application links are stored in JSON format at:
```
<AppDirectory>\Data\Data.json
```

### Windows Autostart

Enabling **Start with Windows** creates a startup value in the Windows Registry:
- **Registry Key**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Value Name**: `DisplayFX`
- **Value Data**: `"C:\Path\To\DisplayFX.exe"`

### Foreground Process Monitoring

DisplayFX inspects the active foreground window handle (`GetForegroundWindow`) at 1-second intervals. If the executable path matches a configured profile's `LinkedExecutablePath`, DisplayFX automatically applies the linked profile settings.

---

## Troubleshooting

#### Hardware Color Controls Not Applying
- Verify that the target display is connected directly to an NVIDIA GPU.
- Ensure official NVIDIA display drivers are installed and functioning.

#### Automatic Profile Switching Fails for Elevated Processes
- If a target application runs with elevated (Administrator) privileges, DisplayFX must also be executed as Administrator to inspect process metadata.

#### Profile Data Reset
- To reset application configuration to default settings, terminate DisplayFX and delete `<AppDirectory>\Data\Data.json`. DisplayFX will regenerate a clean configuration file upon next launch.

---

## License

This project includes `WindowsDisplayAPI` under its original license. All rights reserved.
