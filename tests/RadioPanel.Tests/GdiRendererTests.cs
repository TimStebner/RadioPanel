using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class GdiRendererTests
{
    [TestMethod]
    public void DrawsDarkBackgroundAndAmberFrequencyTextToNativeWindow()
    {
        var assembly = Assembly.Load("RadioPanel");
        var windowType = assembly.GetType("RadioPanel.UI.PanelWindow", throwOnError: true)!;
        var createWindow = windowType.GetMethod("CreateNativeWindow", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var window = (nint)createWindow.Invoke(null, null)!;
        Assert.AreNotEqual(nint.Zero, window);
        var dc = GetDC(window);
        var rendererType = assembly.GetType("RadioPanel.UI.GdiRenderer", throwOnError: true)!;
        var renderer = Activator.CreateInstance(rendererType, nonPublic: true)!;

        try
        {
            var frequenciesType = assembly.GetType("RadioPanel.Domain.RadioFrequencies", throwOnError: true)!;
            var statusType = assembly.GetType("RadioPanel.Domain.ConnectionStatus", throwOnError: true)!;
            var frequencies = Activator.CreateInstance(frequenciesType, 118_005_000, 122_800_000, 136_975_000, 136_990_000, false, false)!;
            var connected = Enum.Parse(statusType, "Connected");
            var draw = rendererType.GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
            draw.Invoke(renderer, new object[] { dc, new Rectangle(0, 0, 320, 160), frequencies, connected });
            GdiFlush();

            Assert.AreEqual(0x001F1712u, GetPixel(dc, 2, 2), "The panel background should use the dark theme color.");
            Assert.IsTrue(ContainsAmberPixel(dc, new Rectangle(166, 34, 110, 24)), "The active frequency should be drawn in amber.");
        }
        finally
        {
            ((IDisposable)renderer).Dispose();
            ReleaseDC(window, dc);
            DestroyWindow(window);
        }
    }

    private static bool ContainsAmberPixel(nint hdc, Rectangle region)
    {
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            var color = GetPixel(hdc, x, y);
            var red = color & 0xFF;
            var green = (color >> 8) & 0xFF;
            var blue = (color >> 16) & 0xFF;
            if (red > 200 && green is > 120 and < 230 && blue < 30)
                return true;
        }

        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetDC")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
    private static extern int ReleaseDC(nint window, nint hdc);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("gdi32.dll", EntryPoint = "GetPixel")]
    private static extern uint GetPixel(nint hdc, int x, int y);

    [DllImport("gdi32.dll", EntryPoint = "GdiFlush")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
