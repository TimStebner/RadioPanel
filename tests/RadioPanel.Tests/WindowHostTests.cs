using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class WindowHostTests
{
    [TestMethod]
    public void CreatesFrameless320By160Window()
    {
        var assembly = Assembly.Load("RadioPanel");
        var windowType = assembly.GetType("RadioPanel.UI.PanelWindow", throwOnError: false);
        Assert.IsNotNull(windowType, "PanelWindow should be present in the RadioPanel assembly.");

        var factory = windowType.GetMethod("CreateNativeWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotNull(factory, "PanelWindow.CreateNativeWindow should be present.");
        Assert.AreEqual(typeof(nint), factory.ReturnType);

        var hwnd = (nint)factory.Invoke(null, null)!;
        Assert.AreNotEqual(nint.Zero, hwnd, "The native window should have a valid HWND.");
        try
        {
            Assert.IsTrue(IsWindow(hwnd));
            Assert.IsTrue(GetWindowRect(hwnd, out var rect));
            Assert.AreEqual(320, rect.Right - rect.Left);
            Assert.AreEqual(160, rect.Bottom - rect.Top);

            const long styleFlags = 0x00C00000L | 0x00040000L | 0x00800000L; // WS_CAPTION | WS_THICKFRAME | WS_BORDER
            var style = (long)GetWindowLongPtrW(hwnd, -16); // GWL_STYLE
            Assert.AreEqual(0L, style & styleFlags, "The panel should have no non-client frame.");
        }
        finally
        {
            DestroyWindow(hwnd);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hwnd);
}
