using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RadioPanel.Platform;

public sealed class Win32ReconnectTimer : IReconnectTimer
{
    public void Start(nint window, uint timerId, uint intervalMs)
    {
        if (NativeMethods.SetTimer(window, (nuint)timerId, intervalMs, nint.Zero) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the SimConnect retry timer.");
    }

    public void Stop(nint window, uint timerId)
    {
        NativeMethods.KillTimer(window, (nuint)timerId);
    }
}
