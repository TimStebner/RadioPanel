using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class ConnectionTests
{
    private const int HResultFail = unchecked((int)0x80004005);
    private const uint ReceiveOpen = 2;
    private const uint ReceiveQuit = 3;

    [TestMethod]
    public void DelayedOpenMessageKeepsOriginalHandleUntilSimulatorResponds()
    {
        var h = CreateHarness(0);

        Invoke(h.Connection, "Start", new nint(42));
        AssertStatus(h.Connection, "Connecting");
        Assert.AreEqual(1, h.Timer.StartCount);
        Assert.AreEqual(3000u, h.Timer.LastInterval);

        // AutoFPS observed a 34-second delay between SimConnect_Open and OnOpen.
        for (var tick = 0; tick < 11; tick++)
            Invoke(h.Connection, "HandleRetryTimer");

        Assert.AreEqual(1, h.Api.OpenCount, "A pending handle must survive beyond the 3-second retry interval.");
        Assert.AreEqual(0, h.Api.CloseCount);

        h.Api.ReceiveIds.Enqueue(ReceiveOpen);
        Invoke(h.Connection, "HandleSimConnectWindowMessage");

        AssertStatus(h.Connection, "Connected");
        Assert.AreEqual(1, h.Api.OpenCount);
        Assert.AreEqual(0, h.Api.CloseCount);
    }

    [TestMethod]
    public void PendingOpenHandleIsRetriedAfterHandshakeTimeout()
    {
        var h = CreateHarness(0, 0);

        Invoke(h.Connection, "Start", new nint(42));
        for (var tick = 0; tick < 19; tick++)
            Invoke(h.Connection, "HandleRetryTimer");

        Assert.AreEqual(1, h.Api.OpenCount, "The initial handle must remain open during the 57-second handshake window.");
        Assert.AreEqual(0, h.Api.CloseCount);

        Invoke(h.Connection, "HandleRetryTimer");

        AssertStatus(h.Connection, "Connecting");
        Assert.AreEqual(2, h.Api.OpenCount);
        Assert.AreEqual(1, h.Api.CloseCount, "An unconfirmed handle is retried after the full handshake timeout.");
    }

    [TestMethod]
    public void OpenMessageTransitionsToConnectedAndStopsRetryTimer()
    {
        var h = CreateHarness(0);
        h.Api.EmptyDispatchResult = HResultFail;
        h.Api.ReceiveIds.Enqueue(ReceiveOpen);

        Invoke(h.Connection, "Start", new nint(42));
        AssertStatus(h.Connection, "Connecting");
        Invoke(h.Connection, "HandleSimConnectWindowMessage");

        AssertStatus(h.Connection, "Connected");
        Assert.AreEqual(1, h.Timer.StopCount);
        Assert.AreEqual(0, h.Api.CloseCount);
    }

    [TestMethod]
    public void QuitClosesHandleAndSchedulesRetry()
    {
        var h = CreateHarness(0);
        h.Api.ReceiveIds.Enqueue(ReceiveOpen);

        Invoke(h.Connection, "Start", new nint(42));
        Invoke(h.Connection, "HandleSimConnectWindowMessage");
        h.Api.ReceiveIds.Enqueue(ReceiveQuit);
        Invoke(h.Connection, "HandleSimConnectWindowMessage");

        AssertStatus(h.Connection, "Waiting");
        Assert.AreEqual(1, h.Api.CloseCount);
        Assert.AreEqual(2, h.Timer.StartCount);
    }

    [TestMethod]
    public void MissingLibraryEntersErrorAndRetries()
    {
        var h = CreateHarness(Array.Empty<int>(), missingLibraryOnFirstOpen: true);

        Invoke(h.Connection, "Start", new nint(42));
        AssertStatus(h.Connection, "Error");
        Assert.AreEqual(1, h.Timer.StartCount);

        Invoke(h.Connection, "HandleRetryTimer");
        AssertStatus(h.Connection, "Connecting");
        Assert.AreEqual(2, h.Api.OpenCount);
    }

    [TestMethod]
    public void FatalDispatchErrorEntersErrorAndRetries()
    {
        var h = CreateHarness(0);
        h.Api.ReceiveIds.Enqueue(ReceiveOpen);

        Invoke(h.Connection, "Start", new nint(42));
        Invoke(h.Connection, "HandleSimConnectWindowMessage");
        AssertStatus(h.Connection, "Connected");

        h.Api.EmptyDispatchResult = HResultFail;
        Invoke(h.Connection, "HandleSimConnectWindowMessage");

        AssertStatus(h.Connection, "Error");
        Assert.AreEqual(1, h.Api.CloseCount);
        Assert.AreEqual(2, h.Timer.StartCount);
    }

    [TestMethod]
    public void StopClosesHandleExactlyOnce()
    {
        var h = CreateHarness(0);
        h.Api.ReceiveIds.Enqueue(ReceiveOpen);

        Invoke(h.Connection, "Start", new nint(42));
        Invoke(h.Connection, "HandleSimConnectWindowMessage");
        Invoke(h.Connection, "Stop");
        Invoke(h.Connection, "Stop");

        Assert.AreEqual(1, h.Api.CloseCount);
    }

    private static Harness CreateHarness(params int[] openResults)
        => CreateHarness(openResults, missingLibraryOnFirstOpen: false);

    private static Harness CreateHarness(int[] openResults, bool missingLibraryOnFirstOpen)
    {
        var assembly = Assembly.Load("RadioPanel");
        var apiType = RequireType(assembly, "RadioPanel.SimConnect.ISimConnectApi");
        var timerType = RequireType(assembly, "RadioPanel.Platform.IReconnectTimer");
        var connectionType = RequireType(assembly, "RadioPanel.SimConnect.SimConnectConnection");
        var constructor = connectionType.GetConstructor(new[] { apiType, timerType });
        Assert.IsNotNull(constructor, "SimConnectConnection should accept API and retry-timer seams.");

        var api = (FakeSimConnectApiProxy)DispatchProxy.Create(apiType, typeof(FakeSimConnectApiProxy));
        foreach (var result in openResults)
            api.OpenResults.Enqueue(result);
        api.ThrowMissingLibraryOnce = missingLibraryOnFirstOpen;

        var timer = (FakeReconnectTimerProxy)DispatchProxy.Create(timerType, typeof(FakeReconnectTimerProxy));
        var connection = constructor.Invoke(new object[] { api, timer });
        return new Harness(connection, api, timer);
    }

    private static Type RequireType(Assembly assembly, string name)
    {
        var type = assembly.GetType(name, throwOnError: false);
        Assert.IsNotNull(type, $"Expected production type {name}.");
        return type!;
    }

    private static void Invoke(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(method, $"Expected method {methodName}.");
        method.Invoke(target, arguments);
    }

    private static void AssertStatus(object connection, string expected)
    {
        var property = connection.GetType().GetProperty("Status", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(property, "The connection should expose its current status.");
        Assert.AreEqual(expected, property.GetValue(connection)?.ToString());
    }

    private sealed record Harness(object Connection, FakeSimConnectApiProxy Api, FakeReconnectTimerProxy Timer);
}

public class FakeSimConnectApiProxy : DispatchProxy
{
    public Queue<int> OpenResults { get; } = new();
    public Queue<uint> ReceiveIds { get; } = new();
    public int OpenCount { get; private set; }
    public int CloseCount { get; private set; }
    public int EmptyDispatchResult { get; set; } = 1; // S_FALSE
    public bool ThrowMissingLibraryOnce { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "Open":
                OpenCount++;
                if (ThrowMissingLibraryOnce)
                {
                    ThrowMissingLibraryOnce = false;
                    throw new DllNotFoundException("SimConnect.dll");
                }

                var hresult = OpenResults.Count == 0 ? 0 : OpenResults.Dequeue();
                args![2] = hresult >= 0 ? new nint(100 + OpenCount) : nint.Zero;
                return hresult;
            case "Close":
                CloseCount++;
                return 0;
            case "GetNextDispatch":
                var messageType = targetMethod.GetParameters()[1].ParameterType.GetElementType()!;
                if (ReceiveIds.Count == 0)
                {
                    args![1] = Activator.CreateInstance(messageType);
                    return EmptyDispatchResult;
                }

                var receiveId = ReceiveIds.Dequeue();
                args![1] = Activator.CreateInstance(messageType, receiveId, 0u, 0u, 0u, Array.Empty<byte>());
                return 0; // S_OK
            default:
                throw new MissingMethodException(targetMethod?.Name);
        }
    }
}

public class FakeReconnectTimerProxy : DispatchProxy
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public uint LastInterval { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case "Start":
                StartCount++;
                LastInterval = (uint)args![2]!;
                return null;
            case "Stop":
                StopCount++;
                return null;
            default:
                throw new MissingMethodException(targetMethod?.Name);
        }
    }
}
