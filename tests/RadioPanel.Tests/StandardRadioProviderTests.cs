using System.Buffers.Binary;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class StandardRadioProviderTests
{
    private const uint ReceiveSimObjectData = 8;

    [TestMethod]
    public void RegistersFourMhzFloat64FieldsAndChangedOnlyOneSecondRequest()
    {
        var h = CreateHarness("RadioPanel.SimConnect.StandardRadioProvider");

        Invoke(h.Provider, "Start", new nint(77));

        var definitions = h.Api.Calls.Where(call => call.Name == "AddToDataDefinition").ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "COM ACTIVE FREQUENCY:1",
                "COM STANDBY FREQUENCY:1",
                "COM ACTIVE FREQUENCY:2",
                "COM STANDBY FREQUENCY:2"
            },
            definitions.Select(call => (string)call.Arguments[2]!).ToArray());
        Assert.IsTrue(definitions.All(call => (string)call.Arguments[3]! == "MHz"));
        Assert.IsTrue(definitions.All(call => (uint)call.Arguments[4]! == 4), "Data type must be SIMCONNECT_DATATYPE_FLOAT64.");
        Assert.IsTrue(definitions.All(call => (nint)call.Arguments[0]! == new nint(77)));

        var request = h.Api.Calls.Single(call => call.Name == "RequestDataOnSimObject");
        Assert.AreEqual(4u, (uint)request.Arguments[4]!, "SIMCONNECT_PERIOD_SECOND is 4.");
        Assert.AreEqual(1u, (uint)request.Arguments[5]!, "SIMCONNECT_DATA_REQUEST_FLAG_CHANGED is 1.");
        Assert.AreEqual(0u, (uint)request.Arguments[3]!, "Request targets SIMCONNECT_OBJECT_ID_USER.");
    }

    [TestMethod]
    public void PreservesAvailableValuesWhenOneFieldIsMissing()
    {
        var h = CreateHarness("RadioPanel.SimConnect.StandardRadioProvider");
        Invoke(h.Provider, "Start", new nint(77));
        SendRadioData(h.Provider, 118.005, double.NaN, 122.800, 136.990);

        var current = h.Provider.GetType().GetProperty("Current")!.GetValue(h.Provider)!;
        Assert.AreEqual(118_005_000, ReadProperty(current, "Com1ActiveHz"));
        Assert.IsNull(ReadProperty(current, "Com1StandbyHz"));
        Assert.AreEqual(122_800_000, ReadProperty(current, "Com2ActiveHz"));
        Assert.AreEqual(136_990_000, ReadProperty(current, "Com2StandbyHz"));
        Assert.AreEqual(true, h.Provider.GetType().GetProperty("IsAvailable")!.GetValue(h.Provider));
    }

    [TestMethod]
    public void DoesNotRaiseChangeForIdenticalValues()
    {
        var h = CreateHarness("RadioPanel.SimConnect.StandardRadioProvider");
        Invoke(h.Provider, "Start", new nint(77));

        var eventInfo = h.Provider.GetType().GetEvent("ValuesChanged")
            ?? throw new AssertFailedException("Expected StandardRadioProvider.ValuesChanged.");
        var valueType = eventInfo.EventHandlerType!.GetGenericArguments()[0];
        var counterType = typeof(EventCounter<>).MakeGenericType(valueType);
        var counter = Activator.CreateInstance(counterType)!;
        var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, counter, counterType.GetMethod("OnChanged")!);
        eventInfo.AddEventHandler(h.Provider, handler);

        SendRadioData(h.Provider, 118.005, 122.800, 136.975, 136.990);
        SendRadioData(h.Provider, 118.005, 122.800, 136.975, 136.990);

        Assert.AreEqual(1, (int)counterType.GetProperty("Count")!.GetValue(counter)!);
    }

    [TestMethod]
    public void RequestsAircraftIdentityOnStartAndAircraftLoaded()
    {
        var h = CreateHarness("RadioPanel.SimConnect.AircraftIdentityProvider");

        Invoke(h.Provider, "Start", new nint(77));

        var definitions = h.Api.Calls.Where(call => call.Name == "AddToDataDefinition").ToArray();
        CollectionAssert.AreEqual(new[] { "TITLE", "ATC MODEL" }, definitions.Select(call => (string)call.Arguments[2]!).ToArray());
        CollectionAssert.AreEqual(new uint[] { 9, 8 }, definitions.Select(call => (uint)call.Arguments[4]!).ToArray());
        Assert.AreEqual("AircraftLoaded", h.Api.Calls.Single(call => call.Name == "SubscribeToSystemEvent").Arguments[2]);
        Assert.AreEqual(1u, (uint)h.Api.Calls.Single(call => call.Name == "RequestDataOnSimObject").Arguments[4]!, "SIMCONNECT_PERIOD_ONCE is 1.");

        var id = (uint)h.Api.Calls.Single(call => call.Name == "SubscribeToSystemEvent").Arguments[1]!;
        var eventPayload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(eventPayload.AsSpan(4), id);
        var eventMessage = CreateMessage(6, 0, 0, 0, eventPayload, id);
        Invoke(h.Provider, "OnSimConnectMessage", eventMessage);
        Assert.AreEqual(2, h.Api.Calls.Count(call => call.Name == "RequestDataOnSimObject"));

        SendIdentityData(h.Provider, "Example C172", "C172");
        var identity = h.Provider.GetType().GetProperty("Current")!.GetValue(h.Provider)!;
        Assert.AreEqual("Example C172", ReadProperty(identity, "Title"));
        Assert.AreEqual("C172", ReadProperty(identity, "AtcModel"));
    }

    private static Harness CreateHarness(string providerTypeName)
    {
        var assembly = Assembly.Load("RadioPanel");
        var apiType = RequireType(assembly, "RadioPanel.SimConnect.ISimConnectApi");
        var providerType = RequireType(assembly, providerTypeName);
        var constructor = providerType.GetConstructor(new[] { apiType });
        Assert.IsNotNull(constructor, $"{providerTypeName} should accept ISimConnectApi.");

        var api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        return new Harness(constructor.Invoke(new object[] { api }), api);
    }

    private static void SendRadioData(object provider, double com1Active, double com1Standby, double com2Active, double com2Standby)
    {
        var payload = CreateDataPayload(32);
        WriteDouble(payload, 28, com1Active);
        WriteDouble(payload, 36, com1Standby);
        WriteDouble(payload, 44, com2Active);
        WriteDouble(payload, 52, com2Standby);
        Invoke(provider, "OnSimConnectMessage", CreateMessage(ReceiveSimObjectData, 1, 11, 0, payload));
    }

    private static void SendIdentityData(object provider, string title, string atcModel)
    {
        var payload = CreateDataPayload(384);
        System.Text.Encoding.UTF8.GetBytes(title, payload.AsSpan(28, 256));
        System.Text.Encoding.UTF8.GetBytes(atcModel, payload.AsSpan(284, 128));
        Invoke(provider, "OnSimConnectMessage", CreateMessage(ReceiveSimObjectData, 2, 12, 0, payload));
    }

    private static byte[] CreateDataPayload(int dataBytes) => new byte[28 + dataBytes];

    private static void WriteDouble(byte[] payload, int offset, double value)
        => BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset, sizeof(long)), BitConverter.DoubleToInt64Bits(value));

    private static object CreateMessage(uint receiveId, uint requestId, uint definitionId, uint exceptionCode, byte[] payload, uint eventId = 0)
    {
        var type = RequireType(Assembly.Load("RadioPanel"), "RadioPanel.SimConnect.SimConnectMessage");
        var constructor = type.GetConstructor(new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(byte[]), typeof(uint) });
        return constructor is null
            ? Activator.CreateInstance(type, receiveId, requestId, definitionId, exceptionCode, payload)!
            : constructor.Invoke(new object[] { receiveId, requestId, definitionId, exceptionCode, payload, eventId });
    }

    private static object? ReadProperty(object value, string name) => value.GetType().GetProperty(name)?.GetValue(value);

    private static Type RequireType(Assembly assembly, string name)
        => assembly.GetType(name, throwOnError: false) ?? throw new AssertFailedException($"Expected production type {name}.");

    private static void Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(method, $"Expected method {methodName}.");
        method.Invoke(target, arguments);
    }

    private sealed record Harness(object Provider, RecordingSimConnectApiProxy Api);

    private sealed class EventCounter<T>
    {
        public int Count { get; private set; }
        public void OnChanged(T value) => Count++;
    }
}

public class RecordingSimConnectApiProxy : DispatchProxy
{
    public List<RecordedCall> Calls { get; } = new();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod?.Name ?? string.Empty;
        Calls.Add(new RecordedCall(name, args?.ToArray() ?? Array.Empty<object?>()));

        return name switch
        {
            "AddToDataDefinition" or "RequestDataOnSimObject" or "SubscribeToSystemEvent"
                or "MapClientDataNameToId" or "AddToClientDataDefinition" or "RequestClientData" or "SetClientData" => 0,
            "Open" => Open(args!),
            "Close" => 0,
            "GetNextDispatch" => EmptyDispatch(targetMethod!, args!),
            _ => throw new MissingMethodException(name)
        };
    }

    private static int Open(object?[] args)
    {
        args[2] = new nint(77);
        return 0;
    }

    private static int EmptyDispatch(MethodInfo method, object?[] args)
    {
        var messageType = method.GetParameters()[1].ParameterType.GetElementType()!;
        args[1] = Activator.CreateInstance(messageType);
        return 1;
    }
}

public sealed record RecordedCall(string Name, object?[] Arguments);
