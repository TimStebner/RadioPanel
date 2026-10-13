using RadioPanel.Domain;
using RadioPanel.SimConnect;

namespace RadioPanel.Aircraft;

public sealed class AircraftRouter
{
    private const uint ReceiveEventFilename = 6;
    private const uint AircraftLoadedEventId = AircraftIdentityProvider.AircraftLoadedEventId;

    private readonly StandardRadioProvider _standard;
    private readonly IRadioProvider _pmdg;
    private readonly IRadioProvider _fenix;
    private nint _handle;
    private AircraftIdentity _identity;
    private AircraftFamily _family = AircraftFamily.Standard;
    private RadioFrequencies _lastPublished;
    private bool _started;

    public AircraftRouter(StandardRadioProvider standard, IRadioProvider pmdg, IRadioProvider fenix)
    {
        _standard = standard ?? throw new ArgumentNullException(nameof(standard));
        _pmdg = pmdg ?? throw new ArgumentNullException(nameof(pmdg));
        _fenix = fenix ?? throw new ArgumentNullException(nameof(fenix));
        _standard.ValuesChanged += OnProviderValuesChanged;
        _pmdg.ValuesChanged += OnProviderValuesChanged;
        _fenix.ValuesChanged += OnProviderValuesChanged;
    }

    public AircraftFamily Family => _family;
    public AircraftIdentity Identity => _identity;
    public RadioFrequencies Current => SelectCurrent();
    public event Action<RadioFrequencies>? ValuesChanged;

    public static AircraftFamily Classify(AircraftIdentity identity)
    {
        var title = identity.Title ?? string.Empty;
        if (title.Contains("PMDG", StringComparison.OrdinalIgnoreCase)
            || title.Contains("737", StringComparison.OrdinalIgnoreCase)
            || title.Contains("777", StringComparison.OrdinalIgnoreCase))
            return AircraftFamily.PMDG;

        if (title.Contains("Fenix", StringComparison.OrdinalIgnoreCase)
            || title.Contains("FNX", StringComparison.OrdinalIgnoreCase))
            return AircraftFamily.Fenix;

        return AircraftFamily.Standard;
    }

    public void Start(nint simConnectHandle)
    {
        if (simConnectHandle == nint.Zero)
            throw new ArgumentException("A valid SimConnect handle is required.", nameof(simConnectHandle));

        Stop();
        _handle = simConnectHandle;
        _started = true;
        _standard.Start(_handle);
        Refresh();
    }

    public void Stop()
    {
        _pmdg.Stop();
        _fenix.Stop();
        _standard.Stop();
        _handle = nint.Zero;
        _started = false;
        _identity = default;
        _family = AircraftFamily.Standard;
        Publish(default);
    }

    public void SetAircraftIdentity(AircraftIdentity identity)
    {
        if (!_started)
        {
            _identity = identity;
            _family = Classify(identity);
            return;
        }

        if (string.IsNullOrWhiteSpace(identity.Title))
        {
            _identity = default;
            _standard.ResetForAircraftChange();
            SelectedSpecialProvider()?.ResetForAircraftChange();
            Refresh();
            return;
        }

        var nextFamily = Classify(identity);
        var previousFamily = _family;
        if (nextFamily != previousFamily)
        {
            _family = nextFamily;
            SelectedSpecialProvider()?.Start(_handle);
        }
        else
        {
            SelectedSpecialProvider()?.ResetForAircraftChange();
        }

        _identity = identity;
        _standard.ResetForAircraftChange();
        Refresh();
    }

    public void OnSimConnectMessage(in SimConnectMessage message)
    {
        if (!_started)
            return;

        if (message.ReceiveId == ReceiveEventFilename && message.EventId == AircraftLoadedEventId)
            SetAircraftIdentity(default);

        _standard.OnSimConnectMessage(message);
        SelectedSpecialProvider()?.OnSimConnectMessage(message);
    }

    public void Refresh() => Publish(SelectCurrent());

    private IRadioProvider? SelectedSpecialProvider()
        => _family switch
        {
            AircraftFamily.PMDG => _pmdg,
            AircraftFamily.Fenix => _fenix,
            _ => null
        };

    private RadioFrequencies SelectCurrent()
    {
        var special = SelectedSpecialProvider();
        if (special is { IsAvailable: true })
            return special.Current;

        var standard = _standard.Current;
        if (_family != AircraftFamily.Fenix)
            return standard;

        return standard with
        {
            Com1StandbyUnverified = standard.Com1StandbyHz.HasValue,
            Com2StandbyUnverified = standard.Com2StandbyHz.HasValue
        };
    }

    private void OnProviderValuesChanged(RadioFrequencies _) => Refresh();

    private void Publish(RadioFrequencies next)
    {
        if (_lastPublished == next)
            return;

        _lastPublished = next;
        ValuesChanged?.Invoke(next);
    }
}
