using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TrifoldDesk.Services;
public static class ShellLinkHelper
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IconCacheLock = new();
    internal static int TestCacheCount { get { lock (IconCacheLock) return IconCache.Count; } }
    internal static void TestClearIconCache() { lock (IconCacheLock) IconCache.Clear(); }
    private static double CurrentDpi()
    {
        var app = Application.Current;
        return app != null && app.Dispatcher.CheckAccess() && app.MainWindow is Window window
            ? VisualTreeHelper.GetDpi(window).DpiScaleX : 1;
    }
    private static int PixelSize(double dpiScale, int logicalSize) => (int)Math.Clamp(Math.Ceiling(Math.Clamp(logicalSize, 16, 256) * (double.IsFinite(dpiScale) && dpiScale > 0 ? Math.Clamp(dpiScale, .5, 8) : 1)), 16, 512);
    private static long Modified(string? path)
    {
        try { return File.GetLastWriteTimeUtc(Environment.ExpandEnvironmentVariables(path ?? "")).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }
    private static void Cache(string key, ImageSource? image)
    {
        if (!IconCache.ContainsKey(key) && IconCache.Count >= 512) IconCache.Remove(IconCache.Keys.First());
        IconCache[key] = image;
    }
    public static ShortcutItem CreateShortcut(string source, int screen, int order)
    {
        source = Path.GetFullPath(source);
        var item = new ShortcutItem { Name = Directory.Exists(source) ? new DirectoryInfo(source).Name : Path.GetFileNameWithoutExtension(source), SourcePath = source, TargetPath = source, ScreenIndex = screen, Order = order };
        if (Path.GetExtension(source).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            object? com = null;
            try
            {
                com = new ShellLink(); ((IPersistFile)com).Load(source, 0);
                var link = (IShellLinkW)com;
                var text = new StringBuilder(32768);
                link.GetPath(text, text.Capacity, IntPtr.Zero, 4); item.TargetPath = text.ToString();
                text.Clear(); link.GetArguments(text, text.Capacity); item.Arguments = text.ToString();
                text.Clear(); link.GetWorkingDirectory(text, text.Capacity); item.WorkingDirectory = text.ToString();
                text.Clear(); link.GetIconLocation(text, text.Capacity, out int index); item.IconPath = text.ToString(); item.IconIndex = index;
            }
            catch (COMException ex) { App.Log(ex); /* Keep the Shell source for non-file/advertised shortcuts. */ }
            finally { if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); }
        }
        return item;
    }
    public static bool IsAvailable(ShortcutItem item)
    {
        if (!SourceExists(item)) return false;
        if (!Path.GetExtension(item.SourcePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return true;
        string target = Environment.ExpandEnvironmentVariables(item.TargetPath ?? "");
        // Virtual Shell targets may have no filesystem path. Keep their original .lnk launch semantics.
        return !Path.IsPathFullyQualified(target) || File.Exists(target) || Directory.Exists(target);
    }
    public static bool SourceExists(ShortcutItem item) => File.Exists(item.SourcePath) || Directory.Exists(item.SourcePath);
    public static void Launch(ShortcutItem item)
    {
        if (!SourceExists(item)) throw new FileNotFoundException("入口已失效，请右键重新定位。", item.SourcePath);
        // Shell owns link arguments, working directory, elevation and special targets.
        var plan = ScriptLaunchRules.Create(item);
        var start = new ProcessStartInfo { FileName = plan.FileName, UseShellExecute = plan.UseShellExecute, WorkingDirectory = plan.WorkingDirectory };
        foreach (string argument in plan.ArgumentList) start.ArgumentList.Add(argument);
        Process.Start(start);
    }
    public static void ShowInFolder(ShortcutItem item)
    {
        if (!SourceExists(item)) throw new FileNotFoundException("原文件不存在，请重新定位。", item.SourcePath);
        if (Directory.Exists(item.SourcePath)) Process.Start(new ProcessStartInfo(item.SourcePath) { UseShellExecute = true });
        else Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + item.SourcePath + "\"", UseShellExecute = true });
    }
    public static ImageSource? GetIcon(ShortcutItem item) => GetIcon(item, CurrentDpi());
    public static ImageSource? GetIcon(ShortcutItem item, double dpiScale = 1, int logicalSize = 40)
    {
        int pixels = PixelSize(dpiScale, logicalSize);
        string key = item.SourcePath + "|" + item.TargetPath + "|" + item.IconPath + "|" + item.IconIndex + "|" + Modified(item.SourcePath) + "|" + Modified(item.TargetPath) + "|" + Modified(item.IconPath) + "|" + dpiScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + logicalSize + "|" + pixels;
        lock (IconCacheLock)
        {
            if (IconCache.TryGetValue(key, out var cached)) return cached;
            var image = LoadIcon(item, pixels); Cache(key, image); return image;
        }
    }
    private static ImageSource? LoadIcon(ShortcutItem item, int pixels)
    {
        var info = new SHFILEINFO();
        IntPtr result;
        if (Path.GetExtension(item.SourcePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            // Query the target rather than the .lnk: Shell always overlays link icons.
            object? com = null; IntPtr pidl = IntPtr.Zero;
            result = IntPtr.Zero;
            try
            {
                string iconPath = Environment.ExpandEnvironmentVariables(item.IconPath ?? "");
                if (File.Exists(iconPath))
                {
                    SHDefExtractIcon(iconPath, item.IconIndex, 0, out var large, out var small, (uint)pixels);
                    if (large == IntPtr.Zero)
                    {
                        if (small != IntPtr.Zero) DestroyIcon(small);
                        ExtractIconEx(iconPath, item.IconIndex, out large, out small, 1);
                    }
                    info.hIcon = large;
                    if (small != IntPtr.Zero) DestroyIcon(small);
                    if (large != IntPtr.Zero) result = large;
                }
                if (info.hIcon == IntPtr.Zero)
                {
                    com = new ShellLink(); ((IPersistFile)com).Load(item.SourcePath, 0);
                    ((IShellLinkW)com).GetIDList(out pidl);
                    if (pidl != IntPtr.Zero)
                    {
                        var highResolution = ShellImage(pidl, pixels);
                        if (highResolution != null) return highResolution;
                        result = SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x108);
                    }
                }
            }
            catch (COMException ex) { App.Log(ex); }
            finally
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
                if (com != null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
            }
        }
        else
        {
            var highResolution = ShellImage(Environment.ExpandEnvironmentVariables(item.TargetPath is { Length: > 0 } ? item.TargetPath : item.SourcePath), pixels);
            if (highResolution != null) return highResolution;
            result = SHGetFileInfo(item.SourcePath, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100);
        }
        ImageSource? image = null;
        if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try { var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); image = bitmap; }
            finally { DestroyIcon(info.hIcon); }
        }
        return image;
    }
    public static void InvalidateIcon(string path)
    { lock (IconCacheLock) foreach (var key in IconCache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase) || k.StartsWith("shell-app|" + path + "|", StringComparison.OrdinalIgnoreCase)).ToArray()) IconCache.Remove(key); }
    private static string ShellAppPath(string path)=>Path.IsPathFullyQualified(path)?path:"shell:AppsFolder\\"+path;
    public static ImageSource? GetShellAppIcon(string path) => GetShellAppIcon(path, CurrentDpi());
    public static ImageSource? GetShellAppIcon(string path, double dpiScale, int logicalSize = 40)
    {
        int pixels = PixelSize(dpiScale, logicalSize);
        string key = "shell-app|" + path + "|" + Modified(path) + "|" + pixels;
        lock (IconCacheLock)
        {
            if (IconCache.TryGetValue(key, out var cached)) return cached;
            IntPtr pidl = IntPtr.Zero; var info = new SHFILEINFO(); ImageSource? image = null;
            try
            {
                if (SHParseDisplayName(ShellAppPath(path), IntPtr.Zero, out pidl, 0, out _) != 0) return null;
                image = ShellImage(pidl, pixels);
                if (image == null)
                {
                    SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x108);
                    if (info.hIcon != IntPtr.Zero) { var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); image = bitmap; }
                }
                Cache(key, image); return image;
            }
            finally { if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }
    }
    private static ImageSource? ShellImage(string path, int pixels)
    {
        IntPtr pidl = IntPtr.Zero;
        try { return SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) == 0 ? ShellImage(pidl, pixels) : null; }
        finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
    }
    private static ImageSource? ShellImage(IntPtr pidl, int pixels)
    {
        IShellItemImageFactory? factory = null; IntPtr bitmap = IntPtr.Zero;
        try
        {
            var id = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromIDList(pidl, ref id, out factory) != 0 || factory == null) return null;
            // ICONONLY prevents thumbnails; BIGGERSIZEOK retains native high-resolution assets.
            if (factory.GetImage(new NativeSize { Width = pixels, Height = pixels }, 0x5, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); return source;
        }
        catch (COMException ex) { App.Log(ex); return null; }
        finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (factory != null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory); }
    }
    public static string SaveShellAppLink(InstalledApp app)
    {
        string id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(app.ShellPath)));
        string directory=Path.Combine(App.DataDirectory,"app-links",id);Directory.CreateDirectory(directory);
        // Keep existing links stable when only the picker display name changes.
        var existing=Directory.EnumerateFiles(directory,"*.lnk").FirstOrDefault();if(existing!=null)return existing;
        string name=new(app.DisplayName.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());if(name.Length>80)name=name[..80];
        string file=Path.Combine(directory,name+".lnk");IntPtr pidl=IntPtr.Zero;object? com=null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(ShellAppPath(app.ShellPath),IntPtr.Zero,out pidl,0,out _));
            com=new ShellLink();((IShellLinkW)com).SetIDList(pidl);((IShellLinkW)com).SetDescription(app.Name);((IPersistFile)com).Save(file,true);return file;
        }
        finally {if(com!=null&&Marshal.IsComObject(com))Marshal.FinalReleaseComObject(com);if(pidl!=IntPtr.Zero)Marshal.FreeCoTaskMem(pidl);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }
    [DllImport("shell32.dll")] private static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHParseDisplayName(string name,IntPtr binding,out IntPtr pidl,uint attributes,out uint actualAttributes);
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW")] private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl,uint attributes,ref SHFILEINFO info,uint size,uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string path, int index, out IntPtr large, out IntPtr small, uint count);
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl); void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short key); void SetHotkey(short key); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
