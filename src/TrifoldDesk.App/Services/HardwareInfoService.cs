using Microsoft.Win32;
using System.Runtime.InteropServices;
namespace TrifoldDesk.Services;

public sealed record HardwareInfo(string Cpu, string Gpu);
public static class HardwareInfoService
{
    // Query once, away from the UI thread; unavailable information is never invented.
    public static Task<HardwareInfo> ReadAsync() => Cached.Value;
    private static readonly Lazy<Task<HardwareInfo>> Cached = new(() => Task.Run(Read));
    private static HardwareInfo Read()
    {
        string cpu = "型号不可用", gpu = "型号不可用";
        try { using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"); cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? cpu; } catch { }
        object? locator = null, service = null, rows = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            service = ((dynamic)locator!).ConnectServer(".", @"root\cimv2");
            rows = ((dynamic)service).ExecQuery("SELECT Name FROM Win32_VideoController", "WQL", 48);
            var names = new List<string>();
            foreach (object row in (System.Collections.IEnumerable)rows)
            {
                try { string? name = Convert.ToString(((dynamic)row).Name); if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim()); }
                finally { Release(row); }
            }
            if (names.Count > 0) gpu = string.Join(" / ", names.Distinct());
        }
        catch { }
        finally { Release(rows); Release(service); Release(locator); }
        if(gpu=="型号不可用")
        {
            try
            {
                using var adapters=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                var names=new List<string>();
                foreach(var id in adapters?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    if(id.Length!=4||!id.All(char.IsDigit))continue;
                    using var adapter=adapters!.OpenSubKey(id);
                    if(adapter?.GetValue("DriverDesc") is string name && !string.IsNullOrWhiteSpace(name))names.Add(name.Trim());
                }
                if(names.Count>0)gpu=string.Join(" / ",names.Distinct().OrderByDescending(n=>n.Contains("NVIDIA",StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }
        return new(cpu, gpu);
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
