using System.Drawing;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class DirtyRegionCalculatorTests
{
    private static readonly Rectangle Com1Active = new(12, 34, 296, 24);
    private static readonly Rectangle Com1Standby = new(12, 60, 296, 24);
    private static readonly Rectangle Com2Active = new(12, 92, 296, 24);
    private static readonly Rectangle Com2Standby = new(12, 118, 296, 24);
    private static readonly Rectangle StatusLed = new(280, 10, 14, 14);

    [TestMethod]
    public void EqualSnapshotsHaveNoDirtyRegions()
    {
        var snapshot = new RadioValues(118_005_000, 122_800_000, 136_975_000, 136_990_000);

        CollectionAssert.AreEqual(Array.Empty<Rectangle>(), Calculate(snapshot, snapshot, "Connected", "Connected"));
    }

    [TestMethod]
    public void OneFrequencyChangeInvalidatesOnlyItsRow()
    {
        var previous = new RadioValues(118_005_000, 122_800_000, 136_975_000, 136_990_000);
        var current = previous with { Com2StandbyHz = 136_980_000 };

        CollectionAssert.AreEqual(new[] { Com2Standby }, Calculate(previous, current, "Connected", "Connected"));
    }

    [TestMethod]
    public void StandbyVerificationChangeInvalidatesTheStandbyRow()
    {
        var previous = new RadioValues(118_005_000, 122_800_000, 136_975_000, 136_990_000);
        var current = previous with { Com1StandbyUnverified = true };

        CollectionAssert.AreEqual(new[] { Com1Standby }, Calculate(previous, current, "Connected", "Connected"));
    }

    [TestMethod]
    public void StatusOnlyChangeInvalidatesOnlyTheLed()
    {
        var snapshot = new RadioValues(118_005_000, 122_800_000, 136_975_000, 136_990_000);

        CollectionAssert.AreEqual(new[] { StatusLed }, Calculate(snapshot, snapshot, "Waiting", "Connected"));
    }

    private static Rectangle[] Calculate(RadioValues previous, RadioValues current, string previousStatus, string currentStatus)
    {
        var assembly = Assembly.Load("RadioPanel");
        var valueType = assembly.GetType("RadioPanel.Domain.RadioFrequencies", throwOnError: true)!;
        var statusType = assembly.GetType("RadioPanel.Domain.ConnectionStatus", throwOnError: true)!;
        var calculatorType = assembly.GetType("RadioPanel.UI.DirtyRegionCalculator", throwOnError: true)!;
        var method = calculatorType.GetMethod("GetDirtyRegions", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method, "Expected DirtyRegionCalculator.GetDirtyRegions.");

        var previousValue = Activator.CreateInstance(valueType,
            previous.Com1ActiveHz, previous.Com1StandbyHz, previous.Com2ActiveHz, previous.Com2StandbyHz,
            previous.Com1StandbyUnverified, previous.Com2StandbyUnverified)!;
        var currentValue = Activator.CreateInstance(valueType,
            current.Com1ActiveHz, current.Com1StandbyHz, current.Com2ActiveHz, current.Com2StandbyHz,
            current.Com1StandbyUnverified, current.Com2StandbyUnverified)!;
        var previousConnection = Enum.Parse(statusType, previousStatus);
        var currentConnection = Enum.Parse(statusType, currentStatus);
        var result = method.Invoke(null, new[] { previousValue, currentValue, previousConnection, currentConnection })!;
        return ((IEnumerable<Rectangle>)result).ToArray();
    }

    private readonly record struct RadioValues(
        int? Com1ActiveHz,
        int? Com1StandbyHz,
        int? Com2ActiveHz,
        int? Com2StandbyHz,
        bool Com1StandbyUnverified = false,
        bool Com2StandbyUnverified = false);
}
