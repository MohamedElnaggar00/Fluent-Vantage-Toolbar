# Implementation evidence

- https://github.com/sahidhh/lenovo-battery-tray : MIT, explicitly excludes Legion / LOQ. Concept reference only.
- https://github.com/BartoszCichecki/LenovoLegionToolkit : GPL-3.0; protocol/behavior evidence, no implementation code copied. 82JH mapped to 15ITH6H; touchpad support/query/set WMI names; microphone uses Core Audio; charge mode uses EnergyDrv.
- https://github.com/dantmnf/OpenLenovoSettings : MIT; independent EnergyDrv charging protocol evidence, conservation bit 0x20 and express bit 0x04, commands 3/5 and 7/8. Conservation support bits vary by generation, so successful state reads do not prove write support. Writes are read back and failure is surfaced.
- https://psref.lenovo.com/syspool/Sys/PDF/Legion/Lenovo_Legion_5_15ITH6H/Lenovo_Legion_5_15ITH6H_Spec.html : physical e-camera shutter switch.
- https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops : native WinUI MicaBackdrop.
- https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font : Windows 11 glyph font.
- https://github.com/MohamedElnaggar00/fluent-prayer-times : MIT, same owner's tray shell infrastructure reused with license retained. No prayer/location data included.

- LenovoLegionToolkit (GPL-3.0), protocol evidence only, no code copied, for: Fn Lock (EnergyDrv IOCTL 0x831020E8, query 2, bit 10, set 0xE/0xF), Lenovo battery record (IOCTL 0x83102138, manufacture date field), warranty lookup (pcsupport.lenovo.com getIbaseInfo endpoint, serial number and machine type from Win32_ComputerSystemProduct).
- Battery capacity and cycle count come from Windows WMI (root\WMI BatteryStaticData, BatteryFullChargedCapacity, BatteryCycleCount).

No claim of target-device runtime verification is made.

