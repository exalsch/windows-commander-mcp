using System.Runtime.InteropServices;
using System.Text;

namespace WindowsCommander.Windows.Native;

internal static partial class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    internal delegate bool EnumChildWindowsProc(nint hwnd, nint lParam);

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

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(nint hWndParent, EnumChildWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    // Two managed signatures over the same native function: the GET action
    // writes a DWORD through the pointer, the SET action passes the value
    // directly in the pvParam slot.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    internal static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    internal static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool MoveWindow(nint hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, nuint dwExtraInfo);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    internal const uint InputKeyboard = 1;
    internal const uint KeyEventfKeyUp = 0x0002;
    internal const uint KeyEventfUnicode = 0x0004;
    internal const ushort VkReturn = 0x0D;
    internal const ushort VkTab = 0x09;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;
    public InputUnion Union;
}

// The union must be sized to its largest member (MOUSEINPUT) so the marshalled
// INPUT size matches what SendInput expects; otherwise the call is rejected.
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)]
    public MOUSEINPUT Mouse;

    [FieldOffset(0)]
    public KEYBDINPUT Keyboard;

    [FieldOffset(0)]
    public HARDWAREINPUT Hardware;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint Msg;
    public ushort ParamL;
    public ushort ParamH;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeRect
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint
{
    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public readonly int X;
    public readonly int Y;
}
