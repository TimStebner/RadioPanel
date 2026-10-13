using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace RadioPanel.SimConnect;

public sealed unsafe partial class SimConnectNative : ISimConnectApi
{
    private const string ClientName = "RadioPanel";
    private const uint ReceiveException = 1;
    private const uint ReceiveEvent = 4;
    private const uint ReceiveEventFilename = 6;
    private const uint ReceiveSimObjectData = 8;
    private const uint ReceiveSimObjectDataByType = 9;
    private const uint ReceiveClientData = 16;
    private const int ReceiveHeaderSize = 12;
    private const int SimObjectDataHeaderSize = 24;
    private const int ExceptionCodeOffset = 12;
    private const int RequestIdOffset = 12;
    private const int DefinitionIdOffset = 20;
    private const int EventIdOffset = 16;
    private const int HResultFail = unchecked((int)0x80004005);

    public int Open(nint window, uint windowMessage, out nint handle)
    {
        handle = nint.Zero;
        nint openedHandle = nint.Zero;
        fixed (byte* name = "RadioPanel\0"u8)
        {
            var result = NativeMethods.SimConnectOpen(
                &openedHandle,
                name,
                window,
                windowMessage,
                nint.Zero,
                0);
            handle = openedHandle;
            return result;
        }
    }

    public int Close(nint handle) => NativeMethods.SimConnectClose(handle);

    public int AddToDataDefinition(nint handle, uint definitionId, string variableName, string? unitsName, uint dataType, float epsilon, uint datumId)
    {
        var variableBytes = ToNullTerminatedUtf8(variableName);
        fixed (byte* variable = variableBytes)
        {
            if (unitsName is null)
                return NativeMethods.SimConnectAddToDataDefinition(handle, definitionId, variable, null, dataType, epsilon, datumId);

            var unitsBytes = ToNullTerminatedUtf8(unitsName);
            fixed (byte* units = unitsBytes)
                return NativeMethods.SimConnectAddToDataDefinition(handle, definitionId, variable, units, dataType, epsilon, datumId);
        }
    }

    public int RequestDataOnSimObject(nint handle, uint requestId, uint definitionId, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit)
        => NativeMethods.SimConnectRequestDataOnSimObject(handle, requestId, definitionId, objectId, period, flags, origin, interval, limit);

    public int SubscribeToSystemEvent(nint handle, uint eventId, string eventName)
    {
        var eventBytes = ToNullTerminatedUtf8(eventName);
        fixed (byte* eventNamePointer = eventBytes)
            return NativeMethods.SimConnectSubscribeToSystemEvent(handle, eventId, eventNamePointer);
    }

    public int MapClientDataNameToId(nint handle, string areaName, uint clientDataId)
    {
        var areaBytes = ToNullTerminatedUtf8(areaName);
        fixed (byte* area = areaBytes)
            return NativeMethods.SimConnectMapClientDataNameToId(handle, area, clientDataId);
    }

    public int AddToClientDataDefinition(nint handle, uint definitionId, uint offset, uint sizeOrType, float epsilon, uint datumId)
        => NativeMethods.SimConnectAddToClientDataDefinition(handle, definitionId, offset, sizeOrType, epsilon, datumId);

    public int RequestClientData(nint handle, uint clientDataId, uint requestId, uint definitionId, uint period, uint flags, uint origin, uint interval, uint limit)
        => NativeMethods.SimConnectRequestClientData(handle, clientDataId, requestId, definitionId, period, flags, origin, interval, limit);

    public int SetClientData(nint handle, uint clientDataId, uint definitionId, uint flags, uint reserved, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* dataPointer = data)
            return NativeMethods.SimConnectSetClientData(handle, clientDataId, definitionId, flags, reserved, (uint)data.Length, dataPointer);
    }

    public int GetNextDispatch(nint handle, out SimConnectMessage message)
    {
        message = default;
        byte* receiveBuffer = null;
        uint availableBytes = 0;
        var result = NativeMethods.SimConnectGetNextDispatch(handle, &receiveBuffer, &availableBytes);
        if (result != 0)
            return result;

        if (receiveBuffer is null || availableBytes < ReceiveHeaderSize)
            return HResultFail;

        var declaredBytes = *(uint*)receiveBuffer;
        if (declaredBytes < ReceiveHeaderSize || declaredBytes > availableBytes || declaredBytes > int.MaxValue)
            return HResultFail;

        var receiveId = *((uint*)receiveBuffer + 2);
        var requestId = 0u;
        var definitionId = 0u;
        var exceptionCode = 0u;
        var eventId = 0u;

        if ((receiveId is ReceiveSimObjectData or ReceiveSimObjectDataByType or ReceiveClientData) && declaredBytes >= SimObjectDataHeaderSize)
        {
            requestId = *(uint*)(receiveBuffer + RequestIdOffset);
            definitionId = *(uint*)(receiveBuffer + DefinitionIdOffset);
        }
        else if (receiveId == ReceiveException && declaredBytes >= ExceptionCodeOffset + sizeof(uint))
        {
            exceptionCode = *(uint*)(receiveBuffer + ExceptionCodeOffset);
        }
        else if ((receiveId is ReceiveEvent or ReceiveEventFilename) && declaredBytes >= EventIdOffset + sizeof(uint))
        {
            eventId = *(uint*)(receiveBuffer + EventIdOffset);
        }

        var payloadLength = checked((int)declaredBytes - ReceiveHeaderSize);
        var payload = new ReadOnlySpan<byte>(receiveBuffer + ReceiveHeaderSize, payloadLength).ToArray();
        message = new SimConnectMessage(receiveId, requestId, definitionId, exceptionCode, payload, eventId);
        return 0;
    }

    private static byte[] ToNullTerminatedUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Array.Resize(ref bytes, bytes.Length + 1);
        return bytes;
    }

    private static unsafe partial class NativeMethods
    {
        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_Open")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectOpen(
            nint* handle,
            byte* name,
            nint window,
            uint windowMessage,
            nint eventHandle,
            uint configIndex);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_Close")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectClose(nint handle);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_GetNextDispatch")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectGetNextDispatch(nint handle, byte** receive, uint* receiveSize);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_AddToDataDefinition")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectAddToDataDefinition(nint handle, uint definitionId, byte* variableName, byte* unitsName, uint dataType, float epsilon, uint datumId);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_RequestDataOnSimObject")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectRequestDataOnSimObject(nint handle, uint requestId, uint definitionId, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_SubscribeToSystemEvent")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectSubscribeToSystemEvent(nint handle, uint eventId, byte* eventName);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_MapClientDataNameToID")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectMapClientDataNameToId(nint handle, byte* areaName, uint clientDataId);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_AddToClientDataDefinition")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectAddToClientDataDefinition(nint handle, uint definitionId, uint offset, uint sizeOrType, float epsilon, uint datumId);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_RequestClientData")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectRequestClientData(nint handle, uint clientDataId, uint requestId, uint definitionId, uint period, uint flags, uint origin, uint interval, uint limit);

        [LibraryImport("SimConnect.dll", EntryPoint = "SimConnect_SetClientData")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
        internal static partial int SimConnectSetClientData(nint handle, uint clientDataId, uint definitionId, uint flags, uint reserved, uint unitSize, byte* data);
    }
}
