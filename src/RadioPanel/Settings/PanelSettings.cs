namespace RadioPanel.Settings;

public readonly record struct PanelSettings(int X, int Y, bool AlwaysOnTop)
{
    public static PanelSettings SafeDefault => new(40, 40, false);
}
