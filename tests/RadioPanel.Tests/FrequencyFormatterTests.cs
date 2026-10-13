using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RadioPanel.Tests;

[TestClass]
public sealed class FrequencyFormatterTests
{
    private static Type Formatter => Assembly.Load("RadioPanel").GetType("RadioPanel.SimConnect.FrequencyFormatter", throwOnError: false)
        ?? throw new AssertFailedException("Expected production type RadioPanel.SimConnect.FrequencyFormatter.");

    [TestMethod]
    public void DecodesBcd16AndBcd32ChannelExamples()
    {
        AssertDecoded("TryDecodeBcd16", (ushort)0x1800, 118_000_000);
        AssertDecoded("TryDecodeBcd16", (ushort)0x2280, 122_800_000);
        AssertDecoded("TryDecodeBcd16", (ushort)0x3699, 136_990_000);
        AssertDecoded("TryDecodeBcd32", 0x118005u, 118_005_000);
        AssertDecoded("TryDecodeBcd32", 0x118025u, 118_025_000);
        AssertDecoded("TryDecodeBcd32", 0x122800u, 122_800_000);
        AssertDecoded("TryDecodeBcd32", 0x136990u, 136_990_000);
    }

    [TestMethod]
    public void DecodesIntegerHertzAndNormalizesToDisplayPrecision()
    {
        AssertDecoded("TryDecodeHertz", 118_005_000d, 118_005_000);
        AssertDecoded("TryDecodeHertz", 118_025_000d, 118_025_000);
        AssertDecoded("TryDecodeHertz", 122_800_000d, 122_800_000);
        AssertDecoded("TryDecodeHertz", 136_990_000d, 136_990_000);
        AssertDecoded("TryDecodeHertz", 118_008_333d, 118_008_000);
    }

    [TestMethod]
    public void RejectsMalformedAndOutOfRangeValues()
    {
        AssertRejected("TryDecodeBcd16", (ushort)0x18A0);
        AssertRejected("TryDecodeBcd16", (ushort)0x1799);
        AssertRejected("TryDecodeBcd16", (ushort)0x3700);
        AssertRejected("TryDecodeBcd32", 0x11800Au);
        AssertRejected("TryDecodeBcd32", 0x117999u);
        AssertRejected("TryDecodeBcd32", 0x137000u);
        AssertRejected("TryDecodeHertz", 118_005_000.5d);
        AssertRejected("TryDecodeHertz", double.NaN);
        AssertRejected("TryDecodeHertz", double.PositiveInfinity);
        AssertRejected("TryDecodeHertz", 117_999_000d);
        AssertRejected("TryDecodeHertz", 136_991_000d);
    }

    [TestMethod]
    public void FormatsExactlyThreeInvariantDecimals()
    {
        var format = Formatter.GetMethod("Format", BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException("Expected FrequencyFormatter.Format(int).");

        Assert.AreEqual("118.005", format.Invoke(null, new object[] { 118_005_000 }));
        Assert.AreEqual("122.800", format.Invoke(null, new object[] { 122_800_000 }));
        Assert.AreEqual("136.990", format.Invoke(null, new object[] { 136_990_000 }));
    }

    private static void AssertDecoded(string methodName, object encoded, int expectedHertz)
    {
        var method = Formatter.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException($"Expected FrequencyFormatter.{methodName}.");
        var arguments = new object?[] { encoded, 0 };
        var result = method.Invoke(null, arguments);

        Assert.AreEqual(true, result, $"{methodName} should accept {encoded}.");
        Assert.AreEqual(expectedHertz, arguments[1]);
    }

    private static void AssertRejected(string methodName, object encoded)
    {
        var method = Formatter.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new AssertFailedException($"Expected FrequencyFormatter.{methodName}.");
        var arguments = new object?[] { encoded, 123 };

        Assert.AreEqual(false, method.Invoke(null, arguments), $"{methodName} should reject {encoded}.");
        Assert.AreEqual(0, arguments[1], "A rejected frequency should not leave a plausible output value.");
    }
}
