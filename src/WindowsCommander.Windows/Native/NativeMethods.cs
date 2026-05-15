using System.Runtime.InteropServices;
using System.Text;

namespace WindowsCommander.Windows.Native;

internal static partial class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowRect(nint hWnd, out NativeRect rect);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;
}
