namespace RadioPanel.SimConnect;

public interface ISimConnectApi
{
    int Open(nint window, uint windowMessage, out nint handle);
    int Close(nint handle);
    int GetNextDispatch(nint handle, out SimConnectMessage message);
    int AddToDataDefinition(nint handle, uint definitionId, string variableName, string? unitsName, uint dataType, float epsilon, uint datumId);
    int RequestDataOnSimObject(nint handle, uint requestId, uint definitionId, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);
    int SubscribeToSystemEvent(nint handle, uint eventId, string eventName);
    int MapClientDataNameToId(nint handle, string areaName, uint clientDataId);
    int AddToClientDataDefinition(nint handle, uint definitionId, uint offset, uint sizeOrType, float epsilon, uint datumId);
    int RequestClientData(nint handle, uint clientDataId, uint requestId, uint definitionId, uint period, uint flags, uint origin, uint interval, uint limit);
    int SetClientData(nint handle, uint clientDataId, uint definitionId, uint flags, uint reserved, byte[] data);
}
