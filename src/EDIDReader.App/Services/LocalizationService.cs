using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using EDIDReader.App.Controls;

namespace EDIDReader.App.Services;

internal static partial class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["概览"] = "Overview", ["色彩"] = "Color", ["接口与信号"] = "Interface and signal", ["时序"] = "Timings", ["音频"] = "Audio", ["原始数据"] = "Raw data", ["设置"] = "Settings",
        ["显示器"] = "Displays", ["显示器列表"] = "Display list", ["已保存显示器列表"] = "Saved display list", ["刷新"] = "Refresh", ["导入"] = "Import", ["保存"] = "Save", ["导出"] = "Export", ["一图流"] = "One sheet", ["活动"] = "Active", ["已保存"] = "Saved", ["更多操作"] = "More actions", ["已保存显示器操作"] = "Saved display actions", ["导出报告"] = "Export report", ["重新扫描当前显示器"] = "Rescan active displays", ["导出当前显示器报告"] = "Export current display report", ["导入 EDID"] = "Import EDID", ["保存 EDID"] = "Save EDID", ["导出 EDID"] = "Export EDID", ["导出一图流"] = "Export one sheet", ["导出源文件"] = "Export source file", ["导出图片"] = "Export image", ["导出选项"] = "Export options", ["导入 EDID 文件并加入显示器库"] = "Import an EDID file into the display library", ["将当前活动显示器保存到本地显示器库"] = "Save the active display to the local library", ["导出可再次完整导入的原始 EDID 文件"] = "Export a raw EDID file that can be fully imported again", ["导出浅色 EDID 信息图片"] = "Export a light EDID summary image", ["导出 Bento 风格 EDID 一图流"] = "Export a Bento style EDID one sheet",
        ["正在读取 Windows 显示路径"] = "Reading Windows display paths", ["正在读取"] = "Reading", ["正在扫描"] = "Scanning",
        ["显示器概览"] = "Display overview", ["当前连接"] = "Current connection", ["当前模式"] = "Current mode", ["Windows 高级颜色"] = "Windows advanced color", ["EDID HDR 能力"] = "EDID HDR capability", ["设备身份"] = "Device identity",
        ["显示器名称"] = "Display name", ["厂商代码"] = "Manufacturer code", ["产品代码"] = "Product code", ["序列号"] = "Serial number", ["生产日期"] = "Manufactured", ["物理尺寸"] = "Physical size",
        ["EDID 完整性"] = "EDID integrity", ["实际读取到的能力"] = "Detected capabilities",
        ["色彩与色域"] = "Color and gamut", ["原色与白点"] = "Primaries and white point", ["红色"] = "Red", ["绿色"] = "Green", ["蓝色"] = "Blue", ["白点"] = "White point",
        ["参考色域"] = "Reference gamuts", ["覆盖率"] = "Coverage", ["容积率"] = "Volume", ["EDID 颜色格式"] = "EDID color formats", ["色度学声明"] = "Colorimetry declarations", ["设备"] = "Display",
        ["HDR 信息"] = "HDR information", ["EDID EOTF"] = "EDID EOTF", ["当前位深"] = "Current bit depth", ["静态元数据"] = "Static metadata", ["当前编码"] = "Current encoding", ["Windows 当前输出模式"] = "Current Windows output mode", ["当前输出：HDR"] = "Current output: HDR", ["当前输出：SDR + ACM"] = "Current output: SDR + ACM", ["当前输出：SDR"] = "Current output: SDR", ["Windows 高级颜色已启用，模式未区分"] = "Windows advanced color is enabled; mode unavailable", ["HDR 输出"] = "HDR output", ["用户已开启"] = "Enabled by user", ["可用"] = "Available", ["不可用"] = "Unavailable", ["系统未提供"] = "Not provided by the system", ["高级颜色"] = "Advanced color",
        ["EDID 声明的 EOTF"] = "EDID declared EOTFs", ["CTA 声明亮度"] = "CTA declared luminance", ["内容最大亮度"] = "Maximum content luminance", ["帧平均最大亮度"] = "Maximum frame average", ["最小亮度"] = "Minimum luminance",
        ["接口与信号"] = "Interface and signal", ["Windows 活动连接路径"] = "Windows active connection path", ["显示源"] = "Display source", ["输出技术"] = "Output technology", ["活动目标"] = "Active target",
        ["分辨率"] = "Resolution", ["刷新率"] = "Refresh rate", ["颜色编码"] = "Color encoding", ["通道位深"] = "Channel bit depth", ["VRR 范围"] = "VRR range", ["VRR 可变刷新范围"] = "VRR variable refresh range", ["VRR 技术"] = "VRR technology",
        ["可验证的接口信息"] = "Verified interface information", ["Windows 输出技术"] = "Windows output technology", ["EDID 输入定义"] = "EDID input definition", ["EDID 声明位深"] = "EDID declared bit depth",
        ["垂直频率范围"] = "Vertical frequency range", ["EDID 垂直扫描范围"] = "EDID vertical scan range", ["连接器实例"] = "Connector instance", ["当前像素时钟"] = "Current pixel clock", ["最大 TMDS 字符率"] = "Maximum TMDS character rate", ["接口支持位深"] = "Supported interface bit depths", ["YCbCr 4:2:0 位深"] = "YCbCr 4:2:0 bit depths", ["最大 FRL 带宽"] = "Maximum FRL bandwidth", ["FRL 通道配置"] = "FRL lane configuration", ["EDID 接口数据块"] = "EDID interface data blocks",
        ["时序与视频模式"] = "Timings and video modes", ["仅看首选"] = "Preferred only", ["显示全部"] = "Show all", ["恢复默认排序"] = "Reset order", ["导出 CSV"] = "Export CSV", ["筛选视频模式"] = "Filter video modes", ["导出视频模式 CSV"] = "Export video modes CSV", ["按分辨率排序"] = "Sort by resolution", ["按刷新率排序"] = "Sort by refresh rate", ["按扫描方式排序"] = "Sort by scan type", ["按像素时钟排序"] = "Sort by pixel clock", ["按来源排序"] = "Sort by source", ["按标记排序"] = "Sort by mark",
        ["EDID 模式数量"] = "EDID mode count", ["EDID 最大像素时钟"] = "EDID maximum pixel clock", ["扫描"] = "Scan", ["像素时钟"] = "Pixel clock", ["来源"] = "Source", ["标记"] = "Mark",
        ["详细时序"] = "Detailed timing", ["水平活动像素"] = "Horizontal active", ["垂直活动像素"] = "Vertical active", ["水平消隐"] = "Horizontal blanking", ["垂直消隐"] = "Vertical blanking",
        ["水平总像素"] = "Horizontal total", ["垂直总像素"] = "Vertical total", ["水平同步偏移"] = "Horizontal sync offset", ["水平同步宽度"] = "Horizontal sync width", ["垂直同步偏移"] = "Vertical sync offset", ["垂直同步宽度"] = "Vertical sync width", ["同步极性"] = "Sync polarity",
        ["该模式没有声明完整消隐与同步参数"] = "This mode does not declare complete blanking and sync parameters",
        ["音频能力"] = "Audio capabilities", ["最大声道数"] = "Maximum channels", ["最高采样率"] = "Maximum sample rate", ["LPCM 位深"] = "LPCM bit depth", ["格式"] = "Format", ["声道"] = "Channels", ["采样率"] = "Sample rate", ["位深"] = "Bit depth", ["位深或码率"] = "Bit depth or bitrate", ["扬声器分配"] = "Speaker allocation",
        ["原始 EDID"] = "Raw EDID", ["原始长度"] = "Raw length", ["数据块"] = "Data blocks", ["完整性"] = "Integrity", ["十六进制字节"] = "Hexadecimal bytes", ["复制字节"] = "Copy bytes", ["保存 bin"] = "Save bin",
        ["数据块清单"] = "Data block list", ["解析摘要"] = "Parse summary", ["版本"] = "Version", ["输入定义"] = "Input definition", ["声明扩展数"] = "Declared extensions", ["扩展块"] = "Extension blocks", ["未知扩展"] = "Unknown extensions",
        ["外观与语言"] = "Appearance and language", ["主题"] = "Theme", ["自动"] = "Automatic", ["浅色"] = "Light", ["深色"] = "Dark", ["自动主题"] = "Automatic theme", ["浅色主题"] = "Light theme", ["深色主题"] = "Dark theme", ["语言"] = "Language", ["中文"] = "Chinese", ["英语"] = "English", ["关于"] = "About", ["版本 0.4.0"] = "Version 0.4.0", ["重命名"] = "Rename", ["删除"] = "Delete", ["删除全部"] = "Delete all", ["删除所有已保存"] = "Delete all saved records", ["删除所有已保存的显示器"] = "Delete all saved displays", ["取消"] = "Cancel", ["确认"] = "Confirm", ["保存于"] = "Saved at",
        ["复制"] = "Copy", ["已复制"] = "Copied", ["复制失败"] = "Copy failed", ["未声明"] = "Not declared", ["未提供"] = "Not available", ["未读取"] = "Not read", ["未解析"] = "Not parsed", ["未知"] = "Unknown", ["支持"] = "Supported", ["已启用"] = "Enabled", ["未启用"] = "Disabled", ["逐行"] = "Progressive", ["隔行"] = "Interlaced", ["首选"] = "Preferred", ["原生"] = "Native", ["离线 EDID"] = "Offline EDID", ["离线文件"] = "Offline file", ["离线数据"] = "Offline data", ["10 bpc 色深"] = "10 bpc color depth", ["12 bpc 色深"] = "12 bpc color depth", ["16 bpc 色深"] = "16 bpc color depth",
        ["CIE 1931 xy 色度图"] = "CIE 1931 xy chromaticity diagram", ["HDR 开启"] = "HDR on", ["HDR 支持"] = "HDR supported", ["Windows 高级颜色已启用"] = "Windows advanced color is enabled", ["Windows 高级颜色未启用"] = "Windows advanced color is disabled", ["Windows 高级颜色可用"] = "Windows advanced color is available", ["Windows 高级颜色不可用"] = "Windows advanced color is unavailable",
        ["传统 SDR"] = "Traditional SDR", ["传统 HDR"] = "Traditional HDR", ["传统 SDR / PQ / ST 2084"] = "Traditional SDR / PQ / ST 2084", ["静态元数据 Type 1"] = "Static metadata type 1",
        ["头标识、长度与全部块校验通过"] = "Header, length, and all block checksums passed", ["校验通过"] = "Checksum passed", ["校验失败"] = "Checksum failed", ["EDID 基础块"] = "EDID base block", ["CTA 861 扩展"] = "CTA 861 extension", ["DisplayID 扩展"] = "DisplayID extension", ["CTA 861 扩展，DisplayID 扩展"] = "CTA 861 extension, DisplayID extension",
        ["基础 DTD"] = "Base DTD", ["基础标准时序"] = "Base standard timing", ["基础建立时序"] = "Base established timing", ["见原始 DTD 标志"] = "See raw DTD flags",
        ["音频数据块"] = "Audio data block", ["视频数据块"] = "Video data block", ["扬声器分配数据块"] = "Speaker allocation data block", ["色度学数据块"] = "Colorimetry data block", ["HDR 静态元数据块"] = "HDR static metadata data block", ["AMD FreeSync 厂商数据块"] = "AMD FreeSync vendor data block", ["HDMI 厂商数据块"] = "HDMI vendor data block", ["HDMI Forum 厂商数据块"] = "HDMI Forum vendor data block", ["HDMI Forum Sink Capability 数据块"] = "HDMI Forum sink capability data block", ["YCbCr 4:2:0 视频数据块"] = "YCbCr 4:2:0 video data block", ["YCbCr 4:2:0 能力映射"] = "YCbCr 4:2:0 capability map", ["CTA 基础音频"] = "CTA basic audio",
        ["前置左右 FL / FR"] = "Front left / right FL / FR", ["低频 LFE1"] = "Low frequency LFE1", ["前置中置 FC"] = "Front center FC", ["后置左右 BL / BR"] = "Back left / right BL / BR", ["后置中置 BC"] = "Back center BC", ["前置左右中置 FLC / FRC"] = "Front left / right center FLC / FRC", ["后置左右中置 RLC / RRC"] = "Rear left / right center RLC / RRC", ["前置宽声道 FLW / FRW"] = "Front wide FLW / FRW",
        ["未检测到显示器"] = "No display detected", ["未检测到活动显示器"] = "No active display detected", ["请连接显示器后点击刷新。"] = "Connect a display, then select Refresh.", ["读取失败"] = "Read failed", ["Windows 未提供"] = "Not provided by Windows", ["未知显示器"] = "Unknown display", ["无扩展块"] = "No extension blocks", ["EDID 未声明"] = "Not declared by EDID", ["活动显示路径存在，但注册表中未找到原始 EDID"] = "An active display path exists, but no raw EDID was found in the registry", ["未找到原始 EDID"] = "Raw EDID not found", ["未读取到原始 EDID 字节"] = "No raw EDID bytes were read"
    };

    private static readonly IReadOnlyDictionary<string, string> Chinese = English.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string Language { get; set; } = "zh-CN";

    public static string Translate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var dictionary = Language == "en-US" ? English : Chinese;
        if (dictionary.TryGetValue(value, out var translated))
        {
            return translated;
        }

        if (Language == "en-US")
        {
            var match = DisplayCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} active display(s)";
            }

            match = BareDisplayCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} active display(s)";
            }

            match = ManufacturedWeekPattern().Match(value);
            if (match.Success)
            {
                return $"Week {match.Groups["week"].Value}, {match.Groups["year"].Value}";
            }

            match = DataBlockCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} data block(s)";
            }

            match = BlockIndexPattern().Match(value);
            if (match.Success)
            {
                return $"Block {match.Groups["index"].Value}";
            }

            match = VideoModeCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} video mode(s)";
            }

            match = AudioDescriptorCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} audio descriptor(s)";
            }

            match = ChannelCountPattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["count"].Value} channels";
            }

            match = FrequencyRangePattern().Match(value);
            if (match.Success)
            {
                return $"{match.Groups["minimum"].Value} to {match.Groups["maximum"].Value} Hz";
            }

            match = CtaRevisionPattern().Match(value);
            if (match.Success)
            {
                return $"CTA 861 revision {match.Groups["revision"].Value}";
            }

            match = DisplayIdDataBlockPattern().Match(value);
            if (match.Success)
            {
                return $"DisplayID data block {match.Groups["tag"].Value}, revision {match.Groups["revision"].Value}";
            }

            match = SyncPolarityPattern().Match(value);
            if (match.Success)
            {
                var horizontal = match.Groups["horizontal"].Value == "正" ? "positive" : "negative";
                var vertical = match.Groups["vertical"].Value == "正" ? "positive" : "negative";
                return $"Horizontal {horizontal}, vertical {vertical}";
            }

            if (value.StartsWith("首选，", StringComparison.Ordinal))
            {
                return "Preferred, " + value[3..];
            }

            if (value.StartsWith("HSync ", StringComparison.Ordinal))
            {
                return value.Replace("，", ", ", StringComparison.Ordinal);
            }

            foreach (var (prefix, englishPrefix) in EnglishPrefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return englishPrefix + TranslateEnglishSegments(value[prefix.Length..]);
                }
            }
        }

        return value;
    }

    private static string TranslateEnglishSegments(string value)
        => string.Join(" / ", value.Split(" / ", StringSplitOptions.None).Select(segment => English.TryGetValue(segment, out var translated) ? translated : segment));

    private static readonly (string Chinese, string English)[] EnglishPrefixes =
    [
        ("活动连接：", "Active connection: "),
        ("EDID 输入定义：", "EDID input definition: "),
        ("Windows 输出技术：", "Windows output technology: "),
        ("EOTF：", "EOTF: "),
        ("VRR：", "VRR: ")
    ];

    public static void ApplyToTree(DependencyObject root)
    {
        ApplyElement(root);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            ApplyToTree(VisualTreeHelper.GetChild(root, index));
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        if (element is SelectableTextBlock selectableText)
        {
            selectableText.SetCurrentValue(TextBox.TextProperty, Translate(selectableText.Text));
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.SetCurrentValue(TextBlock.TextProperty, Translate(textBlock.Text));
        }
        else if (element is ContentControl contentControl && contentControl.Content is string content)
        {
            contentControl.SetCurrentValue(ContentControl.ContentProperty, Translate(content));
        }

        if (element is HeaderedContentControl headered && headered.Header is string header)
        {
            headered.SetCurrentValue(HeaderedContentControl.HeaderProperty, Translate(header));
        }

        if (ToolTipService.GetToolTip(element) is string toolTip)
        {
            ToolTipService.SetToolTip(element, Translate(toolTip));
        }

        var automationName = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            AutomationProperties.SetName(element, Translate(automationName));
        }
    }

    [GeneratedRegex(@"(?:已读取|重新读取)\s*(?<count>\d+)\s*台(?:活动)?显示器")]
    private static partial Regex DisplayCountPattern();

    [GeneratedRegex(@"^(?<count>\d+)\s*台活动显示器$")]
    private static partial Regex BareDisplayCountPattern();

    [GeneratedRegex(@"^(?<year>\d+)\s*年第\s*(?<week>\d+)\s*周$")]
    private static partial Regex ManufacturedWeekPattern();

    [GeneratedRegex(@"^(?<count>\d+)\s*个数据块$")]
    private static partial Regex DataBlockCountPattern();

    [GeneratedRegex(@"^块\s*(?<index>\d+)$")]
    private static partial Regex BlockIndexPattern();

    [GeneratedRegex(@"^(?<count>\d+)\s*个视频模式$")]
    private static partial Regex VideoModeCountPattern();

    [GeneratedRegex(@"^(?<count>\d+)\s*[个种]音频描述符$")]
    private static partial Regex AudioDescriptorCountPattern();

    [GeneratedRegex(@"^(?<count>\d+)\s*声道$")]
    private static partial Regex ChannelCountPattern();

    [GeneratedRegex(@"^(?<minimum>[0-9.]+)\s*至\s*(?<maximum>[0-9.]+)\s*Hz$")]
    private static partial Regex FrequencyRangePattern();

    [GeneratedRegex(@"^CTA 861 修订版\s*(?<revision>\d+)$")]
    private static partial Regex CtaRevisionPattern();

    [GeneratedRegex(@"^DisplayID 数据块\s*(?<tag>0x[0-9A-Fa-f]+)，修订版\s*(?<revision>\d+)$")]
    private static partial Regex DisplayIdDataBlockPattern();

    [GeneratedRegex(@"^水平(?<horizontal>[正负])，垂直(?<vertical>[正负])$")]
    private static partial Regex SyncPolarityPattern();
}
