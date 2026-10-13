using System.Buffers.Binary;
using RadioPanel.Aircraft;
using RadioPanel.Domain;

namespace RadioPanel.SimConnect;

public sealed class StandardRadioProvider : IRadioProvider
{
    public const uint DefinitionId = 11;
    public const uint RequestId = 1;
    public const uint SnapshotRequestId = 3;
    private const uint ReceiveSimObjectData = 8;
    private const uint ReceiveSimObjectDataByType = 9;
    private const uint DataTypeFloat64 = 4;
    private const uint PeriodSecond = 4;
    private const uint RequestFlagChanged = 1;
    private const uint UserObjectId = 0;
    private const uint UnusedDatumId = uint.MaxValue;
    private const int SimObjectDataHeaderLength = 28;
    private static readonly string[] Variables =
    [
        "COM ACTIVE FREQUENCY:1",
        "COM STANDBY FREQUENCY:1",
        "COM ACTIVE FREQUENCY:2",
        "COM STANDBY FREQUENCY:2"
    ];

    private readonly ISimConnectApi _api;
    private nint _handle;
    private RadioFrequencies _current;
    private bool _started;
    private bool _isAvailable;

    public StandardRadioProvider(ISimConnectApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsAvailable => _isAvailable;
    public RadioFrequencies Current => _current;
    public event Action<RadioFrequencies>? ValuesChanged;

    public void Start(nint simConnectHandle)
    {
        Stop();
        if (simConnectHandle == nint.Zero)
            return;

        _handle = simConnectHandle;
        foreach (var variable in Variables)
        {
            if (_api.AddToDataDefinition(_handle, DefinitionId, variable, "MHz", DataTypeFloat64, 0f, UnusedDatumId) < 0)
            {
                _handle = nint.Zero;
                return;
            }
        }

        if (_api.RequestDataOnSimObject(
                _handle,
                RequestId,
                DefinitionId,
                UserObjectId,
                PeriodSecond,
                RequestFlagChanged,
                0,
                0,
                0) < 0)
        {
            _handle = nint.Zero;
            return;
        }

        _started = true;
    }

    public void Stop()
    {
        _started = false;
        _handle = nint.Zero;
        _isAvailable = false;
        SetCurrent(default);
    }

    public void ResetForAircraftChange()
    {
        _isAvailable = false;
        SetCurrent(default);
        if (_started && _handle != nint.Zero)
            _api.RequestDataOnSimObject(_handle, SnapshotRequestId, DefinitionId, UserObjectId, 1, 0, 0, 0, 1);
    }

    public void OnSimConnectMessage(in SimConnectMessage message)
    {
        if (!_started || message.ReceiveId is not (ReceiveSimObjectData or ReceiveSimObjectDataByType)
            || message.RequestId is not (RequestId or SnapshotRequestId)
            || message.DefinitionId != DefinitionId)
            return;

        var data = message.Payload.Span;
        var next = new RadioFrequencies(
            ReadFrequency(data, 0),
            ReadFrequency(data, 1),
            ReadFrequency(data, 2),
            ReadFrequency(data, 3));

        _isAvailable = next.Com1ActiveHz.HasValue || next.Com1StandbyHz.HasValue || next.Com2ActiveHz.HasValue || next.Com2StandbyHz.HasValue;
        SetCurrent(next);
    }

    private static int? ReadFrequency(ReadOnlySpan<byte> payload, int index)
    {
        var offset = SimObjectDataHeaderLength + index * sizeof(double);
        if (payload.Length < offset + sizeof(double))
            return null;

        var bits = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(offset, sizeof(double)));
        var mhz = BitConverter.Int64BitsToDouble(bits);
        return FrequencyFormatter.TryDecodeHertz(mhz * 1_000_000d, out var hertz) ? hertz : null;
    }

    private void SetCurrent(RadioFrequencies next)
    {
        if (_current == next)
            return;

        _current = next;
        ValuesChanged?.Invoke(next);
    }
}
