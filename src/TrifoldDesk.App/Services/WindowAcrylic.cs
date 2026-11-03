using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TrifoldDesk.Services;

/// <summary>
/// 为 WPF 窗口启用 Windows DWM 原生亚克力（Acrylic）背景模糊，
/// 实现真正的高斯模糊透光液态玻璃效果。
/// </summary>
public static class WindowAcrylic
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint window, ref WindowCompositionAttributeData data);

    /// <summary>
    /// 给 WPF 窗口施加原生亚克力/毛玻璃模糊效果。
    /// </summary>
    /// <param name="window">目标 WPF 窗口</param>
    /// <param name="tintAlpha">亚克力色调不透明度 (0~255)，推荐 30~55 保证轻薄通透</param>
    /// <param name="red">背景偏色 R (默认深邃黑透 18)</param>
    /// <param name="green">背景偏色 G (默认 22)</param>
    /// <param name="blue">背景偏色 B (默认 30)</param>
    public static void ApplyAcrylic(Window window, byte tintAlpha = 42, byte red = 18, byte green = 22, byte blue = 30)
    {
        if (window == null) return;

        if (window.IsLoaded)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != 0) TryApply(hwnd, tintAlpha, red, green, blue);
        }
        else
        {
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != 0) TryApply(hwnd, tintAlpha, red, green, blue);
            };
        }
    }

    /// <summary>
    /// 直接针对 HWND 句柄应用亚克力模糊。
    /// </summary>
    public static bool TryApply(nint window, byte tintAlpha, byte red, byte green, byte blue)
    {
        if (window == 0) return false;

        // GradientColor 为 ABGR 顺序
        var gradientColor = (tintAlpha << 24) | (blue << 16) | (green << 8) | red;

        var policy = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2, // 向四周延伸模糊，避免边缘亮边
            GradientColor = gradientColor,
            AnimationId = 0
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size
            };

            int res = SetWindowCompositionAttribute(window, ref data);
            if (res == 0)
            {
                // 若系统不支持 AcrylicBlurBehind，退守基础 BlurBehind
                policy.AccentState = AccentEnableBlurBehind;
                Marshal.StructureToPtr(policy, buffer, false);
                res = SetWindowCompositionAttribute(window, ref data);
            }
            return res != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
