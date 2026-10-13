using System.Collections;
using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class SettingsTests
{
    [TestMethod]
    public void ValidIniRoundTripsPositionAndTopmostSetting()
    {
        var (settingsType, iniStore) = GetSettingsApi();
        var settings = Activator.CreateInstance(settingsType, 1440, 620, true)!;
        var path = Path.Combine(Path.GetTempPath(), $"radio-panel-{Guid.NewGuid():N}.ini");

        try
        {
            InvokeStatic(iniStore, "Save", path, settings);
            var loaded = InvokeStatic(iniStore, "Load", path, settings);

            Assert.AreEqual(1440, ReadProperty(loaded!, "X"));
            Assert.AreEqual(620, ReadProperty(loaded!, "Y"));
            Assert.AreEqual(true, ReadProperty(loaded!, "AlwaysOnTop"));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [TestMethod]
    public void MissingAndMalformedIniUseSafeFallbackValues()
    {
        var (settingsType, iniStore) = GetSettingsApi();
        var fallback = Activator.CreateInstance(settingsType, 40, 55, false)!;
        var missingPath = Path.Combine(Path.GetTempPath(), $"radio-panel-missing-{Guid.NewGuid():N}.ini");
        var malformedPath = Path.Combine(Path.GetTempPath(), $"radio-panel-bad-{Guid.NewGuid():N}.ini");

        try
        {
            var missing = InvokeStatic(iniStore, "Load", missingPath, fallback)!;
            Assert.AreEqual(40, ReadProperty(missing, "X"));
            Assert.AreEqual(55, ReadProperty(missing, "Y"));
            Assert.AreEqual(false, ReadProperty(missing, "AlwaysOnTop"));

            File.WriteAllText(malformedPath, "X=not-an-int\r\nY=-2147483649\r\nAlwaysOnTop=maybe\r\n");
            var malformed = InvokeStatic(iniStore, "Load", malformedPath, fallback)!;
            Assert.AreEqual(40, ReadProperty(malformed, "X"));
            Assert.AreEqual(55, ReadProperty(malformed, "Y"));
            Assert.AreEqual(false, ReadProperty(malformed, "AlwaysOnTop"));
        }
        finally
        {
            if (File.Exists(malformedPath))
                File.Delete(malformedPath);
        }
    }

    [TestMethod]
    public void ClampsOffscreenWindowToVisibleWorkArea()
    {
        var assembly = Assembly.Load("RadioPanel");
        var placementType = assembly.GetType("RadioPanel.Platform.MonitorPlacement", throwOnError: true)!;
        var method = placementType.GetMethod("ClampToWorkArea", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "Expected MonitorPlacement.ClampToWorkArea.");

        var monitors = new[]
        {
            new Rectangle(-1920, 0, 1920, 1040),
            new Rectangle(0, 0, 1920, 1040)
        };
        var initial = new Rectangle(5000, 2600, 320, 160);
        var clamped = (Rectangle)method.Invoke(null, new object[] { initial, monitors })!;

        Assert.IsTrue(monitors.Any(monitor => monitor.Contains(clamped)), $"Window {clamped} should fit a visible work area.");
    }

    [TestMethod]
    public void EnumeratesNativeMonitorWorkAreas()
    {
        var assembly = Assembly.Load("RadioPanel");
        var placementType = assembly.GetType("RadioPanel.Platform.MonitorPlacement", throwOnError: true)!;
        var method = placementType.GetMethod("GetWorkAreas", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "Expected MonitorPlacement.GetWorkAreas.");

        var monitors = (IEnumerable)method.Invoke(null, null)!;
        var entries = monitors.Cast<object>().ToArray();
        Assert.IsTrue(entries.Length > 0, "Windows should report at least one display monitor.");
        var workArea = (Rectangle)ReadProperty(entries[0], "WorkArea")!;
        Assert.IsTrue(workArea.Width > 0 && workArea.Height > 0);
    }

    private static (Type Settings, Type IniStore) GetSettingsApi()
    {
        var assembly = Assembly.Load("RadioPanel");
        return (
            assembly.GetType("RadioPanel.Settings.PanelSettings", throwOnError: true)!,
            assembly.GetType("RadioPanel.Settings.IniStore", throwOnError: true)!);
    }

    private static object? InvokeStatic(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, $"Expected {type.Name}.{methodName}.");
        return method.Invoke(null, args);
    }

    private static object? ReadProperty(object value, string name)
        => value.GetType().GetProperty(name)?.GetValue(value);
}
