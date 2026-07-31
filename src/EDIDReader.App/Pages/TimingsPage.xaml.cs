using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EDIDReader.App.Controls;
using EDIDReader.App.Models;
using EDIDReader.App.Services;
using Microsoft.Win32;

namespace EDIDReader.App.Pages;

public partial class TimingsPage : SectionedPage
{
    private IReadOnlyList<VideoModeInfo> _sourceModes = [];
    private bool _preferredOnly;
    private string? _sortKey;
    private bool _sortDescending;

    public TimingsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.NewValue is MonitorProfile monitor)
            {
                _sourceModes = monitor.VideoModes.ToArray();
                ApplyModes();
            }
        };
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor)
        {
            return;
        }
        _preferredOnly = !_preferredOnly;
        ApplyModes();
        FilterLabel.Text = LocalizationService.Translate(_preferredOnly ? "显示全部" : "仅看首选");
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor || sender is not Button { Tag: string key })
        {
            return;
        }

        if (_sortKey == key)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortKey = key;
            _sortDescending = false;
        }

        ApplyModes();
        UpdateSortIndicators();
    }

    private void ResetSort_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor)
        {
            return;
        }

        _sortKey = null;
        _sortDescending = false;
        ApplyModes();
        UpdateSortIndicators();
    }

    private void Timing_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            Dispatcher.BeginInvoke(() => LocalizationService.ApplyToTree(expander), DispatcherPriority.Loaded);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Language == "en-US" ? "Export EDID video modes" : "导出 EDID 视频模式",
            InitialDirectory = AppPreferences.Current.DialogDirectory,
            FileName = $"{SanitizeFileName(monitor.Name)} EDID 视频模式.csv",
            DefaultExt = ".csv",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var headers = new[] { "分辨率", "刷新率", "扫描", "像素时钟", "来源", "标记" }
            .Select(LocalizationService.Translate);
        var csv = new StringBuilder(string.Join(',', headers) + "\r\n");
        foreach (var mode in CurrentModes())
        {
            var row = new[] { mode.Resolution, mode.RefreshRate, mode.Scan, mode.PixelClock, mode.Source, mode.Mark };
            csv.AppendLine(string.Join(',', row.Select(EscapeCsv)));
        }
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        AppPreferences.Current.RememberDialogFile(dialog.FileName);
    }

    private void ApplyModes()
    {
        ModeList.ItemsSource = CurrentModes();
    }

    private IReadOnlyList<VideoModeInfo> CurrentModes()
    {
        var modes = _sourceModes.Select((mode, index) => new IndexedMode(mode, index));
        if (_preferredOnly)
        {
            var preferred = modes.Where(item => item.Mode.Mark is "首选" or "原生").ToArray();
            modes = preferred.Length > 0 ? preferred : modes.Take(1);
        }

        if (_sortKey is not null)
        {
            modes = SortModes(modes, _sortKey, _sortDescending);
        }

        return modes.Select(item => item.Mode).ToArray();
    }

    private static IEnumerable<IndexedMode> SortModes(IEnumerable<IndexedMode> modes, string key, bool descending)
    {
        return (key, descending) switch
        {
            ("Resolution", false) => modes.OrderBy(item => (long)item.Mode.Width * item.Mode.Height).ThenBy(item => item.Mode.Width).ThenBy(item => item.Index),
            ("Resolution", true) => modes.OrderByDescending(item => (long)item.Mode.Width * item.Mode.Height).ThenByDescending(item => item.Mode.Width).ThenBy(item => item.Index),
            ("Refresh", false) => modes.OrderBy(item => item.Mode.RefreshHz).ThenBy(item => item.Index),
            ("Refresh", true) => modes.OrderByDescending(item => item.Mode.RefreshHz).ThenBy(item => item.Index),
            ("Scan", false) => modes.OrderBy(item => item.Mode.Interlaced).ThenBy(item => item.Index),
            ("Scan", true) => modes.OrderByDescending(item => item.Mode.Interlaced).ThenBy(item => item.Index),
            ("PixelClock", false) => modes.OrderBy(item => item.Mode.PixelClockMHz ?? double.MaxValue).ThenBy(item => item.Index),
            ("PixelClock", true) => modes.OrderByDescending(item => item.Mode.PixelClockMHz ?? double.MinValue).ThenBy(item => item.Index),
            ("Source", false) => modes.OrderBy(item => item.Mode.Source, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index),
            ("Source", true) => modes.OrderByDescending(item => item.Mode.Source, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index),
            ("Mark", false) => modes.OrderBy(item => item.Mode.Mark, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index),
            ("Mark", true) => modes.OrderByDescending(item => item.Mode.Mark, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Index),
            _ => modes.OrderBy(item => item.Index)
        };
    }

    private void UpdateSortIndicators()
    {
        UpdateSortIndicator(ResolutionSortIndicator, ResolutionSortIcon, "Resolution");
        UpdateSortIndicator(RefreshSortIndicator, RefreshSortIcon, "Refresh");
        UpdateSortIndicator(ScanSortIndicator, ScanSortIcon, "Scan");
        UpdateSortIndicator(PixelClockSortIndicator, PixelClockSortIcon, "PixelClock");
        UpdateSortIndicator(SourceSortIndicator, SourceSortIcon, "Source");
        UpdateSortIndicator(MarkSortIndicator, MarkSortIcon, "Mark");
        ResetSortButton.IsEnabled = _sortKey is not null;
    }

    private void UpdateSortIndicator(Border indicator, ReIcon icon, string key)
    {
        indicator.Visibility = _sortKey == key ? Visibility.Visible : Visibility.Collapsed;
        if (_sortKey == key)
        {
            icon.Icon = _sortDescending ? ReIconKind.ArrowDown : ReIconKind.ArrowUp;
        }
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }

    private readonly record struct IndexedMode(VideoModeInfo Mode, int Index);
}
