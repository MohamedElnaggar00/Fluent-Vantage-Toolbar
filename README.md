# Fluent Legion Toolbar

[العربية](README.ar.md)

A personal, original WinUI 3 + Mica toolbar for the Lenovo Legion 5 15ITH6H (82JH). It opens from a tray icon as a Windows 11 style flyout with quick hardware controls.

## Status
Private prototype (version 0.2.0). The Windows build and the WinUI fixture captures (English and Arabic, light and dark) pass in CI and were visually inspected. Hardware controls and desktop Mica still need testing on the actual laptop. No public release.

## What's new in 0.2.0
- Lenovo Legion logo as app icon and tray icon.
- Right-click tray menu: Open, Toolbar settings, About.
- Settings page (gear icon at the top right of the flyout): language, theme, and which buttons and links show on the main screen.
- Battery details page: current charge, health with bar, design capacity, full charge capacity, cycle count, manufacture date.
- Warranty card: status, start date, end date, days remaining, Lenovo Support link. Read from Lenovo support with the machine serial number and cached locally.
- Battery colour: green, yellow at 20% or below, red at 5% or below.
- Fn Lock fixed using the same EnergyDrv control path as LenovoLegionToolkit, with a new tile icon.
- The flyout does not appear in the taskbar.
- Background start with administrator rights through Task Scheduler, no UAC prompt (see below).
- English is the default language. The theme follows the Windows system theme by default. Languages: English, French, German, Italian, Spanish, Portuguese, Russian, Chinese (Simplified), Japanese, Arabic (right-to-left). Texts without a translation fall back to English.

## Run at sign-in with administrator rights (SkipUAC)
1. Unzip the download fully into a permanent folder. Do not move it afterwards.
2. Right-click `install-startup.ps1`, then "Run with PowerShell". It asks for administrator approval once.
3. It creates two tasks: `FluentLegionToolbar` (at sign-in, background mode, highest privileges) and `FluentLegionToolbar-Open` (shows the flyout).
4. To remove both, run `uninstall-startup.ps1`.

This is a resident tray process, not a Windows service. A WinUI window cannot run in the services session.

## Controls
Fn Lock, mute all active microphones, battery conservation, rapid charge, touchpad lock, internal-panel 60/144 Hz and Always-on USB. Each control is checked for support and read back after a change. Unsupported controls are disabled and say so. The app does not stop Lenovo services, and it sends no charging or touchpad command to a different model.

## Install and build
Requires Windows 11 x64 and the .NET 8 Desktop Runtime. Open the latest successful `Verify private WinUI build` run in Actions and download `private-test-build-and-captures`. Unzip it, then unzip `Fluent-Legion-toolbar-runtime-dependent.zip` and run `FluentLegionToolbar.exe` from the complete folder.

To build from source use Visual Studio MSBuild as in `.github/workflows/verify.yml`, including its compiled XAML and resource copy step.

`--capture <file.png> --light|--dark --arabic` renders a real WinUI preview with fixture data and does not touch the hardware. The captures are not proof of hardware behaviour.

## Contributors
- Mohamed Elnaggar: developer.
- app.instinct: development contributor.

Hardware protocols were studied from LenovoLegionToolkit (GPL-3.0) as evidence only. This project is an independent implementation. See `SOURCES.md`.
