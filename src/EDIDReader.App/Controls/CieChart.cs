using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EDIDReader.App.Services;

namespace EDIDReader.App.Controls;

public sealed class CieChart : FrameworkElement
{
    private const double MaximumX = 0.8;
    private const double MaximumY = 0.9;
    private static readonly Typeface AxisTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface LabelTypeface = new(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Point[] SpectralLocus = CreateSpectralLocus();

    internal static IReadOnlyList<Point> SpectralLocusPoints => SpectralLocus;

    internal static WriteableBitmap CreateColorMapBitmap(int width, int height) => CreateColorMap(width, height);

    private WriteableBitmap? _colorMap;
    private int _colorMapWidth;
    private int _colorMapHeight;

    public static readonly DependencyProperty HasChromaticityProperty = DependencyProperty.Register(
        nameof(HasChromaticity),
        typeof(bool),
        typeof(CieChart),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RedXProperty = RegisterCoordinate(nameof(RedX), 0.676);
    public static readonly DependencyProperty RedYProperty = RegisterCoordinate(nameof(RedY), 0.322);
    public static readonly DependencyProperty GreenXProperty = RegisterCoordinate(nameof(GreenX), 0.275);
    public static readonly DependencyProperty GreenYProperty = RegisterCoordinate(nameof(GreenY), 0.690);
    public static readonly DependencyProperty BlueXProperty = RegisterCoordinate(nameof(BlueX), 0.148);
    public static readonly DependencyProperty BlueYProperty = RegisterCoordinate(nameof(BlueY), 0.060);
    public static readonly DependencyProperty WhiteXProperty = RegisterCoordinate(nameof(WhiteX), 0.313);
    public static readonly DependencyProperty WhiteYProperty = RegisterCoordinate(nameof(WhiteY), 0.329);

    public bool HasChromaticity { get => (bool)GetValue(HasChromaticityProperty); set => SetValue(HasChromaticityProperty, value); }
    public double RedX { get => (double)GetValue(RedXProperty); set => SetValue(RedXProperty, value); }
    public double RedY { get => (double)GetValue(RedYProperty); set => SetValue(RedYProperty, value); }
    public double GreenX { get => (double)GetValue(GreenXProperty); set => SetValue(GreenXProperty, value); }
    public double GreenY { get => (double)GetValue(GreenYProperty); set => SetValue(GreenYProperty, value); }
    public double BlueX { get => (double)GetValue(BlueXProperty); set => SetValue(BlueXProperty, value); }
    public double BlueY { get => (double)GetValue(BlueYProperty); set => SetValue(BlueYProperty, value); }
    public double WhiteX { get => (double)GetValue(WhiteXProperty); set => SetValue(WhiteXProperty, value); }
    public double WhiteY { get => (double)GetValue(WhiteYProperty); set => SetValue(WhiteYProperty, value); }

    public CieChart()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(FindBrush("ChartBackgroundBrush", Color.FromRgb(250, 248, 245)), null, bounds, 13, 13);

        const double leftInset = 46;
        const double topInset = 28;
        const double rightInset = 18;
        const double bottomInset = 40;
        var availableWidth = Math.Max(100, ActualWidth - leftInset - rightInset);
        var availableHeight = Math.Max(100, ActualHeight - topInset - bottomInset);
        var unitsToPixels = Math.Min(availableWidth / MaximumX, availableHeight / MaximumY);
        var plotWidth = MaximumX * unitsToPixels;
        var plotHeight = MaximumY * unitsToPixels;
        var plot = new Rect(
            leftInset + (availableWidth - plotWidth) / 2d,
            topInset + (availableHeight - plotHeight) / 2d,
            plotWidth,
            plotHeight);
        DrawColorMap(dc, plot);
        DrawGrid(dc, plot);
        DrawSpectralOutline(dc, plot);

        DrawTriangle(
            dc,
            plot,
            [new Point(0.640, 0.330), new Point(0.300, 0.600), new Point(0.150, 0.060)],
            CreatePen(Color.FromRgb(102, 106, 109), 1.35, DashStyles.Dash),
            null);

        DrawTriangle(
            dc,
            plot,
            [new Point(0.680, 0.320), new Point(0.265, 0.690), new Point(0.150, 0.060)],
            CreatePen(Color.FromRgb(121, 84, 216), 1.45, DashStyles.Dash),
            null);

        DrawTriangle(
            dc,
            plot,
            [new Point(0.708, 0.292), new Point(0.170, 0.797), new Point(0.131, 0.046)],
            CreatePen(Color.FromRgb(22, 140, 203), 1.55, DashStyles.Dash),
            null);

        if (HasChromaticity)
        {
            Point[] devicePrimaries =
            [
                new Point(RedX, RedY),
                new Point(GreenX, GreenY),
                new Point(BlueX, BlueY)
            ];
            var deviceColor = Color.FromRgb(22, 139, 72);
            DrawTriangle(dc, plot, devicePrimaries, CreatePen(deviceColor, 2.5), new SolidColorBrush(Color.FromArgb(24, deviceColor.R, deviceColor.G, deviceColor.B)));

            foreach (var primary in devicePrimaries)
            {
                var point = Map(primary, plot);
                dc.DrawEllipse(Brushes.White, CreatePen(deviceColor, 2), point, 4.2, 4.2);
            }

            var whitePoint = Map(new Point(WhiteX, WhiteY), plot);
            var labelColor = ThemeService.IsDark ? Color.FromRgb(243, 239, 233) : Color.FromRgb(38, 36, 33);
            dc.DrawEllipse(ThemeService.IsDark ? Brushes.Black : Brushes.White, CreatePen(labelColor, 1.8), whitePoint, 5.2, 5.2);
            DrawText(dc, LocalizationService.Translate("白点"), new Point(whitePoint.X + 8, whitePoint.Y - 18), 11, new SolidColorBrush(labelColor), LabelTypeface);
        }

        DrawLegend(dc, plot);
        DrawText(dc, "x", new Point(plot.Right + 5, plot.Bottom - 4), 11, FindBrush("MutedBrush", Color.FromRgb(132, 128, 121)), AxisTypeface);
        DrawText(dc, "y", new Point(plot.Left - 22, plot.Top - 21), 11, FindBrush("MutedBrush", Color.FromRgb(132, 128, 121)), AxisTypeface);
    }

    private static DependencyProperty RegisterCoordinate(string name, double defaultValue)
    {
        return DependencyProperty.Register(
            name,
            typeof(double),
            typeof(CieChart),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    private void DrawColorMap(DrawingContext dc, Rect plot)
    {
        var width = Math.Max(1, (int)Math.Ceiling(plot.Width));
        var height = Math.Max(1, (int)Math.Ceiling(plot.Height));
        if (_colorMap is null || _colorMapWidth != width || _colorMapHeight != height)
        {
            _colorMap = CreateColorMap(width, height);
            _colorMapWidth = width;
            _colorMapHeight = height;
        }

        dc.DrawImage(_colorMap, plot);
    }

    private static WriteableBitmap CreateColorMap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];

        for (var row = 0; row < height; row++)
        {
            var y = MaximumY * (1d - (row + 0.5d) / height);
            for (var column = 0; column < width; column++)
            {
                var x = MaximumX * (column + 0.5d) / width;
                if (y <= 0 || !Contains(SpectralLocus, x, y))
                {
                    continue;
                }

                var xyzX = x / y;
                var xyzY = 1d;
                var xyzZ = Math.Max(0d, (1d - x - y) / y);
                var red = 3.2406d * xyzX - 1.5372d * xyzY - 0.4986d * xyzZ;
                var green = -0.9689d * xyzX + 1.8758d * xyzY + 0.0415d * xyzZ;
                var blue = 0.0557d * xyzX - 0.2040d * xyzY + 1.0570d * xyzZ;
                var maximum = Math.Max(red, Math.Max(green, blue));
                if (maximum > 1d)
                {
                    red /= maximum;
                    green /= maximum;
                    blue /= maximum;
                }

                red = ToSrgb(Math.Clamp(red, 0d, 1d));
                green = ToSrgb(Math.Clamp(green, 0d, 1d));
                blue = ToSrgb(Math.Clamp(blue, 0d, 1d));

                var offset = row * stride + column * 4;
                pixels[offset] = SoftChannel(blue);
                pixels[offset + 1] = SoftChannel(green);
                pixels[offset + 2] = SoftChannel(red);
                pixels[offset + 3] = 220;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private void DrawGrid(DrawingContext dc, Rect plot)
    {
        var gridPen = CreatePen(ThemeService.IsDark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(34, 33, 31, 28), 1);
        var axisPen = CreatePen(ThemeService.IsDark ? Color.FromArgb(86, 255, 255, 255) : Color.FromArgb(86, 33, 31, 28), 1);
        var labelBrush = FindBrush("MutedBrush", Color.FromRgb(142, 137, 129));

        for (var x = 0d; x <= MaximumX + 0.0001d; x += 0.1d)
        {
            var pixel = Map(new Point(x, 0), plot).X;
            dc.DrawLine(x == 0 ? axisPen : gridPen, new Point(pixel, plot.Top), new Point(pixel, plot.Bottom));
            DrawText(dc, x.ToString("0.0", CultureInfo.InvariantCulture), new Point(pixel - 10, plot.Bottom + 7), 10, labelBrush, AxisTypeface);
        }

        for (var y = 0d; y <= MaximumY + 0.0001d; y += 0.1d)
        {
            var pixel = Map(new Point(0, y), plot).Y;
            dc.DrawLine(y == 0 ? axisPen : gridPen, new Point(plot.Left, pixel), new Point(plot.Right, pixel));
            if (y > 0)
            {
                DrawText(dc, y.ToString("0.0", CultureInfo.InvariantCulture), new Point(plot.Left - 31, pixel - 7), 10, labelBrush, AxisTypeface);
            }
        }
    }

    private static void DrawSpectralOutline(DrawingContext dc, Rect plot)
    {
        DrawPath(dc, plot, SpectralLocus, CreatePen(ThemeService.IsDark ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(120, 33, 31, 28), 1.15), null, true);
    }

    private void DrawLegend(DrawingContext dc, Rect plot)
    {
        var x = plot.Right - 128;
        var y = plot.Top + 10;
        DrawLegendItem(dc, x, y, "sRGB", CreatePen(Color.FromRgb(102, 106, 109), 1.35, DashStyles.Dash));
        DrawLegendItem(dc, x, y + 19, "Display P3", CreatePen(Color.FromRgb(121, 84, 216), 1.45, DashStyles.Dash));
        DrawLegendItem(dc, x, y + 38, "BT.2020", CreatePen(Color.FromRgb(22, 140, 203), 1.55, DashStyles.Dash));
        if (HasChromaticity)
        {
            DrawLegendItem(dc, x, y + 57, LocalizationService.Translate("设备"), CreatePen(Color.FromRgb(22, 139, 72), 2.5));
        }
    }

    private void DrawLegendItem(DrawingContext dc, double x, double y, string text, Pen pen)
    {
        dc.DrawLine(pen, new Point(x, y + 7), new Point(x + 20, y + 7));
        DrawText(dc, text, new Point(x + 27, y), 10.5, pen.Brush, LabelTypeface);
    }

    private static void DrawTriangle(DrawingContext dc, Rect plot, IReadOnlyList<Point> points, Pen pen, Brush? fill)
    {
        DrawPath(dc, plot, points, pen, fill, true);
    }

    private static void DrawPath(DrawingContext dc, Rect plot, IReadOnlyList<Point> points, Pen pen, Brush? fill, bool close)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Map(points[0], plot), fill is not null, close);
            context.PolyLineTo(points.Skip(1).Select(point => Map(point, plot)).ToArray(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static Point Map(Point point, Rect plot)
    {
        return new Point(
            plot.Left + point.X / MaximumX * plot.Width,
            plot.Bottom - point.Y / MaximumY * plot.Height);
    }

    private void DrawText(DrawingContext dc, string text, Point origin, double size, Brush brush, Typeface typeface)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.GetCultureInfo(LocalizationService.Language),
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, origin);
    }

    private Brush FindBrush(string key, Color fallback)
        => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private static Pen CreatePen(Color color, double thickness, DashStyle? dashStyle = null)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            DashStyle = dashStyle ?? DashStyles.Solid,
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Point[] CreateSpectralLocus()
    {
        // CIE 1931 2 degree standard observer, sampled every 5 nm from 380 to 700 nm.
        // Source: CIE_xyz_1931_2deg.csv published by the International Commission on Illumination.
        return
        [
            new Point(0.174112, 0.004964),
            new Point(0.174008, 0.004981),
            new Point(0.173801, 0.004915),
            new Point(0.173560, 0.004923),
            new Point(0.173337, 0.004797),
            new Point(0.173021, 0.004775),
            new Point(0.172577, 0.004799),
            new Point(0.172087, 0.004833),
            new Point(0.171407, 0.005102),
            new Point(0.170301, 0.005789),
            new Point(0.168878, 0.006900),
            new Point(0.166895, 0.008556),
            new Point(0.164412, 0.010858),
            new Point(0.161105, 0.013793),
            new Point(0.156641, 0.017705),
            new Point(0.150985, 0.022740),
            new Point(0.143960, 0.029703),
            new Point(0.135503, 0.039879),
            new Point(0.124118, 0.057803),
            new Point(0.109594, 0.086843),
            new Point(0.091294, 0.132702),
            new Point(0.068706, 0.200723),
            new Point(0.045391, 0.294976),
            new Point(0.023460, 0.412703),
            new Point(0.008168, 0.538423),
            new Point(0.003859, 0.654823),
            new Point(0.013870, 0.750186),
            new Point(0.038852, 0.812016),
            new Point(0.074302, 0.833803),
            new Point(0.114161, 0.826207),
            new Point(0.154722, 0.805864),
            new Point(0.192876, 0.781629),
            new Point(0.229620, 0.754329),
            new Point(0.265775, 0.724324),
            new Point(0.301604, 0.692308),
            new Point(0.337363, 0.658848),
            new Point(0.373102, 0.624451),
            new Point(0.408736, 0.589607),
            new Point(0.444062, 0.554714),
            new Point(0.478775, 0.520202),
            new Point(0.512486, 0.486591),
            new Point(0.544787, 0.454434),
            new Point(0.575151, 0.424232),
            new Point(0.602933, 0.396497),
            new Point(0.627037, 0.372491),
            new Point(0.648233, 0.351395),
            new Point(0.665764, 0.334011),
            new Point(0.680079, 0.319747),
            new Point(0.691504, 0.308342),
            new Point(0.700606, 0.299301),
            new Point(0.707918, 0.292027),
            new Point(0.714032, 0.285929),
            new Point(0.719033, 0.280935),
            new Point(0.723032, 0.276948),
            new Point(0.725992, 0.274008),
            new Point(0.728272, 0.271728),
            new Point(0.729969, 0.270031),
            new Point(0.731089, 0.268911),
            new Point(0.731993, 0.268007),
            new Point(0.732719, 0.267281),
            new Point(0.733417, 0.266583),
            new Point(0.734047, 0.265953),
            new Point(0.734390, 0.265610),
            new Point(0.734592, 0.265408),
            new Point(0.734690, 0.265310)
        ];
    }

    private static bool Contains(IReadOnlyList<Point> polygon, double x, double y)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            var first = polygon[current];
            var second = polygon[previous];
            if ((first.Y > y) != (second.Y > y)
                && x < (second.X - first.X) * (y - first.Y) / (second.Y - first.Y) + first.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double ToSrgb(double value)
    {
        return value <= 0.0031308d
            ? 12.92d * value
            : 1.055d * Math.Pow(value, 1d / 2.4d) - 0.055d;
    }

    private static byte SoftChannel(double value)
    {
        return (byte)Math.Round(255d * (0.12d + 0.88d * value));
    }
}
