using System.Buffers.Binary;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class AircraftRouterTests
{
    private const uint ReceiveSimObjectData = 8;
    private const uint ReceiveClientData = 16;

    [TestMethod]
    public void ClassifiesAircraftTitlesCaseInsensitively()
    {
        var routerType = RequireType("RadioPanel.Aircraft.AircraftRouter");
        var classify = routerType.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException("Expected AircraftRouter.Classify(AircraftIdentity).");
        var identityType = RequireType("RadioPanel.Domain.AircraftIdentity");

        Assert.AreEqual("PMDG", Classify(classify, identityType, "Boeing 737 PMDG", "B738"));
        Assert.AreEqual("PMDG", Classify(classify, identityType, "pmdg 777", "B77W"));
        Assert.AreEqual("Fenix", Classify(classify, identityType, "FNX A320", "A20N"));
        Assert.AreEqual("Standard", Classify(classify, identityType, "Asobo Cessna 172", "C172"));
    }

    [TestMethod]
    public void FallsBackAndClearsPreviousAircraftValues()
    {
        var router = CreateRouter(out var api, out var pmdg, out var fenix);
        Invoke(router, "Start", new nint(77));
        SendStandardData(router, 118.005, 122.800, 136.975, 136.990);
        Assert.AreEqual(118_005_000, ReadProperty(Current(router), "Com1ActiveHz"));

        SetAircraftIdentity(router, "Fenix A320", "A20N");
        var cleared = Current(router);
        Assert.IsNull(ReadProperty(cleared, "Com1ActiveHz"), "Previous aircraft values must be cleared while the new aircraft loads.");
        Assert.IsTrue((bool)pmdg.GetType().GetProperty("IsAvailable")!.GetValue(pmdg)! == false);

        SendStandardData(router, 118.005, 122.800, 136.975, 136.990);
        var fallback = Current(router);
        Assert.AreEqual(118_005_000, ReadProperty(fallback, "Com1ActiveHz"));
        Assert.AreEqual(true, ReadProperty(fallback, "Com1StandbyUnverified"));
        Assert.AreEqual(true, ReadProperty(fallback, "Com2StandbyUnverified"));

        var specialValues = CreateRadioFrequencies(121_500_000, 122_800_000, 123_450_000, 124_875_000);
        SetFakeProviderState(fenix, true, specialValues);
        Invoke(router, "Refresh");
        Assert.AreEqual(121_500_000, ReadProperty(Current(router), "Com1ActiveHz"));
        Assert.AreEqual(false, ReadProperty(Current(router), "Com1StandbyUnverified"));

        SetAircraftIdentity(router, "PMDG 737-800", "B738");
        var pmdgFallback = Current(router);
        Assert.IsNull(ReadProperty(pmdgFallback, "Com1ActiveHz"), "Changing aircraft must clear values from both prior sources.");
        Assert.AreEqual(false, api.Calls.Any(call => call.Name == "MapClientDataNameToId"));
    }

    [TestMethod]
    public void FenixBridgePublishesOnlyCompleteValidRadioSamples()
    {
        var apiType = RequireType("RadioPanel.SimConnect.ISimConnectApi");
        var providerType = RequireType("RadioPanel.Aircraft.FenixRadioProvider");
        var api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        var provider = providerType.GetConstructor(new[] { apiType })!.Invoke(new object[] { api });
        Invoke(provider, "Start", new nint(77));

        Assert.IsFalse((bool)providerType.GetProperty("IsAvailable")!.GetValue(provider)!);
        Assert.IsTrue(api.Calls.Any(call => call.Name == "MapClientDataNameToId" && Equals(call.Arguments[1], "MobiFlight.Command")));
        Assert.IsTrue(api.Calls.Any(call => call.Name == "MapClientDataNameToId" && Equals(call.Arguments[1], "MobiFlight.Response")));

        var responseRequestId = (uint)providerType.GetField("DefaultResponseRequestId")!.GetRawConstantValue()!;
        var responseDefinitionId = (uint)providerType.GetField("DefaultResponseDefinitionId")!.GetRawConstantValue()!;
        SendClientData(provider, responseRequestId, responseDefinitionId, "MF.Pong");
        SendClientData(provider, responseRequestId, responseDefinitionId, "MF.Clients.Add.RadioPanel.Finished");

        var commands = api.Calls.Where(call => call.Name == "SetClientData").Select(ReadCommand).Where(command => command.StartsWith("MF.SimVars.Add.", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "MF.SimVars.Add.(L:N_PED_RMP1_ACTIVE, Number)",
                "MF.SimVars.Add.(L:N_PED_RMP1_STDBY, Number)",
                "MF.SimVars.Add.(L:N_PED_RMP2_ACTIVE, Number)",
                "MF.SimVars.Add.(L:N_PED_RMP2_STDBY, Number)"
            },
            commands);

        var firstLvarRequestId = (uint)providerType.GetField("FirstLvarRequestId")!.GetRawConstantValue()!;
        SendClientFloat(provider, firstLvarRequestId + 0, 118_005f);
        SendClientFloat(provider, firstLvarRequestId + 1, 122_800f);
        SendClientFloat(provider, firstLvarRequestId + 2, 136_975f);
        Assert.IsFalse((bool)providerType.GetProperty("IsAvailable")!.GetValue(provider)!, "A partial initial sample must remain unavailable.");
        SendClientFloat(provider, firstLvarRequestId + 3, 136_990f);

        Assert.IsTrue((bool)providerType.GetProperty("IsAvailable")!.GetValue(provider)!);
        var current = providerType.GetProperty("Current")!.GetValue(provider)!;
        Assert.AreEqual(118_005_000, ReadProperty(current, "Com1ActiveHz"));
        Assert.AreEqual(122_800_000, ReadProperty(current, "Com1StandbyHz"));
        Assert.AreEqual(136_975_000, ReadProperty(current, "Com2ActiveHz"));
        Assert.AreEqual(136_990_000, ReadProperty(current, "Com2StandbyHz"));
    }

    [TestMethod]
    public void FenixBridgeRejectsMalformedAndOutOfBandSamples()
    {
        var apiType = RequireType("RadioPanel.SimConnect.ISimConnectApi");
        var providerType = RequireType("RadioPanel.Aircraft.FenixRadioProvider");
        var api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        var provider = providerType.GetConstructor(new[] { apiType })!.Invoke(new object[] { api });
        Invoke(provider, "Start", new nint(77));
        var responseRequestId = (uint)providerType.GetField("DefaultResponseRequestId")!.GetRawConstantValue()!;
        var responseDefinitionId = (uint)providerType.GetField("DefaultResponseDefinitionId")!.GetRawConstantValue()!;
        SendClientData(provider, responseRequestId, responseDefinitionId, "MF.Clients.Add.RadioPanel.Finished");

        var firstLvarRequestId = (uint)providerType.GetField("FirstLvarRequestId")!.GetRawConstantValue()!;
        SendClientFloat(provider, firstLvarRequestId + 0, 117_999f);
        SendClientFloat(provider, firstLvarRequestId + 1, 122_800f);
        SendClientFloat(provider, firstLvarRequestId + 2, float.NaN);
        SendClientFloat(provider, firstLvarRequestId + 3, 136_990f);

        Assert.IsFalse((bool)providerType.GetProperty("IsAvailable")!.GetValue(provider)!);
        var current = providerType.GetProperty("Current")!.GetValue(provider)!;
        Assert.IsNull(ReadProperty(current, "Com1ActiveHz"));
        Assert.IsNull(ReadProperty(current, "Com2ActiveHz"));
    }

    [TestMethod]
    public void FenixReuseWithinOneSimConnectSessionClearsAndReaddsLvarsWithoutReregistering()
    {
        var apiType = RequireType("RadioPanel.SimConnect.ISimConnectApi");
        var providerType = RequireType("RadioPanel.Aircraft.FenixRadioProvider");
        var api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        var provider = providerType.GetConstructor(new[] { apiType })!.Invoke(new object[] { api });
        Invoke(provider, "Start", new nint(77));

        var responseRequestId = (uint)providerType.GetField("DefaultResponseRequestId")!.GetRawConstantValue()!;
        var responseDefinitionId = (uint)providerType.GetField("DefaultResponseDefinitionId")!.GetRawConstantValue()!;
        SendClientData(provider, responseRequestId, responseDefinitionId, "MF.Clients.Add.RadioPanel.Finished");
        Invoke(provider, "Start", new nint(77));

        var commands = api.Calls.Where(call => call.Name == "SetClientData").Select(ReadCommand).ToArray();
        Assert.AreEqual(1, commands.Count(command => command == "MF.Clients.Add.RadioPanel"));
        Assert.AreEqual(1, commands.Count(command => command == "MF.SimVars.Clear"));
        Assert.AreEqual(8, commands.Count(command => command.StartsWith("MF.SimVars.Add.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StopClosesEveryAircraftProviderAcrossReconnects()
    {
        var router = CreateRouter(out _, out var pmdg, out var fenix);
        Invoke(router, "Start", new nint(77));
        SetAircraftIdentity(router, "Fenix A320", "A20N");
        SetAircraftIdentity(router, "Asobo C172", "C172");
        var fenixStops = fenix.StoppedCount;
        var pmdgStops = pmdg.StoppedCount;

        Invoke(router, "Stop");

        Assert.AreEqual(fenixStops + 1, fenix.StoppedCount);
        Assert.AreEqual(pmdgStops + 1, pmdg.StoppedCount);
    }

    [TestMethod]
    public void PmdgProviderStaysUnavailableAndExplainsMissingFrequencyLayout()
    {
        var apiType = RequireType("RadioPanel.SimConnect.ISimConnectApi");
        var providerType = RequireType("RadioPanel.Aircraft.PmdgRadioProvider");
        var api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        var provider = providerType.GetConstructor(new[] { apiType })!.Invoke(new object[] { api });

        Invoke(provider, "Start", new nint(77));

        Assert.IsFalse((bool)providerType.GetProperty("IsAvailable")!.GetValue(provider)!);
        Assert.AreEqual(false, providerType.GetField("HasVerifiedSdkLayout")!.GetRawConstantValue());
        var reason = (string)providerType.GetProperty("UnavailabilityReason")!.GetValue(provider)!;
        StringAssert.Contains(reason, "COM");
        StringAssert.Contains(reason, "frequency");
    }

    private static string Classify(MethodInfo method, Type identityType, string title, string model)
        => method.Invoke(null, new[] { Activator.CreateInstance(identityType, title, model)! })!.ToString()!;

    private static object CreateRouter(out RecordingSimConnectApiProxy api, out FakeRadioProviderProxy pmdg, out FakeRadioProviderProxy fenix)
    {
        var assembly = Assembly.Load("RadioPanel");
        var apiType = RequireType("RadioPanel.SimConnect.ISimConnectApi");
        var standardType = RequireType("RadioPanel.SimConnect.StandardRadioProvider");
        var radioProviderType = RequireType("RadioPanel.Aircraft.IRadioProvider");
        var routerType = RequireType("RadioPanel.Aircraft.AircraftRouter");

        api = (RecordingSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(RecordingSimConnectApiProxy));
        var standard = standardType.GetConstructor(new[] { apiType })!.Invoke(new object[] { api });
        pmdg = (FakeRadioProviderProxy)DispatchProxy.Create(radioProviderType, typeof(FakeRadioProviderProxy));
        fenix = (FakeRadioProviderProxy)DispatchProxy.Create(radioProviderType, typeof(FakeRadioProviderProxy));
        return routerType.GetConstructor(new[] { standardType, radioProviderType, radioProviderType })!
            .Invoke(new object[] { standard, pmdg, fenix });
    }

    private static void SetAircraftIdentity(object router, string title, string model)
    {
        var identityType = RequireType("RadioPanel.Domain.AircraftIdentity");
        Invoke(router, "SetAircraftIdentity", Activator.CreateInstance(identityType, title, model)!);
    }

    private static object CreateRadioFrequencies(int com1Active, int com1Standby, int com2Active, int com2Standby)
    {
        var type = RequireType("RadioPanel.Domain.RadioFrequencies");
        return Activator.CreateInstance(type, com1Active, com1Standby, com2Active, com2Standby, false, false)!;
    }

    private static void SetFakeProviderState(FakeRadioProviderProxy provider, bool available, object current)
    {
        provider.Available = available;
        provider.Frequencies = current;
        provider.RaiseChanged();
    }

    private static void SendStandardData(object router, double com1Active, double com1Standby, double com2Active, double com2Standby)
    {
        var payload = new byte[28 + 4 * sizeof(double)];
        WriteDouble(payload, 28, com1Active);
        WriteDouble(payload, 36, com1Standby);
        WriteDouble(payload, 44, com2Active);
        WriteDouble(payload, 52, com2Standby);
        Invoke(router, "OnSimConnectMessage", CreateMessage(ReceiveSimObjectData, 1, 11, payload));
    }

    private static void SendClientData(object provider, uint requestId, uint definitionId, string value)
    {
        var payload = new byte[28 + 1024];
        System.Text.Encoding.ASCII.GetBytes(value, payload.AsSpan(28));
        Invoke(provider, "OnSimConnectMessage", CreateMessage(ReceiveClientData, requestId, definitionId, payload));
    }

    private static void SendClientFloat(object provider, uint requestId, float value)
    {
        var payload = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(28), value);
        Invoke(provider, "OnSimConnectMessage", CreateMessage(ReceiveClientData, requestId, requestId, payload));
    }

    private static object CreateMessage(uint receiveId, uint requestId, uint definitionId, byte[] payload)
    {
        var type = RequireType("RadioPanel.SimConnect.SimConnectMessage");
        return Activator.CreateInstance(type, receiveId, requestId, definitionId, 0u, payload)!;
    }

    private static void WriteDouble(byte[] payload, int offset, double value)
        => BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset, sizeof(long)), BitConverter.DoubleToInt64Bits(value));

    private static object Current(object router) => router.GetType().GetProperty("Current")!.GetValue(router)!;
    private static object? ReadProperty(object value, string name) => value.GetType().GetProperty(name)?.GetValue(value);

    private static string ReadCommand(RecordedCall call)
    {
        var bytes = (byte[])call.Arguments[^1]!;
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;
        return System.Text.Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static Type RequireType(string name)
        => Assembly.Load("RadioPanel").GetType(name, throwOnError: false) ?? throw new AssertFailedException($"Expected production type {name}.");

    private static void Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(method, $"Expected method {methodName}.");
        method.Invoke(target, arguments);
    }
}

public class FakeRadioProviderProxy : DispatchProxy
{
    private Delegate? _valuesChanged;
    public bool Available { get; set; }
    public object? Frequencies { get; set; }
    public int StoppedCount { get; private set; }

    public void RaiseChanged() => _valuesChanged?.DynamicInvoke(Frequencies);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "get_IsAvailable":
                return Available;
            case "get_Current":
                return Frequencies;
            case "add_ValuesChanged":
                _valuesChanged = Delegate.Combine(_valuesChanged, (Delegate)args![0]!);
                return null;
            case "remove_ValuesChanged":
                _valuesChanged = Delegate.Remove(_valuesChanged, (Delegate)args![0]!);
                return null;
            case "Start":
                return null;
            case "Stop":
                StoppedCount++;
                Available = false;
                Frequencies = null;
                return null;
            case "ResetForAircraftChange":
                Available = false;
                Frequencies = null;
                return null;
            case "OnSimConnectMessage":
                return null;
            default:
                throw new MissingMethodException(targetMethod?.Name);
        }
    }
}
