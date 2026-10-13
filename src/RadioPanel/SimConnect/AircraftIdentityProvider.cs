using System.Text;
using RadioPanel.Domain;

namespace RadioPanel.SimConnect;

public sealed class AircraftIdentityProvider
{
    public const uint DefinitionId = 12;
    public const uint RequestId = 2;
    public const uint AircraftLoadedEventId = 0x41;

    private const uint ReceiveSimObjectData = 8;
    private const uint ReceiveSimObjectDataByType = 9;
    private const uint ReceiveEventFilename = 6;
    private const uint DataTypeString128 = 8;
    private const uint DataTypeString256 = 9;
    private const uint PeriodOnce = 1;
    private const uint UserObjectId = 0;
    private const uint DefaultFlags = 0;
    private const uint UnusedDatumId = uint.MaxValue;
    private const int SimObjectDataHeaderLength = 28;
    private const int TitleLength = 256;
    private const int AtcModelLength = 128;

    private readonly ISimConnectApi _api;
    private nint _handle;
    private bool _started;
    private AircraftIdentity _current;

    public AircraftIdentityProvider(ISimConnectApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_current.Title);
    public AircraftIdentity Current => _current;
    public event Action<AircraftIdentity>? IdentityChanged;

    public void Start(nint simConnectHandle)
    {
        Stop();
        if (simConnectHandle == nint.Zero)
            return;

        _handle = simConnectHandle;
        if (_api.AddToDataDefinition(_handle, DefinitionId, "TITLE", null, DataTypeString256, 0f, UnusedDatumId) < 0
            || _api.AddToDataDefinition(_handle, DefinitionId, "ATC MODEL", null, DataTypeString128, 0f, UnusedDatumId) < 0
            || _api.SubscribeToSystemEvent(_handle, AircraftLoadedEventId, "AircraftLoaded") < 0)
        {
            _handle = nint.Zero;
            return;
        }

        _started = true;
        RequestIdentity();
    }

    public void Stop()
    {
        _started = false;
        _handle = nint.Zero;
        SetCurrent(default);
    }

    public void OnSimConnectMessage(in SimConnectMessage message)
    {
        if (!_started)
            return;

        if (message.ReceiveId == ReceiveEventFilename && message.EventId == AircraftLoadedEventId)
        {
            SetCurrent(default);
            RequestIdentity();
            return;
        }

        if (message.ReceiveId is not (ReceiveSimObjectData or ReceiveSimObjectDataByType)
            || message.RequestId != RequestId
            || message.DefinitionId != DefinitionId)
            return;

        var payload = message.Payload.Span;
        if (payload.Length < SimObjectDataHeaderLength + TitleLength + AtcModelLength)
            return;

        var title = ReadString(payload.Slice(SimObjectDataHeaderLength, TitleLength));
        var atcModel = ReadString(payload.Slice(SimObjectDataHeaderLength + TitleLength, AtcModelLength));
        SetCurrent(new AircraftIdentity(title, atcModel));
    }

    private void RequestIdentity()
    {
        if (!_started)
            return;

        _api.RequestDataOnSimObject(_handle, RequestId, DefinitionId, UserObjectId, PeriodOnce, DefaultFlags, 0, 0, 1);
    }

    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
            bytes = bytes[..terminator];
        return Encoding.UTF8.GetString(bytes);
    }

    private void SetCurrent(AircraftIdentity next)
    {
        if (_current == next)
            return;

        _current = next;
        IdentityChanged?.Invoke(next);
    }
}
