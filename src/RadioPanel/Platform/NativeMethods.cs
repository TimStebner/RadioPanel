using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RadioPanel.Platform;

internal static unsafe partial class NativeMethods
{
    internal const uint CsHRedraw = 0x0002;
    internal const uint CsVRedraw = 0x0001;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsVisible = 0x10000000;
    internal const int BlackBrush = 4;
    internal const int NullPen = 8;
    internal const int CwUseDefault = unchecked((int)0x80000000);

    internal const uint WmClose = 0x0010;
    internal const uint WmDestroy = 0x0002;
    internal const uint WmTimer = 0x0113;
    internal const uint WmPaint = 0x000F;
    internal const uint WmEraseBackground = 0x0014;
    internal const uint WmApp = 0x8000;
    internal const int GwlStyle = -16;
    internal const int ErrorClassAlreadyExists = 1410;
    internal const uint MfString = 0x0000;
    internal const uint MfChecked = 0x0008;
    internal const uint MfSeparator = 0x0800;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmReturnCommand = 0x0100;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint WmContextMenu = 0x007B;
    internal const uint WmNcHitTest = 0x0084;
    internal const uint WmCommand = 0x0111;
    internal const uint WmExitSizeMove = 0x0232;
    internal const int HtCaption = 2;
    internal const int HtClient = 1;

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(in WindowClassEx windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    internal static partial nint CreateWindowEx(
        uint extendedStyle,
        char* className,
        char* windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    internal static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(out NativeMessage message, nint window, uint minFilter, uint maxFilter);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    internal static partial int TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    internal static partial int DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "SetTimer", SetLastError = true)]
    internal static partial nuint SetTimer(nint window, nuint timerId, uint intervalMs, nint timerCallback);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer", SetLastError = true)]
    internal static partial int KillTimer(nint window, nuint timerId);

    [LibraryImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
    internal static partial int SetProcessDpiAwarenessContext(nint dpiContext);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    internal static partial nint GetModuleHandle(nint moduleName);

    [LibraryImport("gdi32.dll", EntryPoint = "GetStockObject")]
    internal static partial nint GetStockObject(int objectType);

    [LibraryImport("user32.dll", EntryPoint = "BeginPaint", SetLastError = true)]
    internal static partial nint BeginPaint(nint window, out NativePaintStruct paint);

    [LibraryImport("user32.dll", EntryPoint = "EndPaint", SetLastError = true)]
    internal static partial int EndPaint(nint window, in NativePaintStruct paint);

    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect", SetLastError = true)]
    internal static partial int InvalidateRect(nint window, nint rectangle, int erase);

    [LibraryImport("user32.dll", EntryPoint = "InvalidateRect", SetLastError = true)]
    internal static partial int InvalidateRect(nint window, in NativeRect rectangle, int erase);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush", SetLastError = true)]
    internal static partial nint CreateSolidBrush(uint colorRef);

    [LibraryImport("user32.dll", EntryPoint = "FillRect")]
    internal static partial int FillRect(nint hdc, in NativeRect rectangle, nint brush);

    [LibraryImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
    internal static partial nint SelectObject(nint hdc, nint value);

    [LibraryImport("gdi32.dll", EntryPoint = "SetBkMode", SetLastError = true)]
    internal static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll", EntryPoint = "SetTextColor", SetLastError = true)]
    internal static partial uint SetTextColor(nint hdc, uint colorRef);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", SetLastError = true)]
    internal static partial int TextOut(nint hdc, int x, int y, char* text, int count);

    [LibraryImport("gdi32.dll", EntryPoint = "Ellipse", SetLastError = true)]
    internal static partial int Ellipse(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", SetLastError = true)]
    internal static partial nint CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        uint weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint characterSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        char* faceName);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
    internal static partial int DeleteObject(nint value);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayMonitors", SetLastError = true)]
    internal static partial int EnumDisplayMonitors(
        nint deviceContext,
        nint clipRectangle,
        delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback,
        nint userData);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    internal static partial int GetMonitorInfo(nint monitor, ref NativeMonitorInfo monitorInfo);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    internal static partial int GetWindowRect(nint window, out NativeRect rectangle);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    internal static partial int SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true)]
    internal static partial int AppendMenu(nint menu, uint flags, nuint itemId, char* itemText);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenu", SetLastError = true)]
    internal static partial uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    internal static partial int DestroyMenu(nint menu);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClassEx
{
    internal uint Size;
    internal uint Style;
    internal delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WindowProcedure;
    internal int ClassExtra;
    internal int WindowExtra;
    internal nint Instance;
    internal nint Icon;
    internal nint Cursor;
    internal nint BackgroundBrush;
    internal char* MenuName;
    internal char* ClassName;
    internal nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    internal nint Window;
    internal uint Message;
    internal nint WParam;
    internal nint LParam;
    internal uint Time;
    internal NativePoint Point;
    internal uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;

    internal NativeRect(int left, int top, int right, int bottom)
        => (Left, Top, Right, Bottom) = (left, top, right, bottom);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePaintStruct
{
    internal nint DeviceContext;
    internal int Erase;
    internal NativeRect Paint;
    internal int Restore;
    internal int IncUpdate;
    internal fixed byte Reserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInfo
{
    internal uint Size;
    internal NativeRect MonitorArea;
    internal NativeRect WorkArea;
    internal uint Flags;

    internal NativeMonitorInfo(uint size)
    {
        Size = size;
        MonitorArea = default;
        WorkArea = default;
        Flags = 0;
    }
}
