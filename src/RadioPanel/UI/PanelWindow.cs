using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RadioPanel.Aircraft;
using RadioPanel.Domain;
using RadioPanel.Platform;
using RadioPanel.Settings;
using RadioPanel.SimConnect;

namespace RadioPanel.UI;

internal static unsafe class PanelWindow
{
    private const string WindowClassName = "RadioPanel.NativeWindow";
    private const string WindowTitle = "MSFS Radio Panel";
    private const int DpiAwarenessContextPerMonitorAwareV2 = -4;
    private const int SwShow = 5;
    private const int WindowWidth = 320;
    private const int WindowHeight = 160;
    private const uint MenuAlwaysOnTop = 1;
    private const uint MenuExit = 2;

    private static SimConnectConnection? _connection;
    private static AircraftRouter? _router;
    private static AircraftIdentityProvider? _identityProvider;
    private static GdiRenderer? _renderer;
    private static nint _window;
    private static bool _classRegistered;
    private static bool _shutdown;
    private static RadioFrequencies _frequencies;
    private static ConnectionStatus _status = ConnectionStatus.Waiting;
    private static IReadOnlyList<MonitorWorkArea> _monitors = Array.Empty<MonitorWorkArea>();
    private static string _settingsPath = IniStore.DefaultPath;
    private static PanelSettings _settings;

    internal static int Run()
    {
        _shutdown = false;
        NativeMethods.SetProcessDpiAwarenessContext(new nint(DpiAwarenessContextPerMonitorAwareV2));
        _monitors = MonitorPlacement.GetWorkAreas();
        var defaultPosition = MonitorPlacement.GetDefaultPosition(_monitors, new Size(WindowWidth, WindowHeight));
        _settingsPath = IniStore.DefaultPath;
        _settings = IniStore.Load(_settingsPath, new PanelSettings(defaultPosition.X, defaultPosition.Y, false));
        var restored = MonitorPlacement.ClampToWorkArea(
            new Rectangle(_settings.X, _settings.Y, WindowWidth, WindowHeight),
            _monitors.Select(monitor => monitor.WorkArea).ToArray());
        _settings = _settings with { X = restored.X, Y = restored.Y };
        _renderer = new GdiRenderer();
        var window = CreateNativeWindow();
        if (window == nint.Zero)
        {
            _renderer.Dispose();
            _renderer = null;
            return Marshal.GetLastPInvokeError();
        }

        _window = window;
        ApplyTopmost();
        InitializeSimConnect(window);
        try
        {
            while (true)
            {
                var result = NativeMethods.GetMessage(out var message, nint.Zero, 0, 0);
                if (result <= 0)
                    return result == 0 ? (int)message.WParam : Marshal.GetLastPInvokeError();

                NativeMethods.TranslateMessage(in message);
                NativeMethods.DispatchMessage(in message);
            }
        }
        finally
        {
            Shutdown();
        }
    }

    internal static nint CreateNativeWindow()
    {
        NativeMethods.SetProcessDpiAwarenessContext(new nint(DpiAwarenessContextPerMonitorAwareV2));

        if (!_classRegistered && !RegisterWindowClass())
            return nint.Zero;

        fixed (char* className = WindowClassName)
        fixed (char* windowTitle = WindowTitle)
        {
            var window = NativeMethods.CreateWindowEx(
                0,
                className,
                windowTitle,
                NativeMethods.WsPopup | NativeMethods.WsVisible,
                _settings.X,
                _settings.Y,
                WindowWidth,
                WindowHeight,
                nint.Zero,
                nint.Zero,
                NativeMethods.GetModuleHandle(nint.Zero),
                nint.Zero);

            if (window != nint.Zero)
                NativeMethods.ShowWindow(window, SwShow);

            return window;
        }
    }

    private static void InitializeSimConnect(nint window)
    {
        var api = new SimConnectNative();
        var standard = new StandardRadioProvider(api);
        var pmdg = new PmdgRadioProvider(api);
        var fenix = new FenixRadioProvider(api);
        var router = new AircraftRouter(standard, pmdg, fenix);
        var identity = new AircraftIdentityProvider(api);
        var connection = new SimConnectConnection(api, new Win32ReconnectTimer());

        _router = router;
        _identityProvider = identity;
        _connection = connection;

        router.ValuesChanged += ApplyFrequencies;
        identity.IdentityChanged += router.SetAircraftIdentity;
        connection.StateChanged += OnConnectionStateChanged;
        connection.MessageReceived += message =>
        {
            identity.OnSimConnectMessage(message);
            router.OnSimConnectMessage(message);
        };
        connection.Start(window);
    }

    private static void OnConnectionStateChanged(ConnectionStatus status)
    {
        ApplyStatus(status);
        if (_connection is null || _router is null || _identityProvider is null)
            return;

        if (status == ConnectionStatus.Connected && _connection.Handle != nint.Zero)
        {
            _router.Start(_connection.Handle);
            _identityProvider.Start(_connection.Handle);
        }
        else
        {
            _identityProvider.Stop();
            _router.Stop();
        }
    }

    private static bool RegisterWindowClass()
    {
        fixed (char* className = WindowClassName)
        {
            var windowClass = new WindowClassEx
            {
                Size = (uint)sizeof(WindowClassEx),
                Style = NativeMethods.CsHRedraw | NativeMethods.CsVRedraw,
                WindowProcedure = &WindowProcedure,
                Instance = NativeMethods.GetModuleHandle(nint.Zero),
                BackgroundBrush = NativeMethods.GetStockObject(NativeMethods.BlackBrush),
                ClassName = className
            };

            var atom = NativeMethods.RegisterClassEx(in windowClass);
            var error = Marshal.GetLastPInvokeError();
            if (atom == 0 && error != NativeMethods.ErrorClassAlreadyExists)
                return false;

            _classRegistered = true;
            return true;
        }
    }

    private static void ApplyFrequencies(RadioFrequencies next)
    {
        var dirtyRegions = DirtyRegionCalculator.GetDirtyRegions(_frequencies, next, _status, _status);
        _frequencies = next;
        Invalidate(dirtyRegions);
    }

    private static void ApplyStatus(ConnectionStatus next)
    {
        var dirtyRegions = DirtyRegionCalculator.GetDirtyRegions(_frequencies, _frequencies, _status, next);
        _status = next;
        Invalidate(dirtyRegions);
    }

    private static void Invalidate(IReadOnlyList<Rectangle> dirtyRegions)
    {
        if (_window == nint.Zero)
            return;

        foreach (var region in dirtyRegions)
        {
            var rectangle = new NativeRect(region.Left, region.Top, region.Right, region.Bottom);
            NativeMethods.InvalidateRect(_window, in rectangle, 0);
        }
    }

    private static void Shutdown()
    {
        if (_shutdown)
            return;

        _shutdown = true;
        _connection?.Stop();
        _identityProvider?.Stop();
        _router?.Stop();
        _connection = null;
        _identityProvider = null;
        _router = null;
        _window = nint.Zero;
        _renderer?.Dispose();
        _renderer = null;
    }

    private static void SaveWindowSettings()
    {
        if (_window != nint.Zero && NativeMethods.GetWindowRect(_window, out var rectangle) != 0)
        {
            _settings = _settings with { X = rectangle.Left, Y = rectangle.Top };
        }

        IniStore.Save(_settingsPath, _settings);
    }

    private static void ApplyTopmost()
    {
        if (_window == nint.Zero)
            return;

        var insertAfter = _settings.AlwaysOnTop ? new nint(-1) : new nint(-2);
        NativeMethods.SetWindowPos(
            _window,
            insertAfter,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private static void ShowContextMenu(nint window, nint lParam)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == nint.Zero)
            return;

        try
        {
            fixed (char* topmostLabel = "Always on Top\0")
            fixed (char* exitLabel = "Exit\0")
            {
                var topmostFlags = NativeMethods.MfString | (_settings.AlwaysOnTop ? NativeMethods.MfChecked : 0);
                NativeMethods.AppendMenu(menu, topmostFlags, MenuAlwaysOnTop, topmostLabel);
                NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
                NativeMethods.AppendMenu(menu, NativeMethods.MfString, MenuExit, exitLabel);
            }

            var x = unchecked((short)((long)lParam & 0xFFFF));
            var y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            if (x == -1 && y == -1 && NativeMethods.GetWindowRect(window, out var bounds) != 0)
            {
                x = (short)(bounds.Left + (bounds.Right - bounds.Left) / 2);
                y = (short)(bounds.Top + (bounds.Bottom - bounds.Top) / 2);
            }

            var command = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCommand,
                x,
                y,
                0,
                window,
                nint.Zero);
            if (command == MenuAlwaysOnTop)
            {
                _settings = _settings with { AlwaysOnTop = !_settings.AlwaysOnTop };
                ApplyTopmost();
                SaveWindowSettings();
            }
            else if (command == MenuExit)
            {
                SaveWindowSettings();
                NativeMethods.DestroyWindow(window);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmClose:
                SaveWindowSettings();
                NativeMethods.DestroyWindow(window);
                return nint.Zero;
            case NativeMethods.WmDestroy:
                Shutdown();
                NativeMethods.PostQuitMessage(0);
                return nint.Zero;
            case NativeMethods.WmEraseBackground:
                return _renderer is null ? NativeMethods.DefWindowProc(window, message, wParam, lParam) : new nint(1);
            case NativeMethods.WmPaint:
                Paint(window);
                return nint.Zero;
            case NativeMethods.WmContextMenu:
                ShowContextMenu(window, lParam);
                return nint.Zero;
            case NativeMethods.WmExitSizeMove:
                SaveWindowSettings();
                return nint.Zero;
            case NativeMethods.WmNcHitTest:
                return new nint(NativeMethods.HtCaption);
            default:
                if (message == SimConnectConnection.SimConnectWindowMessage)
                {
                    _connection?.HandleSimConnectWindowMessage();
                    return nint.Zero;
                }

                if (message == NativeMethods.WmTimer && unchecked((uint)wParam) == SimConnectConnection.RetryTimerId)
                {
                    _connection?.HandleRetryTimer();
                    return nint.Zero;
                }

                return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private static void Paint(nint window)
    {
        var hdc = NativeMethods.BeginPaint(window, out var paint);
        try
        {
            if (hdc == nint.Zero)
                return;

            var bounds = new Rectangle(
                paint.Paint.Left,
                paint.Paint.Top,
                paint.Paint.Right - paint.Paint.Left,
                paint.Paint.Bottom - paint.Paint.Top);
            _renderer?.Draw(hdc, bounds, _frequencies, _status);
        }
        finally
        {
            NativeMethods.EndPaint(window, in paint);
        }
    }
}
