using System.Globalization;

namespace RadioPanel.SimConnect;

public static class FrequencyFormatter
{
    private const int MinimumFrequencyHertz = 118_000_000;
    private const int MaximumFrequencyHertz = 136_990_000;
    private const double MaximumFractionalHertzError = 0.01d;

    public static bool TryDecodeBcd16(ushort encoded, out int frequencyHz)
    {
        frequencyHz = 0;
        if (!TryDecodePackedBcd(encoded, 4, out var digits))
            return false;

        var decodedHz = 100_000_000L + digits * 10_000L;
        if (decodedHz is < MinimumFrequencyHertz or > MaximumFrequencyHertz)
            return false;

        frequencyHz = (int)decodedHz;
        return true;
    }

    public static bool TryDecodeBcd32(uint encoded, out int frequencyHz)
    {
        frequencyHz = 0;
        if (!TryDecodePackedBcd(encoded, 8, out var digits))
            return false;

        var decodedHz = digits * 1_000L;
        if (decodedHz is < MinimumFrequencyHertz or > MaximumFrequencyHertz)
            return false;

        frequencyHz = (int)decodedHz;
        return true;
    }

    public static bool TryDecodeHertz(double encoded, out int frequencyHz)
    {
        frequencyHz = 0;
        if (!double.IsFinite(encoded))
            return false;

        var wholeHertz = Math.Round(encoded);
        if (Math.Abs(encoded - wholeHertz) > MaximumFractionalHertzError
            || wholeHertz < MinimumFrequencyHertz
            || wholeHertz > MaximumFrequencyHertz)
            return false;

        var roundedToDisplayResolution = ((long)wholeHertz + 500L) / 1_000L * 1_000L;
        if (roundedToDisplayResolution is < MinimumFrequencyHertz or > MaximumFrequencyHertz)
            return false;

        frequencyHz = (int)roundedToDisplayResolution;
        return true;
    }

    public static string Format(int frequencyHz)
    {
        if (frequencyHz is < MinimumFrequencyHertz or > MaximumFrequencyHertz)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), "Frequency must be within the COM band.");

        var roundedKilohertz = (frequencyHz + 500L) / 1_000L;
        var wholeMegahertz = roundedKilohertz / 1_000L;
        var fractionalKilohertz = roundedKilohertz % 1_000L;
        return wholeMegahertz.ToString(CultureInfo.InvariantCulture)
            + "."
            + fractionalKilohertz.ToString("D3", CultureInfo.InvariantCulture);
    }

    private static bool TryDecodePackedBcd(uint encoded, int digitCount, out uint value)
    {
        value = 0;
        for (var shift = (digitCount - 1) * 4; shift >= 0; shift -= 4)
        {
            var digit = (encoded >> shift) & 0xF;
            if (digit > 9)
            {
                value = 0;
                return false;
            }

            value = value * 10 + digit;
        }

        return true;
    }
}
