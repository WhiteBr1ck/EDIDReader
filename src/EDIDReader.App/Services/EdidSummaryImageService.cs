using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EDIDReader.App.Controls;
using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

public static class EdidSummaryImageService
{
    private const int CanvasWidth = 2000;
    private const int CanvasHeight = 1500;
    private const int RenderScale = 2;
    private const double Margin = 64;
    private const double Gap = 24;
    private const double SevenColumnWidth = 1082;
    private const double FiveColumnWidth = 766;
    private const double NineColumnWidth = 1398;
    private const double ThreeColumnWidth = 450;
    private const double MaximumX = 0.8;
    private const double MaximumY = 0.9;

    private static readonly Typeface SemiBoldTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface BoldTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Brush BackgroundBrush = FrozenBrush("#F4F1ED");
    private static readonly Brush CardBrush = FrozenBrush("#FFFFFF");
    private static readonly Brush CardSoftBrush = FrozenBrush("#FBF8F5");
    private static readonly Brush InkBrush = FrozenBrush("#211C19");
    private static readonly Brush MutedBrush = FrozenBrush("#6F6761");
    private static readonly Brush SubtleBrush = FrozenBrush("#938A83");
    private static readonly Brush AccentBrush = FrozenBrush("#C92E3A");
    private static readonly Brush GreenBrush = FrozenBrush("#18864A");
    private static readonly Brush ChartBrush = FrozenBrush("#FCFBF9");
    private static readonly Pen CardPen = FrozenPen("#E2DCD5", 1.2);
    private static readonly Pen ChartPen = FrozenPen("#E7E1DA", 1);

    public static void Export(MonitorProfile monitor, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, CanvasWidth, CanvasHeight));
            DrawHeader(drawing, monitor);

            const double firstRowY = 170;
            const double firstRowHeight = 320;
            const double secondRowY = firstRowY + firstRowHeight + Gap;
            const double secondRowHeight = 650;
            const double thirdRowY = secondRowY + secondRowHeight + Gap;
            const double thirdRowHeight = 258;
            var firstRightX = Margin + SevenColumnWidth + Gap;
            var secondRightX = Margin + FiveColumnWidth + Gap;

            DrawBasicInfoCard(drawing, monitor, new Rect(Margin, firstRowY, SevenColumnWidth, firstRowHeight));
            DrawHdrCard(drawing, monitor, new Rect(firstRightX, firstRowY, FiveColumnWidth, firstRowHeight));
            DrawColorChartCard(drawing, monitor, new Rect(Margin, secondRowY, FiveColumnWidth, secondRowHeight));
            DrawColorMetricsCard(drawing, monitor, new Rect(secondRightX, secondRowY, SevenColumnWidth, secondRowHeight));
            DrawInterfaceCard(drawing, monitor, new Rect(Margin, thirdRowY, NineColumnWidth, thirdRowHeight));
            DrawAudioCard(drawing, monitor, new Rect(Margin + NineColumnWidth + Gap, thirdRowY, ThreeColumnWidth, thirdRowHeight));
        }

        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var bitmap = new RenderTargetBitmap(
            CanvasWidth * RenderScale,
            CanvasHeight * RenderScale,
            96 * RenderScale,
            96 * RenderScale,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? PortableStorageService.ExportsDirectory);
        using var stream = File.Create(outputPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void DrawHeader(DrawingContext drawing, MonitorProfile monitor)
    {
        DrawText(drawing, "EDID READER", Margin, 24, 18, MutedBrush, BoldTypeface, 400);

        var titleBrush = new LinearGradientBrush(
            Color.FromRgb(172, 24, 46),
            Color.FromRgb(238, 86, 55),
            new Point(0, 0.5),
            new Point(1, 0.5));
        titleBrush.Freeze();
        DrawText(drawing, monitor.Name, Margin, 51, 58, titleBrush, BoldTypeface, 1240);

        var identity = $"{T("厂商", "Manufacturer")}  {monitor.ManufacturerCode}    {T("产品代码", "Product code")}  {monitor.ProductCode}    {T("序列号", "Serial number")}  {monitor.SerialNumber}";
        DrawText(drawing, identity, Margin, 120, 19, MutedBrush, SemiBoldTypeface, CanvasWidth - Margin * 2);
    }

    private static void DrawBasicInfoCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardBrush);
        DrawSectionTitle(drawing, T("基础信息", "BASIC INFORMATION"), rect);
        DrawText(drawing, T("最大分辨率", "Maximum resolution"), rect.X + 36, rect.Y + 65, 15, SubtleBrush, BoldTypeface, rect.Width - 72);
        DrawText(drawing, monitor.MaximumResolution, rect.X + 34, rect.Y + 87, 54, InkBrush, BoldTypeface, rect.Width - 68);

        DrawMetric(drawing, T("最大刷新率", "Maximum refresh rate"), monitor.MaximumRefreshRate, rect.X + 36, rect.Y + 196, 210, AccentBrush, 28);
        DrawMetric(drawing, T("最大色深", "Maximum color depth"), monitor.MaximumColorDepth, rect.X + 270, rect.Y + 196, 200, InkBrush, 28);
        DrawText(drawing, T("颜色格式", "Color formats"), rect.X + 504, rect.Y + 196, 14.5, SubtleBrush, BoldTypeface, rect.Width - 538);
        DrawWrappedText(drawing, monitor.SupportedColorFormats, rect.X + 504, rect.Y + 221, 18, InkBrush, BoldTypeface, rect.Width - 538, 2);
    }

    private static void DrawHdrCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardSoftBrush);
        DrawSectionTitle(drawing, "HDR", rect);

        var hdr = monitor.HdrEotfs.Any(value => value.Contains("PQ", StringComparison.Ordinal) || value.Contains("HLG", StringComparison.Ordinal));
        const double leftColumnWidth = 392;
        const double rightColumnWidth = 270;
        var rightColumnX = rect.Right - 32 - rightColumnWidth;
        DrawText(drawing, hdr ? T("HDR 支持", "HDR CAPABLE") : "SDR", rect.X + 32, rect.Y + 53, 40, hdr ? AccentBrush : InkBrush, BoldTypeface, leftColumnWidth);

        DrawMetric(
            drawing,
            T("静态元数据", "Static metadata"),
            monitor.MetadataType,
            rightColumnX,
            rect.Y + 62,
            rightColumnWidth,
            InkBrush,
            18);

        DrawText(drawing, "EOTF", rect.X + 32, rect.Y + 111, 15, SubtleBrush, BoldTypeface, rect.Width - 64);
        DrawWrappedText(drawing, monitor.Eotf, rect.X + 32, rect.Y + 136, 19, InkBrush, SemiBoldTypeface, leftColumnWidth, 2);

        var metricWidth = (rect.Width - 80) / 3;
        DrawMetric(drawing, T("峰值亮度", "Peak luminance"), monitor.PeakLuminance, rect.X + 32, rect.Y + 218, metricWidth, InkBrush, 20);
        DrawMetric(drawing, T("帧平均亮度", "Frame average"), monitor.AverageLuminance, rect.X + 40 + metricWidth, rect.Y + 218, metricWidth, InkBrush, 20);
        DrawMetric(drawing, T("最小亮度", "Minimum luminance"), monitor.MinimumLuminance, rect.X + 48 + metricWidth * 2, rect.Y + 218, metricWidth, InkBrush, 20);
    }

    private static void DrawColorChartCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardBrush);
        DrawSectionTitle(drawing, T("CIE 1931 xy 色度图", "CIE 1931 xy CHROMATICITY"), rect);

        var chartRect = new Rect(rect.X + 28, rect.Y + 72, rect.Width - 56, rect.Height - 100);
        DrawCieChart(drawing, monitor, chartRect);
    }

    private static void DrawColorMetricsCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardSoftBrush);
        DrawSectionTitle(drawing, T("色域数据", "COLOR GAMUT DATA"), rect);

        var x = rect.X + 32;
        var width = rect.Width - 64;
        DrawGamutMetric(drawing, "sRGB", monitor.SrgbCoverage, monitor.SrgbVolume, x, rect.Y + 74, width, "#3E5968");
        DrawGamutMetric(drawing, "Display P3", monitor.P3Coverage, monitor.P3Volume, x, rect.Y + 180, width, "#C54B42");
        DrawGamutMetric(drawing, "BT.2020", monitor.Bt2020Coverage, monitor.Bt2020Volume, x, rect.Y + 286, width, "#236FA1");

        drawing.DrawLine(ChartPen, new Point(x, rect.Y + 394), new Point(rect.Right - 32, rect.Y + 394));
        DrawMetric(drawing, T("白点", "White point"), monitor.WhitePoint, x, rect.Y + 416, width, InkBrush, 22);

        var coordinateWidth = (width - 28) / 3;
        DrawMetric(drawing, T("红色原色", "Red primary"), monitor.RedPrimary, x, rect.Y + 500, coordinateWidth, InkBrush, 19);
        DrawMetric(drawing, T("绿色原色", "Green primary"), monitor.GreenPrimary, x + coordinateWidth + 14, rect.Y + 500, coordinateWidth, InkBrush, 19);
        DrawMetric(drawing, T("蓝色原色", "Blue primary"), monitor.BluePrimary, x + (coordinateWidth + 14) * 2, rect.Y + 500, coordinateWidth, InkBrush, 19);
    }

    private static void DrawInterfaceCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardSoftBrush);
        DrawSectionTitle(drawing, T("接口能力", "INTERFACE CAPABILITIES"), rect);
        DrawText(drawing, monitor.Connection, rect.X + 32, rect.Y + 57, 31, InkBrush, BoldTypeface, 230);

        const double innerGap = 18;
        var metricsX = rect.X + 274;
        var metricsWidth = rect.Right - metricsX - 32;
        var columnWidth = (metricsWidth - innerGap * 3) / 4;
        var x1 = metricsX;
        var x2 = x1 + columnWidth + innerGap;
        var x3 = x2 + columnWidth + innerGap;
        var x4 = x3 + columnWidth + innerGap;

        if (monitor.IsDisplayPortInterface)
        {
            DrawText(drawing, monitor.ShowCurrentDisplayPortLink ? monitor.DisplayLink.Source : "EDID", rect.X + 32, rect.Y + 101, 14, SubtleBrush, BoldTypeface, 230);
            DrawMetric(drawing, T("当前链路", "Current link"), monitor.DisplayLink.CurrentLinkText, x1, rect.Y + 52, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("链路等级", "Link rate"), monitor.DisplayLink.CurrentGeneration, x2, rect.Y + 52, columnWidth, AccentBrush, 19);
            DrawMetric(drawing, T("原始带宽", "Raw bandwidth"), monitor.DisplayLink.RawBandwidthText, x3, rect.Y + 52, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("理论有效速率", "Theoretical payload rate"), monitor.DisplayLink.PayloadBandwidthText, x4, rect.Y + 52, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("最大链路", "Maximum link"), monitor.DisplayLink.MaximumLinkText, x1, rect.Y + 142, columnWidth, InkBrush, 18);
            DrawMetric(drawing, T("总可用通道", "Total available lanes"), monitor.DisplayLink.LaneCapacityText, x2, rect.Y + 142, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("支持位深", "Supported bit depths"), monitor.SupportedBitDepths, x3, rect.Y + 142, columnWidth, InkBrush, 18);
            DrawMetric(drawing, T("VRR 可变刷新范围", "VRR variable refresh range"), monitor.VrrRangeText, x4, rect.Y + 142, columnWidth, monitor.VrrSupported ? GreenBrush : InkBrush, 19);
            return;
        }

        if (monitor.IsHdmiInterface)
        {
            DrawText(drawing, monitor.ShowCurrentHdmiLink ? monitor.HdmiLink.Source : "EDID", rect.X + 32, rect.Y + 101, 14, SubtleBrush, BoldTypeface, 230);
            DrawMetric(drawing, T("当前像素时钟", "Current pixel clock"), monitor.HdmiLink.CurrentPixelClockText, x1, rect.Y + 52, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("估算 TMDS 频率", "Estimated TMDS frequency"), monitor.HdmiLink.EstimatedTmdsFrequencyText, x2, rect.Y + 52, columnWidth, InkBrush, 18);
            DrawMetric(drawing, T("估算 TMDS 带宽", "Estimated TMDS bandwidth"), monitor.HdmiLink.EstimatedTmdsBandwidthText, x3, rect.Y + 52, columnWidth, InkBrush, 18);
            DrawMetric(drawing, T("当前工作模式", "Current mode"), monitor.HdmiLink.CurrentModeText, x4, rect.Y + 52, columnWidth, AccentBrush, 18);
            DrawMetric(drawing, T("最大 TMDS 频率", "Maximum TMDS frequency"), monitor.MaximumTmdsClock, x1, rect.Y + 142, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("最大 FRL 带宽", "Maximum FRL bandwidth"), monitor.MaximumFrlRate, x2, rect.Y + 142, columnWidth, InkBrush, 19);
            DrawMetric(drawing, T("FRL 通道配置", "FRL lane configuration"), monitor.FrlLaneConfiguration, x3, rect.Y + 142, columnWidth, InkBrush, 18);
            DrawMetric(drawing, "ALLM", monitor.AllmStateText, x4, rect.Y + 142, columnWidth, monitor.AllmSupported ? GreenBrush : InkBrush, 19);
            return;
        }

        DrawText(drawing, "EDID", rect.X + 32, rect.Y + 101, 14, SubtleBrush, BoldTypeface, 230);
        DrawMetric(drawing, T("支持位深", "Supported bit depths"), monitor.SupportedBitDepths, x1, rect.Y + 52, columnWidth, InkBrush, 19);
        DrawMetric(drawing, T("YCbCr 4:2:0 位深", "YCbCr 4:2:0 bit depths"), monitor.Ycbcr420BitDepths, x2, rect.Y + 52, columnWidth, InkBrush, 19);
        DrawMetric(drawing, T("垂直频率范围", "Vertical frequency range"), monitor.VerticalFrequencyRange, x3, rect.Y + 52, columnWidth, InkBrush, 19);
        DrawMetric(drawing, T("VRR 可变刷新范围", "VRR variable refresh range"), monitor.VrrRangeText, x4, rect.Y + 52, columnWidth, monitor.VrrSupported ? GreenBrush : InkBrush, 19);
    }

    private static void DrawAudioCard(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        DrawCard(drawing, rect, CardBrush);
        DrawSectionTitle(drawing, T("音频能力", "AUDIO CAPABILITIES"), rect);
        DrawText(drawing, monitor.AudioChannels, rect.X + 32, rect.Y + 59, 35, InkBrush, BoldTypeface, 140);
        DrawText(drawing, T("支持格式", "Supported formats"), rect.X + 194, rect.Y + 62, 14.5, SubtleBrush, BoldTypeface, rect.Width - 226);
        DrawWrappedText(drawing, monitor.SupportedAudioFormats, rect.X + 194, rect.Y + 87, 14.5, InkBrush, SemiBoldTypeface, rect.Width - 226, 3);
        var columnWidth = (rect.Width - 82) / 2;
        DrawMetric(drawing, T("最高采样率", "Maximum sample rate"), monitor.AudioSampleRate, rect.X + 32, rect.Y + 164, columnWidth, InkBrush, 21);
        DrawMetric(drawing, T("最高 LPCM 位深", "Maximum LPCM bit depth"), monitor.AudioBitDepth, rect.X + 50 + columnWidth, rect.Y + 164, columnWidth, InkBrush, 21);
    }

    private static void DrawCieChart(DrawingContext drawing, MonitorProfile monitor, Rect rect)
    {
        drawing.DrawRoundedRectangle(ChartBrush, ChartPen, rect, 22, 22);
        var plotHeight = Math.Min(510, rect.Height - 46);
        var plotWidth = plotHeight * MaximumX / MaximumY;
        var plot = new Rect(rect.X + 70, rect.Y + 24, plotWidth, plotHeight);
        drawing.DrawImage(
            CieChart.CreateColorMapBitmap((int)Math.Round(plot.Width * RenderScale), (int)Math.Round(plot.Height * RenderScale)),
            plot);

        var gridPen = FrozenPen("#33211C19", 1);
        var axisPen = FrozenPen("#77211C19", 1.2);
        for (var x = 0d; x <= MaximumX + 0.0001; x += 0.1)
        {
            var pixel = MapChromaticity(new Point(x, 0), plot).X;
            drawing.DrawLine(x == 0 ? axisPen : gridPen, new Point(pixel, plot.Top), new Point(pixel, plot.Bottom));
            if (Math.Abs(x * 10 % 2) < 0.001)
            {
                DrawText(drawing, x.ToString("0.0", CultureInfo.InvariantCulture), pixel - 14, plot.Bottom + 8, 12, MutedBrush, SemiBoldTypeface, 40);
            }
        }
        for (var y = 0d; y <= MaximumY + 0.0001; y += 0.1)
        {
            var pixel = MapChromaticity(new Point(0, y), plot).Y;
            drawing.DrawLine(y == 0 ? axisPen : gridPen, new Point(plot.Left, pixel), new Point(plot.Right, pixel));
            if (y > 0 && Math.Abs(y * 10 % 2) < 0.001)
            {
                DrawText(drawing, y.ToString("0.0", CultureInfo.InvariantCulture), plot.Left - 38, pixel - 8, 12, MutedBrush, SemiBoldTypeface, 35, TextAlignment.Right);
            }
        }

        DrawChromaticityPath(drawing, plot, CieChart.SpectralLocusPoints, FrozenPen("#8A2D2723", 1.2), null, true);

        var srgbPen = FrozenPen("#626B7075", 1.8, DashStyles.Dash);
        var p3Pen = FrozenPen("#C54B42", 2, DashStyles.Dash);
        var bt2020Pen = FrozenPen("#236FA1", 2.2, DashStyles.Dash);
        var devicePen = FrozenPen("#B5238A4B", 3);
        DrawChromaticityPath(drawing, plot, [new Point(0.640, 0.330), new Point(0.300, 0.600), new Point(0.150, 0.060)], srgbPen, null, true);
        DrawChromaticityPath(drawing, plot, [new Point(0.680, 0.320), new Point(0.265, 0.690), new Point(0.150, 0.060)], p3Pen, null, true);
        DrawChromaticityPath(drawing, plot, [new Point(0.708, 0.292), new Point(0.170, 0.797), new Point(0.131, 0.046)], bt2020Pen, null, true);

        if (monitor.HasChromaticity)
        {
            Point[] device =
            [
                new Point(monitor.RedX, monitor.RedY),
                new Point(monitor.GreenX, monitor.GreenY),
                new Point(monitor.BlueX, monitor.BlueY)
            ];
            DrawChromaticityPath(drawing, plot, device, devicePen, FrozenBrush("#1F238A4B"), true);
            foreach (var primary in device)
            {
                drawing.DrawEllipse(CardBrush, devicePen, MapChromaticity(primary, plot), 5, 5);
            }

            var whitePoint = MapChromaticity(new Point(monitor.WhiteX, monitor.WhiteY), plot);
            drawing.DrawEllipse(CardBrush, FrozenPen("#211C19", 2.2), whitePoint, 6, 6);
            DrawText(drawing, T("白点", "White"), whitePoint.X + 10, whitePoint.Y - 23, 13, InkBrush, BoldTypeface, 100);
        }

        var legendX = plot.Right + 78;
        var legendY = rect.Y + 72;
        DrawLegendItem(drawing, "sRGB", legendX, legendY, srgbPen);
        DrawLegendItem(drawing, "Display P3", legendX, legendY + 36, p3Pen);
        DrawLegendItem(drawing, "BT.2020", legendX, legendY + 72, bt2020Pen);
        DrawLegendItem(drawing, T("设备", "Display"), legendX, legendY + 108, devicePen);
        DrawText(drawing, "y", plot.Left - 31, plot.Top - 19, 13, MutedBrush, SemiBoldTypeface, 20);
        DrawText(drawing, "x", plot.Right + 10, plot.Bottom - 6, 13, MutedBrush, SemiBoldTypeface, 20);
    }

    private static void DrawGamutMetric(DrawingContext drawing, string name, double coverage, double volume, double x, double y, double width, string color)
    {
        var accent = FrozenBrush(color);
        DrawText(drawing, name, x, y, 24, accent, BoldTypeface, width);
        var metricWidth = (width - 24) / 2;
        DrawText(drawing, T("覆盖率", "Coverage"), x, y + 39, 14, SubtleBrush, BoldTypeface, metricWidth);
        DrawText(drawing, FormatPercent(coverage), x, y + 60, 24, InkBrush, BoldTypeface, metricWidth);
        DrawText(drawing, T("容积率", "Volume"), x + metricWidth + 24, y + 39, 14, SubtleBrush, BoldTypeface, metricWidth);
        DrawText(drawing, FormatPercent(volume), x + metricWidth + 24, y + 60, 24, InkBrush, BoldTypeface, metricWidth);
    }

    private static string FormatPercent(double value) => value > 0 ? $"{value:0.0}%" : T("未声明", "Not declared");

    private static void DrawMetric(DrawingContext drawing, string label, string value, double x, double y, double width, Brush valueBrush, double valueSize)
    {
        DrawText(drawing, label, x, y, 14.5, SubtleBrush, BoldTypeface, width);
        DrawText(drawing, value, x, y + 25, valueSize, valueBrush, BoldTypeface, width);
    }

    private static void DrawSectionTitle(DrawingContext drawing, string title, Rect rect)
        => DrawText(drawing, title, rect.X + 32, rect.Y + 27, 17, MutedBrush, BoldTypeface, rect.Width - 64);

    private static void DrawCard(DrawingContext drawing, Rect rect, Brush brush)
        => drawing.DrawRoundedRectangle(brush, CardPen, rect, 28, 28);

    private static void DrawLegendItem(DrawingContext drawing, string text, double x, double y, Pen pen)
    {
        drawing.DrawLine(pen, new Point(x, y + 9), new Point(x + 27, y + 9));
        DrawText(drawing, text, x + 36, y, 14, pen.Brush, BoldTypeface, 100);
    }

    private static void DrawChromaticityPath(DrawingContext drawing, Rect plot, IReadOnlyList<Point> points, Pen pen, Brush? fill, bool close)
    {
        if (points.Count == 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(MapChromaticity(points[0], plot), fill is not null, close);
            context.PolyLineTo(points.Skip(1).Select(point => MapChromaticity(point, plot)).ToArray(), true, true);
        }
        geometry.Freeze();
        drawing.DrawGeometry(fill, pen, geometry);
    }

    private static Point MapChromaticity(Point point, Rect plot)
        => new(plot.Left + point.X / MaximumX * plot.Width, plot.Bottom - point.Y / MaximumY * plot.Height);

    private static void DrawText(DrawingContext drawing, string text, double x, double y, double fontSize, Brush brush, Typeface typeface, double maxWidth, TextAlignment alignment = TextAlignment.Left)
        => drawing.DrawText(CreateText(text, fontSize, brush, typeface, maxWidth, alignment, 1), new Point(x, y));

    private static void DrawWrappedText(DrawingContext drawing, string text, double x, double y, double fontSize, Brush brush, Typeface typeface, double maxWidth, int maxLines)
        => drawing.DrawText(CreateText(text, fontSize, brush, typeface, maxWidth, TextAlignment.Left, maxLines), new Point(x, y));

    private static FormattedText CreateText(string text, double fontSize, Brush brush, Typeface typeface, double maxWidth, TextAlignment alignment, int maxLines)
    {
        var formatted = new FormattedText(
            text ?? string.Empty,
            CultureInfo.GetCultureInfo(LocalizationService.Language == "en-US" ? "en-US" : "zh-CN"),
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            RenderScale)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = Math.Max(1, maxLines),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
            LineHeight = fontSize * 1.32
        };
        return formatted;
    }

    private static Brush FrozenBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(string color, double thickness, DashStyle? dashStyle = null)
    {
        var pen = new Pen(FrozenBrush(color), thickness)
        {
            DashStyle = dashStyle ?? DashStyles.Solid,
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private static string T(string chinese, string english) => LocalizationService.Language == "en-US" ? english : chinese;
}
