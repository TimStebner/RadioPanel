namespace RadioPanel.Platform;

public interface IReconnectTimer
{
    void Start(nint window, uint timerId, uint intervalMs);
    void Stop(nint window, uint timerId);
}
