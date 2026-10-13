using RadioPanel.Domain;
using RadioPanel.SimConnect;

namespace RadioPanel.Aircraft;

public interface IRadioProvider
{
    bool IsAvailable { get; }
    RadioFrequencies Current { get; }
    event Action<RadioFrequencies>? ValuesChanged;
    void Start(nint simConnectHandle);
    void Stop();
    void ResetForAircraftChange();
    void OnSimConnectMessage(in SimConnectMessage message);
}
