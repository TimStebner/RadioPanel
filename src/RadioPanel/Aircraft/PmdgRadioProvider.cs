using RadioPanel.Domain;
using RadioPanel.SimConnect;

namespace RadioPanel.Aircraft;

public sealed class PmdgRadioProvider : IRadioProvider
{
    public const bool HasVerifiedSdkLayout = false;
    public bool IsAvailable => false;
    public RadioFrequencies Current => default;
    public string UnavailabilityReason => "No verified PMDG SDK data layout exposes the COM active/standby frequency fields.";

    public event Action<RadioFrequencies>? ValuesChanged
    {
        add { }
        remove { }
    }

    public PmdgRadioProvider(ISimConnectApi simConnect)
    {
        ArgumentNullException.ThrowIfNull(simConnect);
    }

    public void Start(nint simConnectHandle) { }
    public void Stop() { }
    public void ResetForAircraftChange() { }
    public void OnSimConnectMessage(in SimConnectMessage message) { }
}
