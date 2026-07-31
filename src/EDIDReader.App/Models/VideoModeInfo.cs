namespace EDIDReader.App.Models;

public sealed record VideoModeInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double RefreshHz { get; init; }
    public bool Interlaced { get; init; }
    public double? PixelClockMHz { get; init; }
    public string Source { get; init; } = "EDID";
    public string Mark { get; init; } = string.Empty;
    public int? HorizontalBlanking { get; init; }
    public int? VerticalBlanking { get; init; }
    public int? HorizontalSyncOffset { get; init; }
    public int? HorizontalSyncWidth { get; init; }
    public int? VerticalSyncOffset { get; init; }
    public int? VerticalSyncWidth { get; init; }
    public string SyncPolarity { get; init; } = "未声明";
    public bool IsExpanded { get; set; }

    public string Resolution => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "未解析";
    public string RefreshRate => RefreshHz > 0 ? $"{RefreshHz:0.##} Hz" : "未声明";
    public string Scan => Interlaced ? "隔行" : "逐行";
    public string PixelClock => PixelClockMHz is > 0 ? $"{PixelClockMHz:0.###} MHz" : "未声明";
    public bool HasDetailedTiming => HorizontalBlanking is > 0 && VerticalBlanking is > 0;
    public string DetailStatus => HasDetailedTiming ? "详细时序" : "该模式没有声明完整消隐与同步参数";
    public string HorizontalActiveText => Width > 0 ? Width.ToString() : "未声明";
    public string VerticalActiveText => Height > 0 ? Height.ToString() : "未声明";
    public string HorizontalBlankingText => Format(HorizontalBlanking);
    public string VerticalBlankingText => Format(VerticalBlanking);
    public string HorizontalTotalText => HorizontalBlanking is > 0 ? (Width + HorizontalBlanking.Value).ToString() : "未声明";
    public string VerticalTotalText => VerticalBlanking is > 0 ? (Height + VerticalBlanking.Value).ToString() : "未声明";
    public string HorizontalSyncOffsetText => Format(HorizontalSyncOffset);
    public string HorizontalSyncWidthText => Format(HorizontalSyncWidth);
    public string VerticalSyncOffsetText => Format(VerticalSyncOffset);
    public string VerticalSyncWidthText => Format(VerticalSyncWidth);

    private static string Format(int? value) => value is > 0 ? value.Value.ToString() : "未声明";
}
