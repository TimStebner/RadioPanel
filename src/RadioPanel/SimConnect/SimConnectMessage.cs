namespace RadioPanel.SimConnect;

public readonly struct SimConnectMessage
{
    private readonly byte[]? _payload;

    public SimConnectMessage(
        uint receiveId,
        uint requestId,
        uint definitionId,
        uint exceptionCode,
        byte[] payload)
        : this(receiveId, requestId, definitionId, exceptionCode, payload, 0)
    {
    }

    public SimConnectMessage(
        uint receiveId,
        uint requestId,
        uint definitionId,
        uint exceptionCode,
        byte[] payload,
        uint eventId)
    {
        ReceiveId = receiveId;
        RequestId = requestId;
        DefinitionId = definitionId;
        ExceptionCode = exceptionCode;
        EventId = eventId;
        _payload = payload is null ? Array.Empty<byte>() : (byte[])payload.Clone();
    }

    public uint ReceiveId { get; }
    public uint RequestId { get; }
    public uint DefinitionId { get; }
    public uint ExceptionCode { get; }
    public uint EventId { get; }
    public ReadOnlyMemory<byte> Payload => _payload ?? Array.Empty<byte>();
}
