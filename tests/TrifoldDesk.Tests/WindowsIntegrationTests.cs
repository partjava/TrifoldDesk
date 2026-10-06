using System.Reflection;
using System.Collections;
public static class WindowsIntegrationTests
{
    // Run from a Windows WPF host; this probe never requests radio access or changes state.
    public static async Task RunReadOnlyAsync(Assembly application, Action<bool,string> check)
    {
        var serviceType=application.GetType("TrifoldDesk.Services.WirelessControlService")!;
        using var service=(IDisposable)Activator.CreateInstance(serviceType)!;
        var read=(Task)serviceType.GetMethod("ReadAsync")!.Invoke(service,null)!;await read;
        var snapshot=read.GetType().GetProperty("Result")!.GetValue(read)!;
        check(serviceType.GetField("_access",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(service)==null,"read-only wireless enumeration never requests radio-control consent");
        var radios=(IEnumerable)snapshot.GetType().GetProperty("Radios")!.GetValue(snapshot)!;
        foreach(var radio in radios)
        {
            var type=radio.GetType();
            check(!string.IsNullOrWhiteSpace((string)type.GetProperty("Id")!.GetValue(radio)!),"actual radio has a device or session identifier");
            check(new[]{"On","Off","Disabled","Unknown"}.Contains((string)type.GetProperty("State")!.GetValue(radio)!),"radio reports an actual Windows state");
        }
        var devices=(IEnumerable)snapshot.GetType().GetProperty("BluetoothDevices")!.GetValue(snapshot)!;
        foreach(var device in devices)check((bool)device.GetType().GetProperty("Paired")!.GetValue(device)!,"Bluetooth enumeration contains actual paired endpoints only");
        var musicType=application.GetType("TrifoldDesk.MusicWidget")!;
        var sessions=(Task)musicType.GetMethod("ReadSessionsAsync")!.Invoke(null,null)!;await sessions;
        var sources=(IEnumerable<string>)sessions.GetType().GetProperty("Result")!.GetValue(sessions)!;
        check(sources.All(s=>!string.IsNullOrWhiteSpace(s)),"media session sources come from Windows session manager");
    }
}
