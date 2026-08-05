using System.Globalization;

namespace EDIDReader.App.Models;

public sealed record HdmiLinkInfo
{
    public static HdmiLinkInfo Unavailable { get; } = new();

    public bool Available { get; init; }
    public string Source { get; init; } = "DisplayConfig";
    public double? CurrentPixelClockMHz { get; init; }
    public double? EstimatedTmdsFrequencyMHz { get; init; }
    public double? EstimatedTmdsBandwidthGbps { get; init; }
    public string CurrentModeText { get; init; } = "无法判断";

    public string CurrentPixelClockText => Format(CurrentPixelClockMHz, "MHz", 3);
    public string EstimatedTmdsFrequencyText => Format(EstimatedTmdsFrequencyMHz, "MHz", 3);
    public string EstimatedTmdsBandwidthText => Format(EstimatedTmdsBandwidthGbps, "Gbps", 3);

    private static string Format(double? value, string unit, int decimals)
        => value is > 0
            ? $"{value.Value.ToString($"0.{new string('#', decimals)}", CultureInfo.InvariantCulture)} {unit}"
            : "无法计算";
}
