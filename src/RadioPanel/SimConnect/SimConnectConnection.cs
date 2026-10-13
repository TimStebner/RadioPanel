using RadioPanel.Domain;
using RadioPanel.Platform;
using System.Runtime.InteropServices;

namespace RadioPanel.SimConnect;

public sealed class SimConnectConnection
{
    public const uint RetryTimerId = 0x52;
    public const uint RetryIntervalMilliseconds = 3000;
    public const uint HandshakeTimeoutMilliseconds = 60_000;
    public const uint SimConnectWindowMessage = NativeMessageId;

    private const uint NativeMessageId = 0x8026; // WM_APP + 0x26
    private const uint ReceiveOpen = 2;
    private const uint ReceiveQuit = 3;
    private const int HResultSFalse = 1;

    private readonly ISimConnectApi _api;
    private readonly IReconnectTimer _retryTimer;
    private nint _window;
    private nint _handle;
    private ConnectionStatus _status = ConnectionStatus.Waiting;
    private uint _handshakeElapsedMilliseconds;
    private bool _timerRunning;
    private bool _stopped;

    public SimConnectConnection(ISimConnectApi api, IReconnectTimer retryTimer)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _retryTimer = retryTimer ?? throw new ArgumentNullException(nameof(retryTimer));
    }

    public ConnectionStatus Status => _status;
    public nint Handle => _handle;

    public event Action<ConnectionStatus>? StateChanged;
    public event Action<SimConnectMessage>? MessageReceived;

    public void Start(nint window)
    {
        if (_stopped)
            throw new InvalidOperationException("A stopped SimConnect connection cannot be restarted.");
        if (window == nint.Zero)
            throw new ArgumentException("A valid window handle is required.", nameof(window));
        if (_window != nint.Zero)
            throw new InvalidOperationException("The SimConnect connection has already started.");

        _window = window;
        AttemptOpen();
    }

    public void HandleRetryTimer()
    {
        if (_stopped || _window == nint.Zero)
            return;

        if (_handle != nint.Zero)
        {
            if (_status != ConnectionStatus.Connecting)
                return;

            _handshakeElapsedMilliseconds += RetryIntervalMilliseconds;
            if (_handshakeElapsedMilliseconds < HandshakeTimeoutMilliseconds)
                return;

            CloseHandle(); // The simulator did not confirm the open request in time.
            SetStatus(ConnectionStatus.Waiting);
        }

        AttemptOpen();
    }

    public void HandleSimConnectWindowMessage()
    {
        if (_stopped || _handle == nint.Zero)
            return;

        int result;
        SimConnectMessage message;
        try
        {
            result = _api.GetNextDispatch(_handle, out message);
        }
        catch (Exception exception) when (IsNativeInteropFailure(exception))
        {
            FailAndRetry();
            return;
        }

        if (result == HResultSFalse)
            return;
        if (result < 0)
        {
            FailAndRetry();
            return;
        }
        if (result != 0)
            return;

        if (message.ReceiveId == 0) // SIMCONNECT_RECV_ID_NULL means the dispatch queue is empty.
            return;

        if (message.ReceiveId == ReceiveOpen)
        {
            StopRetryTimer();
            SetStatus(ConnectionStatus.Connected);
            MessageReceived?.Invoke(message);
            return;
        }

        if (message.ReceiveId == ReceiveQuit)
        {
            CloseHandle();
            SetStatus(ConnectionStatus.Waiting);
            StartRetryTimer();
            MessageReceived?.Invoke(message);
            return;
        }

        MessageReceived?.Invoke(message);
    }

    public void Stop()
    {
        if (_stopped)
            return;

        _stopped = true;
        StopRetryTimer();
        CloseHandle();
        SetStatus(ConnectionStatus.Waiting);
    }

    private void AttemptOpen()
    {
        if (_stopped)
            return;

        _handshakeElapsedMilliseconds = 0;
        SetStatus(ConnectionStatus.Connecting);
        int result;
        nint handle;
        try
        {
            result = _api.Open(_window, SimConnectWindowMessage, out handle);
        }
        catch (Exception exception) when (IsNativeInteropFailure(exception))
        {
            SetStatus(ConnectionStatus.Error);
            StartRetryTimer();
            return;
        }

        if (result < 0 || handle == nint.Zero)
        {
            if (handle != nint.Zero)
                _api.Close(handle);
            SetStatus(ConnectionStatus.Waiting);
            StartRetryTimer();
            return;
        }

        _handle = handle;
        _handshakeElapsedMilliseconds = 0;
        SetStatus(ConnectionStatus.Connecting);
        StartRetryTimer(); // Keep the handle while the simulator completes its asynchronous handshake.
    }

    private void FailAndRetry()
    {
        CloseHandle();
        SetStatus(ConnectionStatus.Error);
        StartRetryTimer();
    }

    private void StartRetryTimer()
    {
        if (_timerRunning || _stopped || _window == nint.Zero)
            return;

        try
        {
            _retryTimer.Start(_window, RetryTimerId, RetryIntervalMilliseconds);
            _timerRunning = true;
        }
        catch (Exception)
        {
            SetStatus(ConnectionStatus.Error);
        }
    }

    private void StopRetryTimer()
    {
        if (!_timerRunning)
            return;

        _timerRunning = false;
        try
        {
            _retryTimer.Stop(_window, RetryTimerId);
        }
        catch (Exception)
        {
            SetStatus(ConnectionStatus.Error);
        }
    }

    private void CloseHandle()
    {
        var handle = _handle;
        _handle = nint.Zero;
        _handshakeElapsedMilliseconds = 0;
        if (handle == nint.Zero)
            return;

        try
        {
            _api.Close(handle);
        }
        catch (Exception exception) when (IsNativeInteropFailure(exception))
        {
            SetStatus(ConnectionStatus.Error);
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (_status == status)
            return;

        _status = status;
        StateChanged?.Invoke(status);
    }

    private static bool IsNativeInteropFailure(Exception exception)
        => exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SEHException;
}
