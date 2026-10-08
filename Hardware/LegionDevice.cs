using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
namespace FluentLegionToolbar.Hardware;
// Protocol facts are documented in SOURCES.md. No Lenovo services are stopped or replaced.
public enum ChargeMode { Normal, Conservation, Rapid }
public sealed record DeviceState(int? Percent, bool Plugged, bool Charging, ChargeMode? Mode, bool? Muted, bool? TouchpadLocked, string Model, string[] Problems, bool? FnLocked = null, int? UsbMode = null, int? RefreshHz = null, int[]? AvailableHz = null);
public sealed class LegionDevice
{
    [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AC, Flags, Percent, Reserved; public uint Life, FullLife; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref uint input, uint inputLength, out uint output, uint outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint code, ref uint input, uint inputLength, byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
    readonly SemaphoreSlim gate = new(1);
    /// <summary>Reads one Lenovo battery record (EnergyDrv IOCTL 0x83102138). Offsets: temperature 14, manufacture date 16, first use 18.</summary>
    public static (ushort Temperature, ushort Manufacture, ushort FirstUse)? ReadLenovoBatteryRecord(uint index)
    {
        using var h = CreateFile(@"\\.\EnergyDrv", 3, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new byte[84];
        if (!DeviceIoControl(h, 0x83102138, ref index, 4, buffer, (uint)buffer.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        ushort Read(int offset) => BitConverter.ToUInt16(buffer, offset);
        return (Read(14), Read(16), Read(18));
    }
    bool targetModel;
    uint Exchange(uint command, uint ioctl = 0x831020F8)
    {
        if (!targetModel) throw new InvalidOperationException("This device is not a recognised Lenovo Legion model, so hardware writes are disabled.");
        using var h = CreateFile(@"\\.\EnergyDrv", 3, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!DeviceIoControl(h, ioctl, ref command, 4, out uint response, 4, out uint returned, IntPtr.Zero) || returned < 4) throw new Win32Exception(Marshal.GetLastWin32Error());
        return response;
    }
    ChargeMode ReadMode()
    {
        uint raw = Exchange(255);
        if ((raw & 0x20) != 0) return ChargeMode.Conservation;
        if ((raw & 0x04) != 0) return ChargeMode.Rapid;
        // Toolkit interprets bit 17 after a byte swap as charge mode enabled.
        if ((raw & 0x200) == 0) throw new InvalidOperationException("Unknown EnergyDrv charging mode; writes are disabled.");
        return ChargeMode.Normal;
    }
    int Touchpad(string method, int? input = null)
    {
        if (!targetModel) throw new InvalidOperationException("This device is not a recognised Lenovo Legion model.");
        using var search = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM LENOVO_GAMEZONE_DATA");
        using var rows = search.Get();
        var instance = rows.Cast<ManagementObject>().FirstOrDefault() ?? throw new InvalidOperationException("Lenovo WMI provider is unavailable.");
        using (instance)
        {
            using var parameters = instance.GetMethodParameters(method);
            if (input.HasValue) parameters["Data"] = input.Value;
            using var result = instance.InvokeMethod(method, parameters, null);
            return input.HasValue ? input.Value : Convert.ToInt32(result["Data"]);
        }
    }
    bool ReadMicrophone()
    {
        using var audio = new MMDeviceEnumerator();
        var endpoints = audio.EnumerateAudioEndPoints(DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
        if (endpoints.Count == 0) throw new InvalidOperationException("No active microphone endpoint.");
        bool muted = true;
        foreach (var device in endpoints) { using (device) muted &= device.AudioEndpointVolume.Mute; }
        return muted;
    }
    public async Task<DeviceState> ReadAsync() => await Task.Run(() =>
    {
        var errors = new List<string>(); string model = "Unknown device";
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"); using var rows = q.Get();
            foreach (ManagementObject row in rows) { using(row) { model = Convert.ToString(row["Model"]) ?? model; targetModel = DeviceInfo.IsSupported; } }
        } catch (Exception e) { errors.Add(e.Message); targetModel = false; }
        int? percent=null; bool plugged=false, charging=false;
        if (GetSystemPowerStatus(out var power)) { percent = power.Percent <= 100 ? power.Percent : null; plugged = power.AC == 1; charging = power.Flags != 255 && (power.Flags & 8) != 0; }
        ChargeMode? mode=null; bool? muted=null, locked=null;
        try { mode=ReadMode(); } catch(Exception e) { errors.Add("Battery mode: "+e.Message); }
        try { if (Touchpad("IsSupportDisableTP") > 0) { int value=Touchpad("GetTPStatus"); if (value is not (0 or 1)) throw new InvalidOperationException("Unknown touchpad state"); locked=value==1; } } catch(Exception e) { errors.Add("Touchpad: "+e.Message); }
        try { muted=ReadMicrophone(); } catch(Exception e) { errors.Add("Microphone: "+e.Message); }
        bool? fn=null;int? usb=null,hz=null;int[] rates=[];
        try { uint settings=Exchange(2,0x831020E8); fn=(settings&1024)!=0; } catch(Exception e) { errors.Add("Fn Lock: "+e.Message); }
        try { uint settings=Exchange(2,0x831020E8); usb=(settings&64)!=0&&(settings&16384)!=0?((settings&128)==0?0:(settings&32768)!=0?2:1):null; } catch(Exception e) { errors.Add("USB: "+e.Message); }
        try {(hz,rates)=DisplayControl.Read();}catch(Exception e){errors.Add("Display: "+e.Message);}
        return new DeviceState(percent, plugged, charging, mode, muted, locked, model, errors.ToArray(),fn,usb,hz,rates);
    });
    // Same control path as the reference toolkit: EnergyDrv settings IOCTL, query 2, bit 10 = Fn Lock, set 0xE = on, 0xF = off.
    // No support bit is required first; the query itself succeeding is the capability check. The state is read back with retries.
    public Task SetFnAsync(bool locked) => Task.Run(async () => {
        Exchange(2,0x831020E8);
        Exchange(locked?14u:15u,0x831020E8);
        for(int i=0;i<10;i++) {
            await Task.Delay(100);
            if(((Exchange(2,0x831020E8)&1024)!=0)==locked) return;
        }
        throw new InvalidOperationException("Fn Lock state was not confirmed.");
    });
    public Task SetUsbAsync(bool enabled) => Task.Run(() => {
        if((Exchange(2,0x831020E8)&0x4040)!=0x4040)throw new NotSupportedException("Always-on USB battery mode not supported.");
        Exchange(enabled?10u:11u,0x831020E8);Exchange(enabled?19u:18u,0x831020E8);
        uint raw=Exchange(2,0x831020E8);int mode=(raw&128)==0?0:(raw&32768)!=0?2:1;
        if(mode!=(enabled?2:0))throw new InvalidOperationException("Always-on USB setting was not confirmed.");
    });
    public Task SetRefreshAsync(int hz) => Task.Run(()=>DisplayControl.Set(hz));
    public async Task SetModeAsync(ChargeMode next)
    {
        await gate.WaitAsync();
        try { await Task.Run(async () => {
            var before=ReadMode();
            if (before == next) return;
            if (before==ChargeMode.Conservation) Exchange(5);
            else if (before==ChargeMode.Rapid) Exchange(8);
            if (next==ChargeMode.Conservation) Exchange(3);
            else if (next==ChargeMode.Rapid) Exchange(7);
            for(int i=0;i<10;i++) { await Task.Delay(100); if(ReadMode()==next) return; }
            throw new InvalidOperationException("Firmware did not confirm the requested mode. Lenovo Vantage may be overriding it.");
        }); } finally { gate.Release(); }
    }
    public Task SetTouchpadAsync(bool locked) => Task.Run(() => {
        if(Touchpad("IsSupportDisableTP")<=0) throw new InvalidOperationException("Touchpad locking is not supported.");
        Touchpad("SetTPStatus", locked?1:0);
        if ((Touchpad("GetTPStatus")==1)!=locked) throw new InvalidOperationException("Touchpad state was not confirmed.");
    });
    public Task SetMicrophoneAsync(bool muted) => Task.Run(() => {
        using var audio=new MMDeviceEnumerator(); var endpoints=audio.EnumerateAudioEndPoints(DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active);
        if(endpoints.Count==0) throw new InvalidOperationException("No active microphone.");
        foreach(var device in endpoints) { using(device) { device.AudioEndpointVolume.Mute=muted; if(device.AudioEndpointVolume.Mute!=muted) throw new InvalidOperationException("Microphone mute was not confirmed."); } }
    });
}

