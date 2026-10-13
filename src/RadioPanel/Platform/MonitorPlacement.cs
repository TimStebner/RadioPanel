using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RadioPanel.Platform;

public readonly record struct MonitorWorkArea(Rectangle WorkArea, bool IsPrimary);

public static unsafe class MonitorPlacement
{
    private const uint MonitorInfoPrimary = 1;
    private const int MonitorEnumerationContinue = 1;

    public static IReadOnlyList<MonitorWorkArea> GetWorkAreas()
    {
        var areas = new List<MonitorWorkArea>(2);
        var context = GCHandle.Alloc(areas);
        try
        {
            NativeMethods.EnumDisplayMonitors(
                nint.Zero,
                nint.Zero,
                &MonitorCallback,
                GCHandle.ToIntPtr(context));
        }
        finally
        {
            context.Free();
        }

        return areas;
    }

    public static Rectangle ClampToWorkArea(Rectangle window, IReadOnlyList<Rectangle> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        var validAreas = workAreas.Where(area => area.Width > 0 && area.Height > 0).ToArray();
        if (validAreas.Length == 0 || validAreas.Any(area => area.Contains(window)))
            return window;

        var target = validAreas
            .Select(area => new
            {
                Area = area,
                Overlap = Rectangle.Intersect(area, window),
                Distance = CenterDistanceSquared(area, window)
            })
            .OrderByDescending(candidate => candidate.Overlap.Width * (long)candidate.Overlap.Height)
            .ThenBy(candidate => candidate.Distance)
            .First()
            .Area;

        var maxX = Math.Max(target.Left, target.Right - window.Width);
        var maxY = Math.Max(target.Top, target.Bottom - window.Height);
        var x = Math.Clamp(window.X, target.Left, maxX);
        var y = Math.Clamp(window.Y, target.Top, maxY);
        return new Rectangle(x, y, window.Width, window.Height);
    }

    public static Point GetDefaultPosition(IReadOnlyList<MonitorWorkArea> monitors, Size windowSize)
    {
        if (monitors.Count == 0)
            return new Point(40, 40);

        var target = monitors.FirstOrDefault(monitor => !monitor.IsPrimary);
        if (target == default)
            target = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
        if (target == default)
            target = monitors[0];

        var insetWindow = new Rectangle(
            target.WorkArea.Left + 24,
            target.WorkArea.Top + 24,
            windowSize.Width,
            windowSize.Height);
        return ClampToWorkArea(insetWindow, monitors.Select(monitor => monitor.WorkArea).ToArray()).Location;
    }

    private static long CenterDistanceSquared(Rectangle area, Rectangle window)
    {
        var dx = (long)area.Left + area.Width / 2 - ((long)window.Left + window.Width / 2);
        var dy = (long)area.Top + area.Height / 2 - ((long)window.Top + window.Height / 2);
        return dx * dx + dy * dy;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int MonitorCallback(nint monitor, nint deviceContext, nint monitorRectangle, nint userData)
    {
        try
        {
            var info = new NativeMonitorInfo((uint)Marshal.SizeOf<NativeMonitorInfo>());
            if (NativeMethods.GetMonitorInfo(monitor, ref info) == 0)
                return MonitorEnumerationContinue;

            var handle = GCHandle.FromIntPtr(userData);
            var areas = (List<MonitorWorkArea>)handle.Target!;
            areas.Add(new MonitorWorkArea(
                new Rectangle(
                    info.WorkArea.Left,
                    info.WorkArea.Top,
                    info.WorkArea.Right - info.WorkArea.Left,
                    info.WorkArea.Bottom - info.WorkArea.Top),
                (info.Flags & MonitorInfoPrimary) != 0));
        }
        catch
        {
            return 0;
        }

        return MonitorEnumerationContinue;
    }
}
