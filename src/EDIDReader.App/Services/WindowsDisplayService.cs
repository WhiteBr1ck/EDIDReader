using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using EDIDReader.App.Models;
using Microsoft.Win32;

namespace EDIDReader.App.Services;

public static partial class WindowsDisplayService
{
    private const uint QueryActivePaths = 0x00000002;
    private const uint InvalidModeIndex = 0xFFFFFFFF;
    private const int ErrorSuccess = 0;

    public static IReadOnlyList<MonitorProfile> ReadActiveMonitors()
    {
        var status = GetDisplayConfigBufferSizes(QueryActivePaths, out var pathCount, out var modeCount);
        if (status != ErrorSuccess)
        {
            throw new Win32Exception(status, "Windows 无法返回活动显示路径数量。 ");
        }

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        status = QueryDisplayConfig(QueryActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (status != ErrorSuccess)
        {
            throw new Win32Exception(status, "Windows 无法读取活动显示路径。 ");
        }

        var profiles = new List<MonitorProfile>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            if (!path.TargetInfo.TargetAvailable)
            {
                continue;
            }

            var targetName = GetTargetName(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            var devicePath = targetName.MonitorDevicePath?.Trim() ?? string.Empty;
            if (!seenPaths.Add(string.IsNullOrWhiteSpace(devicePath) ? $"{path.TargetInfo.AdapterId.HighPart}:{path.TargetInfo.AdapterId.LowPart}:{path.TargetInfo.Id}" : devicePath))
            {
                continue;
            }

            var sourceName = GetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id);
            var rawEdid = ReadEdidFromRegistry(devicePath);
            ParsedEdid? parsed = rawEdid.Length >= 128 ? EdidParser.Parse(rawEdid) : null;
            var advancedColor = GetAdvancedColor(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            var sourceMode = GetSourceMode(path.SourceInfo.ModeInfoIndex, modes, modeCount);
            var targetMode = GetTargetMode(path.TargetInfo.ModeInfoIndex, modes, modeCount);

            profiles.Add(BuildProfile(index, path, targetName, sourceName, sourceMode, targetMode, advancedColor, parsed, rawEdid));
        }

        return profiles;
    }

    public static MonitorProfile CreateOfflineProfile(
        byte[] rawEdid,
        string libraryId,
        string libraryKind,
        string sourcePath,
        string fallbackName,
        int colorIndex,
        DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(rawEdid);
        var parsed = EdidParser.Parse(rawEdid);
        var preferred = parsed.PreferredTiming;
        var preferredMode = preferred?.Mode;
        var eotfs = parsed.HdrEotfs.ToArray();
        var hdrSupported = eotfs.Any(value => value.Contains("PQ", StringComparison.Ordinal) || value.Contains("HLG", StringComparison.Ordinal));
        var connection = OfflineConnectionName(parsed);
        var interfaceCapabilities = new List<string> { $"EDID 输入定义：{parsed.InputDefinition}" };
        interfaceCapabilities.AddRange(parsed.InterfaceCapabilities);
        var colors = (colorIndex % 4) switch
        {
            0 => ("#168CCB", "#E8F5FB"),
            1 => ("#E47724", "#FFF1E4"),
            2 => ("#7954D8", "#F0EBFC"),
            _ => ("#1C9B50", "#E9F7EC")
        };
        var displayName = !string.IsNullOrWhiteSpace(fallbackName)
            ? fallbackName
            : string.IsNullOrWhiteSpace(parsed.DisplayName)
                ? $"{parsed.ManufacturerCode} {parsed.ProductCode}"
                : parsed.DisplayName;

        return new MonitorProfile
        {
            IsActive = false,
            LibraryId = libraryId,
            LibraryKind = libraryKind,
            LibraryPath = sourcePath,
            LibrarySavedAt = savedAt,
            DevicePath = sourcePath,
            SourceName = "离线 EDID",
            ConnectorInstance = "离线文件",
            Name = displayName,
            ManufacturerCode = parsed.ManufacturerCode,
            ProductCode = parsed.ProductCode,
            SerialNumber = parsed.SerialNumber,
            Manufactured = parsed.Manufactured,
            PhysicalSize = parsed.PhysicalSize,
            EdidVersion = parsed.Version,
            EdidInputDefinition = parsed.InputDefinition,
            DeclaredBitDepth = parsed.DeclaredBitDepth,
            Gamma = parsed.Gamma,
            VerticalFrequencyRange = parsed.VerticalFrequencyRange,
            EdidStatusText = parsed.StatusText,
            EdidBlockCountText = $"{parsed.Blocks.Count} 个数据块",
            ExtensionSummary = parsed.ExtensionNames.Count > 0 ? string.Join("，", parsed.ExtensionNames) : "无扩展块",
            DeclaredExtensionCount = parsed.DeclaredExtensionCount,
            UnknownExtensionCount = parsed.UnknownExtensionCount,
            Resolution = preferredMode is not null ? preferredMode.Resolution : "EDID 未声明",
            RefreshRate = preferredMode is not null ? preferredMode.RefreshRate : "EDID 未声明",
            CurrentPixelClock = preferredMode?.PixelClock ?? "EDID 未声明",
            Connection = connection,
            ConnectionShort = libraryKind,
            ColorDepth = parsed.DeclaredBitDepth,
            ColorSpace = parsed.ColorFormats.Count > 0 ? string.Join(" / ", parsed.ColorFormats) : "EDID 未声明",
            MaximumResolution = FormatMaximumResolution(parsed.VideoModes),
            MaximumRefreshRate = FormatMaximumRefreshRate(parsed.VideoModes),
            MaximumColorDepth = FormatMaximumBitDepth(parsed.SupportedBitDepths),
            HdrListText = hdrSupported ? "HDR 支持" : "SDR",
            ColorStateLabel = "EDID HDR 能力",
            HdrStateText = hdrSupported ? "HDR 支持" : "SDR",
            HdrOutputTitle = "离线 EDID，不包含 Windows 当前输出状态",
            HdrWindowsStateText = "离线数据",
            AcmStateText = "离线数据",
            Eotf = eotfs.Length > 0 ? string.Join(" / ", eotfs) : "EDID 未声明",
            PeakLuminance = FormatLuminance(parsed.MaximumLuminanceNits),
            AverageLuminance = FormatLuminance(parsed.MaximumFrameAverageLuminanceNits),
            MinimumLuminance = FormatLuminance(parsed.MinimumLuminanceNits, true),
            MetadataType = parsed.HdrMetadataTypes.Count > 0 ? string.Join("，", parsed.HdrMetadataTypes) : "未声明",
            SrgbCoverage = parsed.SrgbCoverage,
            P3Coverage = parsed.P3Coverage,
            Bt2020Coverage = parsed.Bt2020Coverage,
            SrgbVolume = parsed.SrgbVolume,
            P3Volume = parsed.P3Volume,
            Bt2020Volume = parsed.Bt2020Volume,
            RedX = parsed.RedX,
            RedY = parsed.RedY,
            GreenX = parsed.GreenX,
            GreenY = parsed.GreenY,
            BlueX = parsed.BlueX,
            BlueY = parsed.BlueY,
            WhiteX = parsed.WhiteX,
            WhiteY = parsed.WhiteY,
            HasChromaticity = parsed.HasChromaticity,
            AudioChannels = parsed.MaximumAudioChannels > 0 ? $"{parsed.MaximumAudioChannels} 声道" : "未声明",
            AudioSampleRate = parsed.MaximumAudioSampleRateKHz > 0 ? $"{parsed.MaximumAudioSampleRateKHz:g} kHz" : "未声明",
            AudioBitDepth = parsed.LpcmBitDepths.Count > 0 ? string.Join(" · ", parsed.LpcmBitDepths.Order()) + " bit" : "未声明",
            VideoModeCount = parsed.VideoModes.Count.ToString(),
            MaximumDeclaredPixelClock = parsed.MaximumDeclaredPixelClockMHz > 0 ? $"{parsed.MaximumDeclaredPixelClockMHz:0.###} MHz" : "未声明",
            PreferredHorizontalActive = preferred is not null ? preferred.HorizontalActive.ToString() : "未声明",
            PreferredVerticalActive = preferred is not null ? preferred.VerticalActive.ToString() : "未声明",
            PreferredHorizontalBlanking = preferred is not null ? preferred.HorizontalBlanking.ToString() : "未声明",
            PreferredVerticalBlanking = preferred is not null ? preferred.VerticalBlanking.ToString() : "未声明",
            PreferredSyncPolarity = preferred?.SyncPolarity ?? "未声明",
            MaximumTmdsClock = parsed.MaximumTmdsClockMHz > 0 ? $"{parsed.MaximumTmdsClockMHz} MHz" : "未声明",
            SupportedBitDepths = FormatBitDepths(parsed.SupportedBitDepths),
            Ycbcr420BitDepths = FormatBitDepths(parsed.Ycbcr420BitDepths),
            MaximumFrlRate = parsed.MaximumFrlGbps > 0 ? $"{parsed.MaximumFrlGbps} Gbps" : "未声明",
            FrlLaneConfiguration = parsed.FrlLaneCount > 0
                ? $"{parsed.MaximumFrlLaneRateGbps} Gbps × {parsed.FrlLaneCount} lanes"
                : "未声明",
            AllmSupported = parsed.SupportsAllm,
            AllmStateText = parsed.SupportsAllm ? "支持" : "未声明",
            VrrSupported = parsed.VrrTechnologies.Count > 0,
            VrrStateText = parsed.VrrTechnologies.Count > 0 ? "支持" : "未声明",
            VrrRangeText = parsed.VrrMinimumHz is > 0 && parsed.VrrMaximumHz >= parsed.VrrMinimumHz
                ? $"{parsed.VrrMinimumHz} 至 {parsed.VrrMaximumHz} Hz"
                : "未声明",
            VrrTechnologyText = parsed.VrrTechnologies.Count > 0 ? string.Join(" / ", parsed.VrrTechnologies) : "未声明",
            RawHexDump = FormatHex(rawEdid),
            RawBytes = rawEdid.ToArray(),
            AccentColor = colors.Item1,
            AccentSoftColor = colors.Item2,
            Capabilities = BuildOfflineCapabilities(parsed, libraryKind),
            ColorFormats = parsed.ColorFormats.ToArray(),
            Colorimetry = parsed.Colorimetry.ToArray(),
            HdrEotfs = eotfs,
            InterfaceCapabilities = interfaceCapabilities.Distinct().ToArray(),
            VideoModes = parsed.VideoModes,
            AudioFormats = parsed.AudioFormats,
            SpeakerLayouts = parsed.SpeakerLayouts,
            EdidBlocks = parsed.Blocks,
            ParsedDataBlocks = parsed.CtaDataBlocks.Concat(parsed.DisplayIdDataBlocks).Order().ToArray()
        };
    }

    private static MonitorProfile BuildProfile(
        int index,
        DisplayConfigPathInfo path,
        DisplayConfigTargetDeviceName targetName,
        string sourceName,
        DisplayConfigSourceMode? sourceMode,
        DisplayConfigTargetMode? targetMode,
        AdvancedColorSnapshot advancedColor,
        ParsedEdid? parsed,
        byte[] rawEdid)
    {
        var connection = ConnectionName(path.TargetInfo.OutputTechnology);
        var connectionShort = ConnectionShortName(path.TargetInfo.OutputTechnology);
        var width = sourceMode is not null ? (int)sourceMode.Value.Width : parsed?.PreferredTiming?.Mode.Width ?? 0;
        var height = sourceMode is not null ? (int)sourceMode.Value.Height : parsed?.PreferredTiming?.Mode.Height ?? 0;
        var refresh = RationalValue(path.TargetInfo.RefreshRate);
        if (refresh <= 0 && targetMode is not null)
        {
            refresh = RationalValue(targetMode.Value.TargetVideoSignalInfo.VSyncFreq);
        }

        var friendlyName = targetName.MonitorFriendlyDeviceName?.Trim();
        var monitorName = !string.IsNullOrWhiteSpace(friendlyName)
            ? friendlyName
            : parsed?.DisplayName ?? "未知显示器";
        var eotfs = parsed?.HdrEotfs.ToArray() ?? [];
        var edidHdrSupported = eotfs.Any(value => value.Contains("PQ", StringComparison.Ordinal) || value.Contains("HLG", StringComparison.Ordinal));
        var hdrActive = advancedColor.HasModeInfo && advancedColor.ActiveMode == AdvancedColorMode.Hdr;
        var acmActive = advancedColor.HasModeInfo && advancedColor.ActiveMode == AdvancedColorMode.Wcg;
        var hdrSupported = advancedColor.HasModeInfo ? advancedColor.HdrSupported : edidHdrSupported;
        var hdrState = advancedColor.HasModeInfo
            ? hdrActive ? "已启用" : advancedColor.HdrUserEnabled ? "用户已开启" : advancedColor.HdrSupported ? "可用" : "不可用"
            : "系统未提供";
        var acmState = advancedColor.HasModeInfo
            ? acmActive ? "已启用" : advancedColor.WideColorUserEnabled ? "用户已开启" : advancedColor.WideColorSupported ? "可用" : "不可用"
            : "系统未提供";
        var currentColorMode = advancedColor.HasModeInfo
            ? advancedColor.ActiveMode switch
            {
                AdvancedColorMode.Hdr => "当前输出：HDR",
                AdvancedColorMode.Wcg => "当前输出：SDR + ACM",
                _ => "当前输出：SDR"
            }
            : advancedColor.Enabled ? "Windows 高级颜色已启用，模式未区分" : "当前输出：SDR";
        var colorDepth = advancedColor.Available && advancedColor.BitsPerColorChannel > 0
            ? $"{advancedColor.BitsPerColorChannel} bpc"
            : "Windows 未提供";
        var colorSpace = advancedColor.Available
            ? ColorEncodingName(advancedColor.ColorEncoding)
            : "Windows 未提供";

        var capabilities = BuildCapabilities(parsed, connection, advancedColor);
        var interfaceCapabilities = new List<string>();
        if (parsed is not null)
        {
            interfaceCapabilities.Add($"EDID 输入定义：{parsed.InputDefinition}");
            interfaceCapabilities.AddRange(parsed.InterfaceCapabilities);
        }
        interfaceCapabilities.Add($"Windows 输出技术：{connection}");
        if (advancedColor.Available)
        {
            interfaceCapabilities.Add(advancedColor.Supported ? "Windows 高级颜色可用" : "Windows 高级颜色不可用");
        }

        var colors = (index % 4) switch
        {
            0 => ("#168CCB", "#E8F5FB"),
            1 => ("#E47724", "#FFF1E4"),
            2 => ("#7954D8", "#F0EBFC"),
            _ => ("#1C9B50", "#E9F7EC")
        };
        var preferred = parsed?.PreferredTiming;
        var extensions = parsed?.ExtensionNames.Count > 0
            ? string.Join("，", parsed.ExtensionNames)
            : "无扩展块";
        var currentPixelClock = targetMode?.TargetVideoSignalInfo.PixelRate > 0
            ? $"{targetMode.Value.TargetVideoSignalInfo.PixelRate / 1_000_000d:0.###} MHz"
            : "Windows 未提供";
        var rawDump = FormatHex(rawEdid);

        return new MonitorProfile
        {
            DevicePath = targetName.MonitorDevicePath?.Trim() ?? string.Empty,
            SourceName = string.IsNullOrWhiteSpace(sourceName) ? "未提供" : sourceName,
            ConnectorInstance = targetName.ConnectorInstance.ToString(),
            Name = monitorName,
            ManufacturerCode = parsed?.ManufacturerCode ?? ManufacturerCodeFromEdidId(targetName.EdidManufactureId),
            ProductCode = parsed?.ProductCode ?? $"0x{targetName.EdidProductCodeId:X4}",
            SerialNumber = parsed?.SerialNumber ?? "未读取",
            Manufactured = parsed?.Manufactured ?? "未读取",
            PhysicalSize = parsed?.PhysicalSize ?? "未读取",
            EdidVersion = parsed?.Version ?? "未读取",
            EdidInputDefinition = parsed?.InputDefinition ?? "未读取",
            DeclaredBitDepth = parsed?.DeclaredBitDepth ?? "未读取",
            Gamma = parsed?.Gamma ?? "未读取",
            VerticalFrequencyRange = parsed?.VerticalFrequencyRange ?? "未读取",
            EdidStatusText = parsed?.StatusText ?? "活动显示路径存在，但注册表中未找到原始 EDID",
            EdidBlockCountText = parsed is not null ? $"{parsed.Blocks.Count} 个数据块" : "0 个数据块",
            ExtensionSummary = extensions,
            DeclaredExtensionCount = parsed?.DeclaredExtensionCount ?? 0,
            UnknownExtensionCount = parsed?.UnknownExtensionCount ?? 0,
            Resolution = width > 0 && height > 0 ? $"{width} × {height}" : "Windows 未提供",
            RefreshRate = refresh > 0 ? $"{refresh:0.##} Hz" : "Windows 未提供",
            CurrentPixelClock = currentPixelClock,
            Connection = connection,
            ConnectionShort = connectionShort,
            ColorDepth = colorDepth,
            ColorSpace = colorSpace,
            MaximumResolution = parsed is not null ? FormatMaximumResolution(parsed.VideoModes) : width > 0 && height > 0 ? $"{width} × {height}" : "未声明",
            MaximumRefreshRate = parsed is not null ? FormatMaximumRefreshRate(parsed.VideoModes) : refresh > 0 ? $"{refresh:0.##} Hz" : "未声明",
            MaximumColorDepth = parsed is not null ? FormatMaximumBitDepth(parsed.SupportedBitDepths) : colorDepth,
            HdrListText = hdrActive ? "HDR 输出" : acmActive ? "SDR + ACM" : hdrSupported ? "HDR 支持" : "SDR",
            HdrStateText = advancedColor.HasModeInfo ? (hdrActive ? "HDR" : acmActive ? "SDR + ACM" : "SDR") : advancedColor.Enabled ? "高级颜色" : "SDR",
            HdrOutputTitle = currentColorMode,
            HdrWindowsStateText = hdrState,
            AcmStateText = acmState,
            Eotf = eotfs.Length > 0 ? string.Join(" / ", eotfs) : "EDID 未声明",
            PeakLuminance = FormatLuminance(parsed?.MaximumLuminanceNits),
            AverageLuminance = FormatLuminance(parsed?.MaximumFrameAverageLuminanceNits),
            MinimumLuminance = FormatLuminance(parsed?.MinimumLuminanceNits, true),
            MetadataType = parsed?.HdrMetadataTypes.Count > 0 ? string.Join("，", parsed.HdrMetadataTypes) : "未声明",
            SrgbCoverage = parsed?.SrgbCoverage ?? 0,
            P3Coverage = parsed?.P3Coverage ?? 0,
            Bt2020Coverage = parsed?.Bt2020Coverage ?? 0,
            SrgbVolume = parsed?.SrgbVolume ?? 0,
            P3Volume = parsed?.P3Volume ?? 0,
            Bt2020Volume = parsed?.Bt2020Volume ?? 0,
            RedX = parsed?.RedX ?? 0,
            RedY = parsed?.RedY ?? 0,
            GreenX = parsed?.GreenX ?? 0,
            GreenY = parsed?.GreenY ?? 0,
            BlueX = parsed?.BlueX ?? 0,
            BlueY = parsed?.BlueY ?? 0,
            WhiteX = parsed?.WhiteX ?? 0,
            WhiteY = parsed?.WhiteY ?? 0,
            HasChromaticity = parsed?.HasChromaticity == true,
            AudioChannels = parsed?.MaximumAudioChannels > 0 ? $"{parsed.MaximumAudioChannels} 声道" : "未声明",
            AudioSampleRate = parsed?.MaximumAudioSampleRateKHz > 0 ? $"{parsed.MaximumAudioSampleRateKHz:g} kHz" : "未声明",
            AudioBitDepth = parsed?.LpcmBitDepths.Count > 0 ? string.Join(" · ", parsed.LpcmBitDepths.Order()) + " bit" : "未声明",
            VideoModeCount = parsed?.VideoModes.Count.ToString() ?? "0",
            MaximumDeclaredPixelClock = parsed?.MaximumDeclaredPixelClockMHz > 0 ? $"{parsed.MaximumDeclaredPixelClockMHz:0.###} MHz" : "未声明",
            PreferredHorizontalActive = preferred is not null ? preferred.HorizontalActive.ToString() : "未声明",
            PreferredVerticalActive = preferred is not null ? preferred.VerticalActive.ToString() : "未声明",
            PreferredHorizontalBlanking = preferred is not null ? preferred.HorizontalBlanking.ToString() : "未声明",
            PreferredVerticalBlanking = preferred is not null ? preferred.VerticalBlanking.ToString() : "未声明",
            PreferredSyncPolarity = preferred?.SyncPolarity ?? "未声明",
            MaximumTmdsClock = parsed?.MaximumTmdsClockMHz > 0 ? $"{parsed.MaximumTmdsClockMHz} MHz" : "未声明",
            SupportedBitDepths = parsed is not null ? FormatBitDepths(parsed.SupportedBitDepths) : "未声明",
            Ycbcr420BitDepths = parsed is not null ? FormatBitDepths(parsed.Ycbcr420BitDepths) : "未声明",
            MaximumFrlRate = parsed?.MaximumFrlGbps > 0 ? $"{parsed.MaximumFrlGbps} Gbps" : "未声明",
            FrlLaneConfiguration = parsed?.FrlLaneCount > 0
                ? $"{parsed.MaximumFrlLaneRateGbps} Gbps × {parsed.FrlLaneCount} lanes"
                : "未声明",
            AllmSupported = parsed?.SupportsAllm == true,
            AllmStateText = parsed?.SupportsAllm == true ? "支持" : "未声明",
            VrrSupported = parsed?.VrrTechnologies.Count > 0,
            VrrStateText = parsed?.VrrTechnologies.Count > 0 ? "支持" : "未声明",
            VrrRangeText = parsed?.VrrMinimumHz is > 0 && parsed.VrrMaximumHz >= parsed.VrrMinimumHz
                ? $"{parsed.VrrMinimumHz} 至 {parsed.VrrMaximumHz} Hz"
                : "未声明",
            VrrTechnologyText = parsed?.VrrTechnologies.Count > 0
                ? string.Join(" / ", parsed.VrrTechnologies)
                : "未声明",
            RawHexDump = rawDump,
            RawBytes = rawEdid,
            AccentColor = colors.Item1,
            AccentSoftColor = colors.Item2,
            Capabilities = capabilities,
            ColorFormats = parsed?.ColorFormats.ToArray() ?? [],
            Colorimetry = parsed?.Colorimetry.ToArray() ?? [],
            HdrEotfs = eotfs,
            InterfaceCapabilities = interfaceCapabilities.Distinct().ToArray(),
            VideoModes = parsed?.VideoModes ?? [],
            AudioFormats = parsed?.AudioFormats ?? [],
            SpeakerLayouts = parsed?.SpeakerLayouts ?? [],
            EdidBlocks = parsed?.Blocks ?? [],
            ParsedDataBlocks = parsed is null
                ? []
                : parsed.CtaDataBlocks.Concat(parsed.DisplayIdDataBlocks).Order().ToArray()
        };
    }

    private static string FormatBitDepths(IEnumerable<int> bitDepths)
    {
        var values = bitDepths.Distinct().Order().ToArray();
        return values.Length > 0 ? $"{string.Join(" / ", values)} bpc" : "未声明";
    }

    private static string FormatMaximumResolution(IEnumerable<VideoModeInfo> modes)
    {
        var maximum = modes
            .Where(mode => mode.Width > 0 && mode.Height > 0)
            .OrderByDescending(mode => (long)mode.Width * mode.Height)
            .ThenByDescending(mode => mode.Width)
            .ThenByDescending(mode => mode.Height)
            .FirstOrDefault();
        return maximum is null ? "未声明" : maximum.Resolution;
    }

    private static string FormatMaximumRefreshRate(IEnumerable<VideoModeInfo> modes)
    {
        var maximum = modes.Select(mode => mode.RefreshHz).Where(value => value > 0).DefaultIfEmpty(0).Max();
        return maximum > 0 ? $"{maximum:0.##} Hz" : "未声明";
    }

    private static string FormatMaximumBitDepth(IEnumerable<int> bitDepths)
    {
        var maximum = bitDepths.DefaultIfEmpty(0).Max();
        return maximum > 0 ? $"{maximum} bpc" : "未声明";
    }

    private static IReadOnlyList<string> BuildCapabilities(ParsedEdid? parsed, string connection, AdvancedColorSnapshot advancedColor)
    {
        var values = new List<string> { $"活动连接：{connection}" };
        if (parsed is null)
        {
            values.Add("未找到原始 EDID");
            return values;
        }

        values.Add(parsed.Version);
        values.Add($"{parsed.VideoModes.Count} 个视频模式");
        if (parsed.HdrEotfs.Count > 0) values.Add($"EOTF：{string.Join(" / ", parsed.HdrEotfs)}");
        if (parsed.ColorFormats.Count > 0) values.Add(string.Join(" / ", parsed.ColorFormats));
        if (parsed.AudioFormats.Count > 0) values.Add($"{parsed.AudioFormats.Count} 种音频描述符");
        if (parsed.BasicAudio) values.Add("CTA 基础音频");
        if (parsed.VrrTechnologies.Count > 0) values.Add($"VRR：{string.Join(" / ", parsed.VrrTechnologies)}");
        if (parsed.HasCtaExtension) values.Add($"CTA 861 修订版 {parsed.CtaRevision}");
        if (parsed.HasDisplayIdExtension) values.Add(string.IsNullOrWhiteSpace(parsed.DisplayIdVersion) ? "DisplayID" : parsed.DisplayIdVersion);
        if (advancedColor.Available) values.Add(advancedColor.Enabled ? "Windows 高级颜色已启用" : "Windows 高级颜色未启用");
        return values;
    }

    private static IReadOnlyList<string> BuildOfflineCapabilities(ParsedEdid parsed, string libraryKind)
    {
        var values = new List<string> { $"来源：{libraryKind}", parsed.Version, $"{parsed.VideoModes.Count} 个视频模式" };
        if (parsed.HdrEotfs.Count > 0) values.Add($"EOTF：{string.Join(" / ", parsed.HdrEotfs)}");
        if (parsed.ColorFormats.Count > 0) values.Add(string.Join(" / ", parsed.ColorFormats));
        if (parsed.AudioFormats.Count > 0) values.Add($"{parsed.AudioFormats.Count} 种音频描述符");
        if (parsed.BasicAudio) values.Add("CTA 基础音频");
        if (parsed.VrrTechnologies.Count > 0) values.Add($"VRR：{string.Join(" / ", parsed.VrrTechnologies)}");
        if (parsed.HasCtaExtension) values.Add($"CTA 861 修订版 {parsed.CtaRevision}");
        if (parsed.HasDisplayIdExtension) values.Add(string.IsNullOrWhiteSpace(parsed.DisplayIdVersion) ? "DisplayID" : parsed.DisplayIdVersion);
        return values;
    }

    private static string OfflineConnectionName(ParsedEdid parsed)
    {
        if (parsed.InputDefinition.Contains("DisplayPort", StringComparison.OrdinalIgnoreCase))
        {
            return "DisplayPort（EDID 声明）";
        }
        if (parsed.InputDefinition.Contains("HDMI", StringComparison.OrdinalIgnoreCase)
            || parsed.InterfaceCapabilities.Any(value => value.Contains("HDMI", StringComparison.OrdinalIgnoreCase)))
        {
            return "HDMI（EDID 声明）";
        }
        if (parsed.InputDefinition.Contains("DVI", StringComparison.OrdinalIgnoreCase))
        {
            return "DVI（EDID 声明）";
        }
        return parsed.InputDefinition;
    }

    private static DisplayConfigTargetDeviceName GetTargetName(Luid adapterId, uint targetId)
    {
        var value = new DisplayConfigTargetDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetTargetName,
                Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId
            },
            MonitorFriendlyDeviceName = string.Empty,
            MonitorDevicePath = string.Empty
        };
        var status = DisplayConfigGetDeviceInfo(ref value);
        return status == ErrorSuccess ? value : default;
    }

    private static string GetSourceName(Luid adapterId, uint sourceId)
    {
        var value = new DisplayConfigSourceDeviceName
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetSourceName,
                Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                AdapterId = adapterId,
                Id = sourceId
            },
            ViewGdiDeviceName = string.Empty
        };
        return DisplayConfigGetDeviceInfo(ref value) == ErrorSuccess ? value.ViewGdiDeviceName?.Trim() ?? string.Empty : string.Empty;
    }

    private static AdvancedColorSnapshot GetAdvancedColor(Luid adapterId, uint targetId)
    {
        var value2 = new DisplayConfigGetAdvancedColorInfo2
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo2,
                Size = (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo2>(),
                AdapterId = adapterId,
                Id = targetId
            }
        };
        var status2 = DisplayConfigGetDeviceInfo(ref value2);
        if (status2 == ErrorSuccess)
        {
            return new AdvancedColorSnapshot(
                true,
                (value2.Value & 0x01) != 0,
                (value2.Value & 0x02) != 0,
                true,
                (value2.Value & 0x10) != 0,
                (value2.Value & 0x20) != 0,
                (value2.Value & 0x40) != 0,
                (value2.Value & 0x80) != 0,
                value2.ActiveColorMode,
                value2.ColorEncoding,
                value2.BitsPerColorChannel);
        }

        var value = new DisplayConfigGetAdvancedColorInfo
        {
            Header = new DisplayConfigDeviceInfoHeader
            {
                Type = DisplayConfigDeviceInfoType.GetAdvancedColorInfo,
                Size = (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
                AdapterId = adapterId,
                Id = targetId
            }
        };
        var status = DisplayConfigGetDeviceInfo(ref value);
        return status == ErrorSuccess
            ? new AdvancedColorSnapshot(true, (value.Value & 0x01) != 0, (value.Value & 0x02) != 0, false, false, false, false, false, AdvancedColorMode.Sdr, value.ColorEncoding, value.BitsPerColorChannel)
            : default;
    }

    private static DisplayConfigSourceMode? GetSourceMode(uint modeIndex, DisplayConfigModeInfo[] modes, uint modeCount)
    {
        if (modeIndex == InvalidModeIndex || modeIndex >= modeCount || modes[modeIndex].InfoType != DisplayConfigModeInfoType.Source)
        {
            return null;
        }
        return modes[modeIndex].ModeInfo.SourceMode;
    }

    private static DisplayConfigTargetMode? GetTargetMode(uint modeIndex, DisplayConfigModeInfo[] modes, uint modeCount)
    {
        if (modeIndex == InvalidModeIndex || modeIndex >= modeCount || modes[modeIndex].InfoType != DisplayConfigModeInfoType.Target)
        {
            return null;
        }
        return modes[modeIndex].ModeInfo.TargetMode;
    }

    private static byte[] ReadEdidFromRegistry(string monitorDevicePath)
    {
        var match = MonitorPathRegex().Match(monitorDevicePath);
        if (!match.Success)
        {
            return [];
        }

        var model = match.Groups["model"].Value;
        var instance = match.Groups["instance"].Value;
        var path = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{model}\{instance}\Device Parameters";
        using var key = Registry.LocalMachine.OpenSubKey(path, false);
        return key?.GetValue("EDID") is byte[] bytes ? bytes : [];
    }

    private static string FormatHex(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "未读取到原始 EDID 字节";
        }
        var builder = new StringBuilder(bytes.Length * 3 + bytes.Length / 16);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(index % 16 == 0 ? Environment.NewLine : ' ');
            }
            builder.Append(bytes[index].ToString("X2"));
        }
        return builder.ToString();
    }

    private static string FormatLuminance(double? value, bool minimum = false)
    {
        if (value is null or <= 0)
        {
            return "未声明";
        }
        return minimum ? $"{value.Value:0.####} nit" : $"{value.Value:0.#} nit";
    }

    private static double RationalValue(DisplayConfigRational rational)
        => rational.Denominator == 0 ? 0 : rational.Numerator / (double)rational.Denominator;

    private static string ManufacturerCodeFromEdidId(ushort value)
    {
        var first = (char)('A' + ((value >> 10) & 0x1F) - 1);
        var second = (char)('A' + ((value >> 5) & 0x1F) - 1);
        var third = (char)('A' + (value & 0x1F) - 1);
        return new string([first, second, third]);
    }

    private static string ConnectionName(DisplayConfigVideoOutputTechnology technology) => technology switch
    {
        DisplayConfigVideoOutputTechnology.Hdmi => "HDMI",
        DisplayConfigVideoOutputTechnology.DisplayPortExternal => "DisplayPort",
        DisplayConfigVideoOutputTechnology.DisplayPortEmbedded => "内置 DisplayPort",
        DisplayConfigVideoOutputTechnology.Dvi => "DVI",
        DisplayConfigVideoOutputTechnology.Lvds => "LVDS",
        DisplayConfigVideoOutputTechnology.DisplayPortUsbTunnel => "DisplayPort USB4 隧道",
        DisplayConfigVideoOutputTechnology.IndirectWired => "间接有线显示",
        DisplayConfigVideoOutputTechnology.IndirectVirtual => "虚拟显示",
        DisplayConfigVideoOutputTechnology.Internal => "内置显示接口",
        DisplayConfigVideoOutputTechnology.Hd15 => "VGA",
        _ => "Windows 未识别接口"
    };

    private static string ConnectionShortName(DisplayConfigVideoOutputTechnology technology) => technology switch
    {
        DisplayConfigVideoOutputTechnology.Hdmi => "HDMI",
        DisplayConfigVideoOutputTechnology.DisplayPortExternal => "DP",
        DisplayConfigVideoOutputTechnology.DisplayPortEmbedded => "eDP",
        DisplayConfigVideoOutputTechnology.DisplayPortUsbTunnel => "USB4 DP",
        DisplayConfigVideoOutputTechnology.Dvi => "DVI",
        DisplayConfigVideoOutputTechnology.Lvds => "LVDS",
        DisplayConfigVideoOutputTechnology.Hd15 => "VGA",
        _ => "显示"
    };

    private static string ColorEncodingName(uint value) => value switch
    {
        0 => "RGB",
        1 => "YCbCr 4:4:4",
        2 => "YCbCr 4:2:2",
        3 => "YCbCr 4:2:0",
        4 => "强度编码",
        _ => $"未知编码 {value}"
    };

    [GeneratedRegex(@"DISPLAY#(?<model>[^#]+)#(?<instance>[^#]+)#", RegexOptions.IgnoreCase)]
    private static partial Regex MonitorPathRegex();

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DisplayConfigPathInfo[] pathInfoArray, ref uint numModeInfoArrayElements, [Out] DisplayConfigModeInfo[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName deviceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName deviceName);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo advancedColorInfo);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo2 advancedColorInfo);

    private readonly record struct AdvancedColorSnapshot(
        bool Available,
        bool Supported,
        bool Enabled,
        bool HasModeInfo,
        bool HdrSupported,
        bool HdrUserEnabled,
        bool WideColorSupported,
        bool WideColorUserEnabled,
        AdvancedColorMode ActiveMode,
        uint ColorEncoding,
        uint BitsPerColorChannel);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    private enum DisplayConfigVideoOutputTechnology : int
    {
        Other = -1,
        Hd15 = 0,
        SVideo = 1,
        CompositeVideo = 2,
        ComponentVideo = 3,
        Dvi = 4,
        Hdmi = 5,
        Lvds = 6,
        Internal = unchecked((int)0x80000000),
        DNetwork = 8,
        Sdi = 9,
        DisplayPortExternal = 10,
        DisplayPortEmbedded = 11,
        UdiExternal = 12,
        UdiEmbedded = 13,
        SdTvDongle = 14,
        Miracast = 15,
        IndirectWired = 16,
        IndirectVirtual = 17,
        DisplayPortUsbTunnel = 18
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    private enum DisplayConfigModeInfoType : uint
    {
        Source = 1,
        Target = 2,
        DesktopImage = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigModeInfo
    {
        public DisplayConfigModeInfoType InfoType;
        public uint Id;
        public Luid AdapterId;
        public DisplayConfigModeInfoUnion ModeInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct DisplayConfigModeInfoUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfig2DRegion
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public PointL Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL
    {
        public int X;
        public int Y;
    }

    private enum DisplayConfigDeviceInfoType : uint
    {
        GetSourceName = 1,
        GetTargetName = 2,
        GetAdvancedColorInfo = 9,
        GetAdvancedColorInfo2 = 15
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
    }

    private enum AdvancedColorMode : uint
    {
        Sdr = 0,
        Wcg = 1,
        Hdr = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public AdvancedColorMode ActiveColorMode;
    }
}
