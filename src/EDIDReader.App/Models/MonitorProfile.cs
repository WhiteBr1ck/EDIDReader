namespace EDIDReader.App.Models;

public sealed record MonitorProfile
{
    public bool IsActive { get; init; } = true;
    public string LibraryId { get; init; } = string.Empty;
    public string LibraryKind { get; init; } = "活动";
    public string LibraryPath { get; init; } = string.Empty;
    public DateTimeOffset? LibrarySavedAt { get; init; }
    public string DevicePath { get; init; } = string.Empty;
    public string SourceName { get; init; } = "未提供";
    public string ConnectorInstance { get; init; } = "未提供";
    public string Name { get; init; } = "未知显示器";
    public string ManufacturerCode { get; init; } = "未声明";
    public string ProductCode { get; init; } = "未声明";
    public string SerialNumber { get; init; } = "未声明";
    public string Manufactured { get; init; } = "未声明";
    public string PhysicalSize { get; init; } = "未声明";
    public string EdidVersion { get; init; } = "未知";
    public string EdidInputDefinition { get; init; } = "未声明";
    public string DeclaredBitDepth { get; init; } = "未声明";
    public string Gamma { get; init; } = "未声明";
    public string VerticalFrequencyRange { get; init; } = "未声明";
    public string EdidStatusText { get; init; } = "未读取";
    public string EdidBlockCountText { get; init; } = "0 个数据块";
    public string ExtensionSummary { get; init; } = "无扩展块";
    public int DeclaredExtensionCount { get; init; }
    public int UnknownExtensionCount { get; init; }

    public string Resolution { get; init; } = "未提供";
    public string RefreshRate { get; init; } = "未提供";
    public string CurrentPixelClock { get; init; } = "未提供";
    public string Connection { get; init; } = "未知";
    public string ConnectionShort { get; init; } = "未知";
    public string ColorDepth { get; init; } = "未提供";
    public string ColorSpace { get; init; } = "未提供";
    public string MaximumResolution { get; init; } = "未声明";
    public string MaximumRefreshRate { get; init; } = "未声明";
    public string MaximumColorDepth { get; init; } = "未声明";
    public bool IsDisplayPortInterface { get; init; }
    public bool IsHdmiInterface { get; init; }
    public bool ShowCurrentDisplayPortLink { get; init; }
    public bool ShowCurrentHdmiLink { get; init; }
    public DisplayLinkInfo DisplayLink { get; init; } = DisplayLinkInfo.Unavailable;
    public HdmiLinkInfo HdmiLink { get; init; } = HdmiLinkInfo.Unavailable;

    public string HdrListText { get; init; } = "SDR";
    public string ColorStateLabel { get; init; } = "Windows 高级颜色";
    public string HdrStateText { get; init; } = "未启用";
    public string HdrOutputTitle { get; init; } = "Windows 高级颜色未启用";
    public string HdrWindowsStateText { get; init; } = "系统未提供";
    public string AcmStateText { get; init; } = "系统未提供";
    public string Eotf { get; init; } = "未声明";
    public string PeakLuminance { get; init; } = "未声明";
    public string AverageLuminance { get; init; } = "未声明";
    public string MinimumLuminance { get; init; } = "未声明";
    public string MetadataType { get; init; } = "未声明";

    public double SrgbCoverage { get; init; }
    public double P3Coverage { get; init; }
    public double Bt2020Coverage { get; init; }
    public double SrgbVolume { get; init; }
    public double P3Volume { get; init; }
    public double Bt2020Volume { get; init; }
    public double RedX { get; init; }
    public double RedY { get; init; }
    public double GreenX { get; init; }
    public double GreenY { get; init; }
    public double BlueX { get; init; }
    public double BlueY { get; init; }
    public double WhiteX { get; init; }
    public double WhiteY { get; init; }
    public bool HasChromaticity { get; init; }

    public string AudioChannels { get; init; } = "未声明";
    public string AudioSampleRate { get; init; } = "未声明";
    public string AudioBitDepth { get; init; } = "未声明";
    public string VideoModeCount { get; init; } = "0";
    public string MaximumDeclaredPixelClock { get; init; } = "未声明";
    public string PreferredHorizontalActive { get; init; } = "未声明";
    public string PreferredVerticalActive { get; init; } = "未声明";
    public string PreferredHorizontalBlanking { get; init; } = "未声明";
    public string PreferredVerticalBlanking { get; init; } = "未声明";
    public string PreferredSyncPolarity { get; init; } = "未声明";

    public string MaximumTmdsClock { get; init; } = "未声明";
    public string SupportedBitDepths { get; init; } = "未声明";
    public string Ycbcr420BitDepths { get; init; } = "未声明";
    public string MaximumFrlRate { get; init; } = "未声明";
    public string FrlLaneConfiguration { get; init; } = "未声明";
    public bool AllmSupported { get; init; }
    public string AllmStateText { get; init; } = "未声明";
    public bool VrrSupported { get; init; }
    public string VrrStateText { get; init; } = "未声明";
    public string VrrRangeText { get; init; } = "未声明";
    public string VrrTechnologyText { get; init; } = "未声明";
    public string RawHexDump { get; init; } = string.Empty;
    public byte[] RawBytes { get; init; } = [];
    public string RawByteCountText => $"{RawBytes.Length} B";
    public string AccentColor { get; init; } = "#168CCB";
    public string AccentSoftColor { get; init; } = "#E8F5FB";

    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<string> ColorFormats { get; init; } = [];
    public IReadOnlyList<string> Colorimetry { get; init; } = [];
    public IReadOnlyList<string> HdrEotfs { get; init; } = [];
    public IReadOnlyList<string> InterfaceCapabilities { get; init; } = [];
    public IReadOnlyList<VideoModeInfo> VideoModes { get; init; } = [];
    public IReadOnlyList<AudioFormatInfo> AudioFormats { get; init; } = [];
    public IReadOnlyList<string> SpeakerLayouts { get; init; } = [];
    public IReadOnlyList<EdidBlockInfo> EdidBlocks { get; init; } = [];
    public IReadOnlyList<string> ParsedDataBlocks { get; init; } = [];

    public string ModeSummary => $"{Resolution}  ·  {RefreshRate}";
    public string LibrarySavedAtText => LibrarySavedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    public string SignalSummary => $"{RefreshRate}  ·  {ColorDepth}";
    public string SrgbCoverageText => HasChromaticity ? $"{SrgbCoverage:0.0} %" : "未声明";
    public string P3CoverageText => HasChromaticity ? $"{P3Coverage:0.0} %" : "未声明";
    public string Bt2020CoverageText => HasChromaticity ? $"{Bt2020Coverage:0.0} %" : "未声明";
    public string SrgbVolumeText => HasChromaticity ? $"{SrgbVolume:0.0} %" : "未声明";
    public string P3VolumeText => HasChromaticity ? $"{P3Volume:0.0} %" : "未声明";
    public string Bt2020VolumeText => HasChromaticity ? $"{Bt2020Volume:0.0} %" : "未声明";
    public string RedPrimary => HasChromaticity ? $"{RedX:0.0000}   {RedY:0.0000}" : "未声明";
    public string GreenPrimary => HasChromaticity ? $"{GreenX:0.0000}   {GreenY:0.0000}" : "未声明";
    public string BluePrimary => HasChromaticity ? $"{BlueX:0.0000}   {BlueY:0.0000}" : "未声明";
    public string WhitePoint => HasChromaticity ? $"{WhiteX:0.0000}   {WhiteY:0.0000}" : "未声明";
    public string SupportedColorFormats => ColorFormats.Count > 0 ? string.Join("  ·  ", ColorFormats) : "未声明";
    public string SupportedAudioFormats => AudioFormats.Count > 0
        ? string.Join("  ·  ", AudioFormats.Select(format => format.Format).Distinct(StringComparer.OrdinalIgnoreCase))
        : "未声明";

    public override string ToString() => Name;
}
