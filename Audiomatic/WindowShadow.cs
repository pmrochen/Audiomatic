using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Audiomatic;

internal static class WindowShadow
{
    public static void Apply(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var margins = new MARGINS { bottomHeight = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int leftWidth;
        public int rightWidth;
        public int topHeight;
        public int bottomHeight;
    }
}
