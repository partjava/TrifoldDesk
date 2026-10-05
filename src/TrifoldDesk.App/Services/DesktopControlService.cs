using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace TrifoldDesk.Services;
public sealed record AudioState(double Volume, bool Muted);
public sealed record BrightnessState(byte Value, string InstanceName);
public static class DesktopControlService
{
    public static void OpenSettings(string uri)
    {
        if (!uri.StartsWith("ms-settings:", StringComparison.Ordinal)) throw new ArgumentException("无效系统设置入口。");
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
    public static AudioState ReadAudio() => WithAudio(volume =>
    { Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float value)); Marshal.ThrowExceptionForHR(volume.GetMute(out bool mute)); return new AudioState(value * 100, mute); });
    public static void SetVolume(double value) => WithAudio(volume =>
    { Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)Math.Clamp(value / 100, 0, 1), Guid.Empty)); return true; });
    public static void SetMute(bool muted) => WithAudio(volume =>
    { Marshal.ThrowExceptionForHR(volume.SetMute(muted, Guid.Empty)); return true; });
    private static T WithAudio<T>(Func<IAudioEndpointVolume, T> operation)
    {
        object? enumerator = null, endpoint = null; IMMDevice? device = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
            return operation((IAudioEndpointVolume)endpoint);
        }
        finally { Release(endpoint); Release(device); Release(enumerator); }
    }
    public static string BatteryText()
    {
        var power = Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)) return "交流供电 · 无电池";
        float level = power.BatteryLifePercent;
        string percentage = level is >= 0 and <= 1 ? $"{level * 100:0}%" : "未知";
        return "电池 " + percentage + (power.PowerLineStatus == Forms.PowerLineStatus.Online ? " · 正在供电" : " · 使用电池");
    }
    public static BrightnessState? ReadBrightness()
    {
        object? locator = null, services = null, collection = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI 不可用。"));
            dynamic wmi = locator!; services = wmi.ConnectServer(".", @"root\wmi"); dynamic connection = services;
            collection = connection.ExecQuery("SELECT * FROM WmiMonitorBrightness WHERE Active=True");
            foreach (dynamic monitor in (System.Collections.IEnumerable)collection!)
            {
                try { return new BrightnessState(Convert.ToByte(monitor.CurrentBrightness), Convert.ToString(monitor.InstanceName) ?? ""); }
                finally { Release(monitor); }
            }
            return null;
        }
        finally { Release(collection); Release(services); Release(locator); }
    }
    public static void SetBrightness(BrightnessState target, double value)
    {
        object? locator = null, services = null, collection = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI 不可用。"));
            dynamic wmi = locator!; services = wmi.ConnectServer(".", @"root\wmi"); dynamic connection = services;
            collection = connection.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            foreach (dynamic monitor in (System.Collections.IEnumerable)collection!)
            {
                try
                {
                    if ((string)monitor.InstanceName != target.InstanceName) continue;
                    uint result = Convert.ToUInt32(monitor.WmiSetBrightness(1u, (byte)Math.Clamp(value, 0, 100)));
                    if (result != 0) throw new InvalidOperationException("显示器拒绝了亮度调整。WMI 返回：" + result);
                    return;
                }
                finally { Release(monitor); }
            }
            throw new InvalidOperationException("此显示器不支持亮度调整，或设备已断开。");
        }
        finally { Release(collection); Release(services); Release(locator); }
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float value, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float value, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float value);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, [MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
        [PreserveSig] int VolumeStepUp([MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int VolumeStepDown([MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float step);
    }
}
