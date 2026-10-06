using Windows.Devices.Radios;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Networking.Connectivity;

namespace TrifoldDesk.Services;
public sealed record WirelessRadioReading(string Id, string Name, string Kind, string State, bool CanToggle);
public sealed record BluetoothDeviceReading(string Id, string Name, bool Paired, bool? Connected);
public sealed record WirelessSnapshot(IReadOnlyList<WirelessRadioReading> Radios, IReadOnlyList<string> WifiConnections,
    IReadOnlyList<BluetoothDeviceReading> BluetoothDevices, IReadOnlyList<string> Warnings);
public sealed record WirelessChangeResult(bool Succeeded, string Message, string ActualState);
/// <summary>Radio enumeration is read only; consent and state changes require an explicit UI action.</summary>
public sealed class WirelessControlService : IDisposable
{
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<string, Radio> _radios = [];
    private readonly object _gate = new();
    private RadioAccessStatus? _access;
    private bool _disposed;
    public event Action? Changed;
    public void ResetAccessStatus() { _access = null; }
    public async Task<WirelessSnapshot> ReadAsync()
    {
        await _readGate.WaitAsync();
        try
        {
            lock (_gate) if (_disposed) return new([], [], [], ["无线控制已停止"]);
            var warnings = new List<string>();
            var readings = new List<WirelessRadioReading>();
            var radios = new Dictionary<string, Radio>();
            try
            {
                // Device-interface identifiers keep a button bound to the actual radio.
                var devices = await DeviceInformation.FindAllAsync(Radio.GetDeviceSelector());
                foreach (var device in devices)
                {
                    try
                    {
                        var radio = await Radio.FromIdAsync(device.Id); if (radio == null) continue;
                        radios[device.Id] = radio;
                        readings.Add(new(device.Id, radio.Name, radio.Kind.ToString(), radio.State.ToString(),
                            radio.Kind is RadioKind.WiFi or RadioKind.Bluetooth && radio.State is RadioState.On or RadioState.Off));
                    }
                    catch (Exception ex) { warnings.Add(device.Name + "：" + ex.Message); }
                }
                // Some desktops expose radios to GetRadiosAsync without a device selector entry.
                if (radios.Count == 0)
                {
                    foreach (var radio in await Radio.GetRadiosAsync())
                    {
                        string token = "session-radio:" + Guid.NewGuid().ToString("N"); radios[token] = radio;
                        readings.Add(new(token, radio.Name, radio.Kind.ToString(), radio.State.ToString(),
                            radio.Kind is RadioKind.WiFi or RadioKind.Bluetooth && radio.State is RadioState.On or RadioState.Off));
                    }
                }
                if (radios.Count == 0) warnings.Add("系统没有提供可控制的无线电设备");
            }
            catch (Exception ex) { warnings.Add("无线电枚举不可用：" + ex.Message); }
            var profiles = new List<string>();
            try
            {
                foreach (var profile in NetworkInformation.GetConnectionProfiles().Where(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None))
                {
                    string ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                    if (!string.IsNullOrWhiteSpace(ssid)) profiles.Add(ssid + " · " + profile.GetNetworkConnectivityLevel());
                }
            }
            catch (Exception ex) { warnings.Add("当前Wi-Fi连接不可读取：" + ex.Message); }
            var bluetooth = new List<BluetoothDeviceReading>();
            foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
                try
                {
                    var devices = await DeviceInformation.FindAllAsync(selector, ["System.Devices.Aep.IsConnected"], DeviceInformationKind.AssociationEndpoint);
                    foreach (var device in devices.Take(256))
                        if (!bluetooth.Any(d => d.Id == device.Id)) bluetooth.Add(new(device.Id, device.Name, device.Pairing.IsPaired,
                            device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected) && connected is bool state ? state : null));
                }
                catch (Exception ex) { warnings.Add("蓝牙已配对设备不可读取：" + ex.Message); }
            lock (_gate)
            {
                if (_disposed) return new([], [], [], ["无线控制已停止"]);
                foreach (var radio in _radios.Values) radio.StateChanged -= StateChanged;
                _radios.Clear();
                foreach (var radio in radios) { _radios.Add(radio.Key, radio.Value); radio.Value.StateChanged += StateChanged; }
            }
            return new(readings, profiles, bluetooth, warnings.Distinct().ToArray());
        }
        finally { _readGate.Release(); }
    }
    private void StateChanged(Radio sender, object args) => Changed?.Invoke();
    public async Task<WirelessChangeResult> SetRadioStateAsync(string radioId, bool on)
    {
        Radio? radio;
        lock (_gate)
        {
            if (_disposed || !_radios.TryGetValue(radioId, out radio)) return new(false, "设备列表已变化，请先刷新", "Unknown");
        }
        var target = on ? RadioState.On : RadioState.Off;
        if (radio.Kind is not (RadioKind.WiFi or RadioKind.Bluetooth)) return new(false, "此设备类型没有提供切换能力", radio.State.ToString());
        if (radio.State == RadioState.Disabled) return new(false, "设备被硬件开关或系统策略禁用，请在Windows设置检查", radio.State.ToString());
        if (radio.State == target) return new(true, "设备已处于请求状态", radio.State.ToString());
        try
        {
            // This method is called only by the window's explicit toggle button, on its UI context.
            _access ??= await Radio.RequestAccessAsync();
            if (_access != RadioAccessStatus.Allowed) return new(false, "系统拒绝无线电控制权限：" + _access, radio.State.ToString());
            var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Confirm(Radio sender, object args) { if (sender.State == target) confirmed.TrySetResult(true); }
            radio.StateChanged += Confirm;
            try
            {
                var access = await radio.SetStateAsync(target);
                if (access != RadioAccessStatus.Allowed) return new(false, "系统没有接受切换请求：" + access, radio.State.ToString());
                if (radio.State != target) await Task.WhenAny(confirmed.Task, Task.Delay(TimeSpan.FromSeconds(3)));
                bool success = radio.State == target;
                return new(success, success ? "设备状态已确认" : "系统接受请求，但设备状态未确认；可能受硬件或策略限制", radio.State.ToString());
            }
            finally { radio.StateChanged -= Confirm; }
        }
        catch (Exception ex) { return new(false, "无线电切换失败：" + ex.Message, radio.State.ToString()); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var radio in _radios.Values) radio.StateChanged -= StateChanged;
            _radios.Clear(); Changed = null;
        }
    }
}
