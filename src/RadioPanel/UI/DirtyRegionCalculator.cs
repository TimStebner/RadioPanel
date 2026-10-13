using System.Drawing;
using RadioPanel.Domain;

namespace RadioPanel.UI;

public static class DirtyRegionCalculator
{
    public static readonly Rectangle Com1ActiveRegion = new(12, 34, 296, 24);
    public static readonly Rectangle Com1StandbyRegion = new(12, 60, 296, 24);
    public static readonly Rectangle Com2ActiveRegion = new(12, 92, 296, 24);
    public static readonly Rectangle Com2StandbyRegion = new(12, 118, 296, 24);
    public static readonly Rectangle StatusLedRegion = new(280, 10, 14, 14);

    public static IReadOnlyList<Rectangle> GetDirtyRegions(
        RadioFrequencies previous,
        RadioFrequencies current,
        ConnectionStatus previousStatus,
        ConnectionStatus currentStatus)
    {
        var regions = new List<Rectangle>(5);
        if (previous.Com1ActiveHz != current.Com1ActiveHz)
            regions.Add(Com1ActiveRegion);
        if (previous.Com1StandbyHz != current.Com1StandbyHz
            || previous.Com1StandbyUnverified != current.Com1StandbyUnverified)
            regions.Add(Com1StandbyRegion);
        if (previous.Com2ActiveHz != current.Com2ActiveHz)
            regions.Add(Com2ActiveRegion);
        if (previous.Com2StandbyHz != current.Com2StandbyHz
            || previous.Com2StandbyUnverified != current.Com2StandbyUnverified)
            regions.Add(Com2StandbyRegion);
        if (previousStatus != currentStatus)
            regions.Add(StatusLedRegion);
        return regions;
    }
}
