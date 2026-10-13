using System.Buffers.Binary;
using System.Text;
using RadioPanel.Domain;
using RadioPanel.SimConnect;

namespace RadioPanel.Aircraft;

public sealed class FenixRadioProvider : IRadioProvider
{
    public const string MobiFlightClientName = "RadioPanel";
    public const uint DefaultCommandAreaId = 20;
    public const uint DefaultResponseAreaId = 21;
    public const uint ClientLvarsAreaId = 22;
    public const uint ClientCommandAreaId = 23;
    public const uint ClientResponseAreaId = 24;
    public const uint DefaultCommandDefinitionId = 100;
    public const uint DefaultResponseDefinitionId = 101;
    public const uint ClientCommandDefinitionId = 102;
    public const uint ClientResponseDefinitionId = 103;
    public const uint DefaultResponseRequestId = 100;
    public const uint ClientResponseRequestId = 101;
    public const uint FirstLvarRequestId = 200;
    public const uint FirstLvarDefinitionId = 200;

    private const uint ReceiveClientData = 16;
    private const uint RequestPeriodOnSet = 3;
    private const uint RequestFlagChanged = 1;
    private const uint DefaultFlags = 0;
    private const uint UnusedDatumId = uint.MaxValue;
    private const uint ClientDataChannelBytes = 1024;
    private const int ClientDataHeaderLength = 28;
    private const int LvarValueBytes = sizeof(float);
    private static readonly string[] LvarExpressions =
    [
        "(L:N_PED_RMP1_ACTIVE, Number)",
        "(L:N_PED_RMP1_STDBY, Number)",
        "(L:N_PED_RMP2_ACTIVE, Number)",
        "(L:N_PED_RMP2_STDBY, Number)"
    ];

    private readonly ISimConnectApi _api;
    private readonly float[] _values = new float[4];
    private readonly bool[] _received = new bool[4];
    private nint _handle;
    private nint _initializedHandle;
    private bool _started;
    private bool _defaultChannelsMapped;
    private bool _clientRegistered;
    private bool _isAvailable;
    private RadioFrequencies _current;

    public FenixRadioProvider(ISimConnectApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public bool IsAvailable => _isAvailable;
    public RadioFrequencies Current => _current;
    public event Action<RadioFrequencies>? ValuesChanged;

    public void Start(nint simConnectHandle)
    {
        if (simConnectHandle == nint.Zero)
            return;

        if (_initializedHandle != simConnectHandle)
        {
            ResetSessionState();
            _initializedHandle = simConnectHandle;
        }

        _handle = simConnectHandle;
        _started = true;

        if (_defaultChannelsMapped)
        {
            if (_clientRegistered)
                ResetForAircraftChange();
            else
                BeginClientRegistration();
            return;
        }

        if (_api.MapClientDataNameToId(_handle, "MobiFlight.Command", DefaultCommandAreaId) < 0
            || _api.MapClientDataNameToId(_handle, "MobiFlight.Response", DefaultResponseAreaId) < 0
            || _api.AddToClientDataDefinition(_handle, DefaultCommandDefinitionId, 0, ClientDataChannelBytes, 0f, UnusedDatumId) < 0
            || _api.AddToClientDataDefinition(_handle, DefaultResponseDefinitionId, 0, ClientDataChannelBytes, 0f, UnusedDatumId) < 0
            || _api.RequestClientData(_handle, DefaultResponseAreaId, DefaultResponseRequestId, DefaultResponseDefinitionId, RequestPeriodOnSet, RequestFlagChanged, 0, 0, 0) < 0)
        {
            _started = false;
            return;
        }

        _defaultChannelsMapped = true;
        BeginClientRegistration();
    }

    public void Stop()
    {
        _started = false;
        _handle = nint.Zero;
        _initializedHandle = nint.Zero;
        ResetSessionState();
    }

    public void ResetForAircraftChange()
    {
        ClearSample();
        if (_started && _clientRegistered)
        {
            SendCommand(ClientCommandAreaId, ClientCommandDefinitionId, "MF.SimVars.Clear");
            AddLvars();
        }
    }

    public void OnSimConnectMessage(in SimConnectMessage message)
    {
        if (!_started || message.ReceiveId != ReceiveClientData)
            return;

        var payload = message.Payload.Span;
        if (message.RequestId == DefaultResponseRequestId && payload.Length >= ClientDataHeaderLength)
        {
            var response = ReadAsciiString(payload[ClientDataHeaderLength..]);
            var completedMessage = $"MF.Clients.Add.{MobiFlightClientName}.Finished";
            if (!_clientRegistered && response.Equals(completedMessage, StringComparison.OrdinalIgnoreCase))
                CompleteClientRegistration();
            return;
        }

        if (!_clientRegistered || payload.Length < ClientDataHeaderLength + LvarValueBytes)
            return;

        var index = (int)(message.RequestId - FirstLvarRequestId);
        if ((uint)index >= _values.Length)
            return;

        var sample = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(ClientDataHeaderLength, LvarValueBytes));
        if (!float.IsFinite(sample) || !FrequencyFormatter.TryDecodeHertz(sample * 1000d, out var frequencyHz))
        {
            _received[index] = false;
            UpdateSnapshot();
            return;
        }

        _values[index] = sample;
        _received[index] = true;
        UpdateSnapshot();
    }

    private void BeginClientRegistration()
    {
        SendCommand(DefaultCommandAreaId, DefaultCommandDefinitionId, "MF.Ping");
        SendCommand(DefaultCommandAreaId, DefaultCommandDefinitionId, $"MF.Clients.Add.{MobiFlightClientName}");
    }

    private void CompleteClientRegistration()
    {
        if (_api.MapClientDataNameToId(_handle, $"{MobiFlightClientName}.LVars", ClientLvarsAreaId) < 0
            || _api.MapClientDataNameToId(_handle, $"{MobiFlightClientName}.Command", ClientCommandAreaId) < 0
            || _api.MapClientDataNameToId(_handle, $"{MobiFlightClientName}.Response", ClientResponseAreaId) < 0
            || _api.AddToClientDataDefinition(_handle, ClientCommandDefinitionId, 0, ClientDataChannelBytes, 0f, UnusedDatumId) < 0
            || _api.AddToClientDataDefinition(_handle, ClientResponseDefinitionId, 0, ClientDataChannelBytes, 0f, UnusedDatumId) < 0)
        {
            return;
        }

        for (var index = 0; index < LvarExpressions.Length; index++)
        {
            var definitionId = FirstLvarDefinitionId + (uint)index;
            var requestId = FirstLvarRequestId + (uint)index;
            var offset = (uint)(index * LvarValueBytes);
            if (_api.AddToClientDataDefinition(_handle, definitionId, offset, LvarValueBytes, 0f, UnusedDatumId) < 0
                || _api.RequestClientData(_handle, ClientLvarsAreaId, requestId, definitionId, RequestPeriodOnSet, RequestFlagChanged, 0, 0, 0) < 0)
            {
                return;
            }
        }

        _clientRegistered = true;
        AddLvars();
    }

    private void AddLvars()
    {
        foreach (var expression in LvarExpressions)
            SendCommand(ClientCommandAreaId, ClientCommandDefinitionId, $"MF.SimVars.Add.{expression}");
    }

    private void SendCommand(uint areaId, uint definitionId, string command)
    {
        if (!_started || _handle == nint.Zero)
            return;

        var buffer = new byte[ClientDataChannelBytes];
        var commandBytes = Encoding.ASCII.GetBytes(command);
        if (commandBytes.Length >= buffer.Length)
            return;
        commandBytes.CopyTo(buffer, 0);
        _api.SetClientData(_handle, areaId, definitionId, DefaultFlags, 0, buffer);
    }

    private static string ReadAsciiString(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
            bytes = bytes[..terminator];
        return Encoding.ASCII.GetString(bytes);
    }

    private void ClearSample()
    {
        Array.Clear(_received);
        Array.Clear(_values);
        _isAvailable = false;
        SetCurrent(default);
    }

    private void UpdateSnapshot()
    {
        _isAvailable = _clientRegistered && _received.All(received => received);
        if (!_isAvailable)
        {
            SetCurrent(default);
            return;
        }

        var next = new RadioFrequencies(
            DecodeValue(_values[0]),
            DecodeValue(_values[1]),
            DecodeValue(_values[2]),
            DecodeValue(_values[3]));
        SetCurrent(next);
    }

    private static int? DecodeValue(float value)
        => FrequencyFormatter.TryDecodeHertz(value * 1000d, out var frequencyHz) ? frequencyHz : null;

    private void SetCurrent(RadioFrequencies next)
    {
        if (_current == next)
            return;

        _current = next;
        ValuesChanged?.Invoke(next);
    }

    private void ResetSessionState()
    {
        _defaultChannelsMapped = false;
        _clientRegistered = false;
        ClearSample();
    }
}
