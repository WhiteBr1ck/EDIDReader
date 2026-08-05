using System.Globalization;

namespace EDIDReader.App.Models;

public sealed record DisplayLinkInfo
{
    public static DisplayLinkInfo Unavailable { get; } = new();

    public bool Available { get; init; }
    public string Vendor { get; init; } = string.Empty;
    public string Source { get; init; } = "驱动未提供";
    public double? CurrentLinkRateGbps { get; init; }
    public int? CurrentLaneCount { get; init; }
    public double? MaximumLinkRateGbps { get; init; }
    public int? MaximumLaneCount { get; init; }
    public int? TotalAvailableLaneCount { get; init; }
    public string CurrentGeneration { get; init; } = "驱动未提供";
    public string MaximumGeneration { get; init; } = "驱动未提供";

    public string CurrentLinkText => FormatLink(CurrentLinkRateGbps, CurrentLaneCount);
    public string MaximumLinkText => FormatLink(MaximumLinkRateGbps, MaximumLaneCount);
    public string RawBandwidthText => FormatBandwidth(CurrentLinkRateGbps, CurrentLaneCount, false);
    public string PayloadBandwidthText => FormatBandwidth(CurrentLinkRateGbps, CurrentLaneCount, true);
    public string LaneCapacityText => (TotalAvailableLaneCount ?? MaximumLaneCount) is > 0 and var lanes
        ? lanes.ToString(CultureInfo.InvariantCulture)
        : "驱动未提供";

    private static string FormatLink(double? rateGbps, int? laneCount)
        => rateGbps is > 0 && laneCount is > 0
            ? $"{FormatNumber(rateGbps.Value)} Gbps × {laneCount.Value}"
            : "驱动未提供";

    private static string FormatBandwidth(double? rateGbps, int? laneCount, bool payload)
    {
        if (rateGbps is not > 0 || laneCount is not > 0)
        {
            return "驱动未提供";
        }

        var value = rateGbps.Value * laneCount.Value;
        if (payload)
        {
            value *= rateGbps.Value >= 10 ? 128d / 132d : 8d / 10d;
        }
        return $"{FormatNumber(value)} Gbps";
    }

    private static string FormatNumber(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
