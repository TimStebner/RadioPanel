using System.Drawing;
using RadioPanel.Domain;
using RadioPanel.Platform;
using RadioPanel.SimConnect;

namespace RadioPanel.UI;

internal sealed unsafe class GdiRenderer : IDisposable
{
    private const int Transparent = 1;
    private const uint FwNormal = 400;
    private const uint FwSemiBold = 600;
    private const uint DefaultCharset = 1;
    private const uint OutDefaultPrecision = 0;
    private const uint ClipDefaultPrecision = 0;
    private const uint DefaultQuality = 0;
    private const uint FixedPitch = 1;
    private const uint ModernFamily = 48;

    private readonly nint _backgroundBrush;
    private readonly nint _activeFont;
    private readonly nint _standbyFont;
    private readonly nint _headerFont;
    private bool _disposed;

    internal GdiRenderer()
    {
        _backgroundBrush = NativeMethods.CreateSolidBrush(ColorRef(18, 23, 31));
        _activeFont = CreateFont(19, FwSemiBold);
        _standbyFont = CreateFont(18, FwNormal);
        _headerFont = CreateFont(13, FwSemiBold);
    }

    internal void Draw(nint hdc, Rectangle clip, RadioFrequencies values, ConnectionStatus status)
    {
        if (_disposed || hdc == nint.Zero || clip.IsEmpty)
            return;

        var nativeClip = new NativeRect(clip.Left, clip.Top, clip.Right, clip.Bottom);
        NativeMethods.FillRect(hdc, in nativeClip, _backgroundBrush);
        NativeMethods.SetBkMode(hdc, Transparent);

        if (clip.IntersectsWith(HeaderRegion))
            DrawText(hdc, _headerFont, ColorRef(139, 154, 173), 16, 10, "RADIO PANEL");
        if (clip.IntersectsWith(DirtyRegionCalculator.StatusLedRegion))
            DrawStatusLed(hdc, status);

        DrawFrequencyRow(hdc, clip, DirtyRegionCalculator.Com1ActiveRegion, _activeFont, ColorRef(255, 176, 0), "COM1 ACTIVE", values.Com1ActiveHz, false);
        DrawFrequencyRow(hdc, clip, DirtyRegionCalculator.Com1StandbyRegion, _standbyFont, ColorRef(0, 229, 255), "COM1 STBY", values.Com1StandbyHz, values.Com1StandbyUnverified);
        DrawFrequencyRow(hdc, clip, DirtyRegionCalculator.Com2ActiveRegion, _activeFont, ColorRef(255, 176, 0), "COM2 ACTIVE", values.Com2ActiveHz, false);
        DrawFrequencyRow(hdc, clip, DirtyRegionCalculator.Com2StandbyRegion, _standbyFont, ColorRef(0, 229, 255), "COM2 STBY", values.Com2StandbyHz, values.Com2StandbyUnverified);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DeleteGdiObject(_backgroundBrush);
        DeleteGdiObject(_activeFont);
        DeleteGdiObject(_standbyFont);
        DeleteGdiObject(_headerFont);
    }

    private static readonly Rectangle HeaderRegion = new(12, 8, 280, 18);

    private void DrawFrequencyRow(nint hdc, Rectangle clip, Rectangle region, nint font, uint color, string label, int? frequencyHz, bool unverified)
    {
        if (!clip.IntersectsWith(region))
            return;

        DrawText(hdc, font, ColorRef(145, 158, 174), 16, region.Top + 3, label);
        var formatted = frequencyHz.HasValue ? FrequencyFormatter.Format(frequencyHz.Value) : "---.---";
        DrawText(hdc, font, color, 166, region.Top + 2, formatted);
        if (unverified)
            DrawText(hdc, font, ColorRef(112, 126, 142), 279, region.Top + 2, "*");
    }

    private static void DrawStatusLed(nint hdc, ConnectionStatus status)
    {
        var color = status switch
        {
            ConnectionStatus.Connected => ColorRef(32, 210, 107),
            ConnectionStatus.Error => ColorRef(240, 75, 83),
            _ => ColorRef(255, 194, 71)
        };
        var brush = NativeMethods.CreateSolidBrush(color);
        if (brush == nint.Zero)
            return;

        var previousBrush = NativeMethods.SelectObject(hdc, brush);
        var previousPen = NativeMethods.SelectObject(hdc, NativeMethods.GetStockObject(NativeMethods.NullPen));
        var region = DirtyRegionCalculator.StatusLedRegion;
        NativeMethods.Ellipse(hdc, region.Left, region.Top, region.Right, region.Bottom);
        NativeMethods.SelectObject(hdc, previousPen);
        NativeMethods.SelectObject(hdc, previousBrush);
        DeleteGdiObject(brush);
    }

    private static void DrawText(nint hdc, nint font, uint color, int x, int y, string text)
    {
        var previousFont = NativeMethods.SelectObject(hdc, font);
        NativeMethods.SetTextColor(hdc, color);
        fixed (char* textPointer = text)
            NativeMethods.TextOut(hdc, x, y, textPointer, text.Length);
        NativeMethods.SelectObject(hdc, previousFont);
    }

    private static nint CreateFont(int height, uint weight)
    {
        fixed (char* faceName = "Consolas\0")
        {
            return NativeMethods.CreateFont(
                -height, 0, 0, 0, weight, 0, 0, 0,
                DefaultCharset, OutDefaultPrecision, ClipDefaultPrecision,
                DefaultQuality, FixedPitch | ModernFamily, faceName);
        }
    }

    private static uint ColorRef(byte red, byte green, byte blue)
        => (uint)(red | (green << 8) | (blue << 16));

    private static void DeleteGdiObject(nint value)
    {
        if (value != nint.Zero)
            NativeMethods.DeleteObject(value);
    }
}
