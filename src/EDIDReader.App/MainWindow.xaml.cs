using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EDIDReader.App.Controls;
using EDIDReader.App.Models;
using EDIDReader.App.Pages;
using EDIDReader.App.Services;
using Microsoft.Win32;

namespace EDIDReader.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, PageDefinition> _pages = [];
    private readonly ObservableCollection<MonitorProfile> _monitors = [];
    private readonly ObservableCollection<MonitorProfile> _savedMonitors = [];
    private readonly AppPreferences _preferences = AppPreferences.Current;
    private MonitorProfile? _selectedMonitor;
    private bool _hasNavigated;
    private bool _isScanning;
    private bool _isSelectingNavigation;
    private bool _isSelectingMonitor;
    private bool _isChangingLanguage;
    private long _navigationVersion;
    private string _currentPageKey = "overview";
    private MonitorProfile? _libraryDialogMonitor;
    private LibraryDialogMode _libraryDialogMode;

    public MainWindow()
    {
        InitializeComponent();

        _pages.Add("overview", new("概览", () => new OverviewPage(), true));
        _pages.Add("color", new("色彩", () => new ColorPage(), true));
        _pages.Add("hdr", new("HDR", () => new HdrPage(), true));
        _pages.Add("interface", new("接口与信号", () => new InterfacePage(), true));
        _pages.Add("timings", new("时序", () => new TimingsPage(), true));
        _pages.Add("audio", new("音频", () => new AudioPage(), true));
        _pages.Add("raw", new("原始数据", () => new RawPage(), true));
        _pages.Add("settings", new("设置", () => new SettingsPage(), false));

        MonitorList.ItemsSource = _monitors;
        SavedMonitorList.ItemsSource = _savedMonitors;
        _preferences.PropertyChanged += Preferences_PropertyChanged;
        LocalizationService.Language = _preferences.Language;
        ThemeService.Apply(_preferences.ThemeMode);

        ReloadSavedMonitors();
        ReloadMonitors(false);

        var args = Environment.GetCommandLineArgs();
        var monitorArgument = GetArgumentValue(args, "--monitor");
        if (int.TryParse(monitorArgument, out var requestedMonitor) && requestedMonitor >= 0 && requestedMonitor < _monitors.Count)
        {
            MonitorList.SelectedIndex = requestedMonitor;
            _selectedMonitor = _monitors[requestedMonitor];
        }

        var importArgument = GetArgumentValue(args, "--import");
        if (!string.IsNullOrWhiteSpace(importArgument) && File.Exists(importArgument))
        {
            try
            {
                var importPath = Path.GetFullPath(importArgument);
                var bytes = EdidLibraryService.ReadImportFile(importPath);
                var stored = EdidLibraryService.Store(bytes, "导入", Path.GetFileNameWithoutExtension(importPath), Path.GetFileName(importPath));
                _selectedMonitor = ReloadSavedMonitors(stored.LibraryId) ?? stored;
                ApplyMonitorListSelection(_selectedMonitor);
                RefreshStatus.Text = L(
                    $"已导入 {Path.GetFileName(importPath)}",
                    $"Imported {Path.GetFileName(importPath)}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
            {
                RefreshStatus.Text = L($"导入失败：{exception.Message}", $"Import failed: {exception.Message}");
            }
        }

        var initialPage = GetArgumentValue(args, "--page");
        var initialKey = initialPage is not null && _pages.ContainsKey(initialPage) ? initialPage : "overview";
        Navigate(initialKey);
        SelectNavigationButton(initialKey);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (!_isSelectingNavigation && sender is RadioButton { Tag: string key })
        {
            Navigate(key);
        }
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            return;
        }

        var navigationVersion = ++_navigationVersion;
        var isFirstNavigation = !_hasNavigated;
        _currentPageKey = key;
        MonitorColumn.Width = page.UsesMonitor ? new GridLength(286) : new GridLength(0);
        MonitorPane.Visibility = page.UsesMonitor ? Visibility.Visible : Visibility.Collapsed;
        MonitorActions.Visibility = page.UsesMonitor ? Visibility.Visible : Visibility.Collapsed;
        BreadcrumbMonitor.Visibility = page.UsesMonitor ? Visibility.Visible : Visibility.Collapsed;
        BreadcrumbPageSeparator.Visibility = page.UsesMonitor ? Visibility.Visible : Visibility.Collapsed;
        BreadcrumbMonitor.Text = _selectedMonitor?.Name ?? "未检测到显示器";
        BreadcrumbPage.Text = LocalizationService.Translate(page.Title);

        DependencyObject localizationRoot;
        if (page.UsesMonitor && _selectedMonitor is null)
        {
            var emptyState = CreateEmptyState();
            PageHost.Content = emptyState;
            localizationRoot = emptyState;
        }
        else
        {
            var pageControl = page.Factory();
            pageControl.DataContext = page.UsesMonitor ? _selectedMonitor : _preferences;
            PageHost.Content = pageControl;
            localizationRoot = pageControl;
        }

        if (_hasNavigated)
        {
            PreparePageEntrance();
            Dispatcher.BeginInvoke(() =>
            {
                if (navigationVersion == _navigationVersion)
                {
                    AnimatePageEntrance(navigationVersion);
                }
            }, DispatcherPriority.Loaded);
        }
        else
        {
            PageHost.BeginAnimation(OpacityProperty, null);
            PageHost.Opacity = 1;
            PageHost.RenderTransform = Transform.Identity;
        }

        _hasNavigated = true;
        if (!_isChangingLanguage && LocalizationService.Language == "en-US")
        {
            var target = isFirstNavigation ? this : localizationRoot;
            Dispatcher.BeginInvoke(() => LocalizationService.ApplyToTree(target), DispatcherPriority.Background);
        }
    }

    private static Border CreateEmptyState()
    {
        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(48),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "未检测到活动显示器", FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "请连接显示器后点击刷新。", Margin = new Thickness(0, 10, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(140, 135, 127)), HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
    }

    private void PreparePageEntrance()
    {
        PageHost.BeginAnimation(OpacityProperty, null);
        PageHost.Opacity = 0;
        PageHost.RenderTransform = new TranslateTransform(0, 8);
    }

    private void AnimatePageEntrance(long navigationVersion)
    {
        var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (navigationVersion != _navigationVersion)
            {
                return;
            }

            PageHost.BeginAnimation(OpacityProperty, null);
            PageHost.Opacity = 1;
            if (PageHost.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.YProperty, null);
            }
            PageHost.RenderTransform = Transform.Identity;
            RenderOptions.SetClearTypeHint(PageHost, ClearTypeHint.Enabled);
        };
        PageHost.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        ((TranslateTransform)PageHost.RenderTransform).BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void SelectNavigationButton(string key)
    {
        _isSelectingNavigation = true;
        try
        {
            foreach (var radioButton in FindVisualChildren<RadioButton>(this))
            {
                if (Equals(radioButton.Tag, key))
                {
                    radioButton.IsChecked = true;
                    return;
                }
            }
        }
        finally
        {
            _isSelectingNavigation = false;
        }
    }

    private void ReloadMonitors(bool showCompletion)
    {
        var selectedPath = _selectedMonitor?.IsActive == true ? _selectedMonitor.DevicePath : null;
        var selectedLibraryId = _selectedMonitor?.IsActive == false ? _selectedMonitor.LibraryId : null;
        _isScanning = true;
        try
        {
            var detected = WindowsDisplayService.ReadActiveMonitors();
            _monitors.Clear();
            foreach (var monitor in detected)
            {
                _monitors.Add(monitor);
            }

            _selectedMonitor = !string.IsNullOrWhiteSpace(selectedLibraryId)
                ? _savedMonitors.FirstOrDefault(monitor => monitor.LibraryId == selectedLibraryId)
                : _monitors.FirstOrDefault(monitor => string.Equals(monitor.DevicePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            _selectedMonitor ??= _monitors.FirstOrDefault() ?? _savedMonitors.FirstOrDefault();
            ApplyMonitorListSelection(_selectedMonitor);
            UpdateMonitorCounts();
            UpdateMonitorActionState();
            RefreshStatus.Text = showCompletion
                ? L(
                    $"{DateTime.Now:HH:mm:ss} 已重新读取 {_monitors.Count} 台显示器",
                    $"{DateTime.Now:HH:mm:ss} rescanned {_monitors.Count} active display(s)")
                : L($"已读取 {_monitors.Count} 台活动显示器", $"Read {_monitors.Count} active display(s)");
        }
        catch (Exception exception)
        {
            _monitors.Clear();
            if (_selectedMonitor?.IsActive != false)
            {
                _selectedMonitor = _savedMonitors.FirstOrDefault();
            }
            ApplyMonitorListSelection(_selectedMonitor);
            MonitorCountText.Text = LocalizationService.Translate("读取失败");
            UpdateMonitorActionState();
            RefreshStatus.Text = exception.Message.Trim();
        }
        finally
        {
            _isScanning = false;
        }
    }

    private MonitorProfile? ReloadSavedMonitors(string? selectLibraryId = null)
    {
        var preservedId = selectLibraryId ?? (_selectedMonitor?.IsActive == false ? _selectedMonitor.LibraryId : null);
        var loaded = EdidLibraryService.LoadAll();
        _savedMonitors.Clear();
        foreach (var monitor in loaded)
        {
            _savedMonitors.Add(monitor);
        }

        var target = string.IsNullOrWhiteSpace(preservedId)
            ? null
            : _savedMonitors.FirstOrDefault(monitor => monitor.LibraryId == preservedId);
        if (target is not null)
        {
            _selectedMonitor = target;
            ApplyMonitorListSelection(target);
        }
        else if (_selectedMonitor?.IsActive == false)
        {
            _selectedMonitor = _monitors.FirstOrDefault() ?? _savedMonitors.FirstOrDefault();
            ApplyMonitorListSelection(_selectedMonitor);
        }

        UpdateMonitorCounts();
        UpdateMonitorActionState();
        return target;
    }

    private void UpdateMonitorCounts()
    {
        MonitorCountText.Text = L($"{_monitors.Count} 台活动显示器", $"{_monitors.Count} active display(s)");
        SavedMonitorCountText.Text = _savedMonitors.Count.ToString();
        var savedVisibility = _savedMonitors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedMonitorSectionHeader.Visibility = savedVisibility;
        SavedMonitorList.Visibility = savedVisibility;
    }

    private void UpdateMonitorActionState()
    {
        SaveMonitorButton.IsEnabled = _selectedMonitor is { IsActive: true, RawBytes.Length: >= 128 };
    }

    private void ApplyMonitorListSelection(MonitorProfile? monitor)
    {
        _isSelectingMonitor = true;
        try
        {
            MonitorList.SelectedItem = monitor?.IsActive == true ? monitor : null;
            SavedMonitorList.SelectedItem = monitor?.IsActive == false ? monitor : null;
        }
        finally
        {
            _isSelectingMonitor = false;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ReloadMonitors(true);
        Navigate(_currentPageKey);
    }

    private void ImportEdid_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 EDID",
            InitialDirectory = _preferences.DialogDirectory,
            Filter = "EDID 文件 (*.edid;*.bin;*.dat;*.json)|*.edid;*.bin;*.dat;*.json|所有文件 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        _preferences.RememberDialogFile(dialog.FileNames[0]);

        string? lastLibraryId = null;
        var importedCount = 0;
        var errors = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var bytes = EdidLibraryService.ReadImportFile(path);
                var stored = EdidLibraryService.Store(bytes, "导入", Path.GetFileNameWithoutExtension(path), Path.GetFileName(path));
                lastLibraryId = stored.LibraryId;
                importedCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
            {
                errors.Add($"{Path.GetFileName(path)}：{exception.Message}");
            }
        }

        var selected = ReloadSavedMonitors(lastLibraryId);
        if (selected is not null)
        {
            _selectedMonitor = selected;
            ApplyMonitorListSelection(selected);
            Navigate(_currentPageKey);
        }
        RefreshStatus.Text = errors.Count == 0
            ? L($"已导入 {importedCount} 个 EDID", $"Imported {importedCount} EDID file(s)")
            : L(
                $"已导入 {importedCount} 个，失败 {errors.Count} 个：{errors[0]}",
                $"Imported {importedCount}; {errors.Count} failed: {errors[0]}");
    }

    private void SaveEdid_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor is not { IsActive: true, RawBytes.Length: >= 128 } monitor)
        {
            return;
        }

        try
        {
            var stored = EdidLibraryService.Store(monitor.RawBytes, "已保存", monitor.Name, monitor.Name);
            ReloadSavedMonitors();
            RefreshStatus.Text = L(
                $"已将 {monitor.Name} 保存到显示器库 · {stored.LibrarySavedAtText}",
                $"Saved {monitor.Name} to the display library · {stored.LibrarySavedAtText}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            RefreshStatus.Text = exception.Message;
        }
    }

    private void SavedMonitorMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MonitorProfile { IsActive: false } monitor } button || button.ContextMenu is not { } menu)
        {
            return;
        }

        e.Handled = true;
        menu.DataContext = monitor;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        Dispatcher.BeginInvoke(() => LocalizationService.ApplyToTree(menu), DispatcherPriority.Loaded);
    }

    private void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu)
        {
            return;
        }

        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        Dispatcher.BeginInvoke(() => LocalizationService.ApplyToTree(menu), DispatcherPriority.Loaded);
    }

    private void SavedMonitorMenu_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            System.Windows.Automation.AutomationProperties.SetName(
                button,
                LocalizationService.Translate("已保存显示器操作"));
            button.ToolTip = LocalizationService.Translate("更多操作");
        }
    }

    private void RenameSavedMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MonitorProfile { IsActive: false } monitor })
        {
            OpenLibraryDialog(monitor, LibraryDialogMode.Rename);
        }
    }

    private void DeleteSavedMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MonitorProfile { IsActive: false } monitor })
        {
            OpenLibraryDialog(monitor, LibraryDialogMode.Delete);
        }
    }

    private void DeleteAllSaved_Click(object sender, RoutedEventArgs e)
    {
        if (_savedMonitors.Count == 0)
        {
            return;
        }

        _libraryDialogMonitor = null;
        _libraryDialogMode = LibraryDialogMode.DeleteAll;
        LibraryDialogError.Visibility = Visibility.Collapsed;
        LibraryDialogOverlay.Visibility = Visibility.Visible;
        LibraryDialogConfirmButton.IsDefault = true;
        LibraryDialogTitle.Text = L("删除所有已保存", "Delete all saved displays");
        LibraryDialogDescription.Text = L(
            $"确定删除全部 {_savedMonitors.Count} 条已保存记录吗？此操作只会删除软件目录中的保存记录。",
            $"Delete all {_savedMonitors.Count} saved record(s)? This removes only the saved records from the application folder.");
        LibraryDialogDescription.Visibility = Visibility.Visible;
        LibraryNameEditor.Visibility = Visibility.Collapsed;
        LibraryDialogConfirmButton.Content = L("删除全部", "Delete all");
        LibraryDialogConfirmButton.Background = (Brush)FindResource("RedSoftBrush");
        LibraryDialogConfirmButton.Foreground = (Brush)FindResource("RedBrush");
        LibraryDialogConfirmButton.BorderBrush = (Brush)FindResource("RedSoftBrush");
        Dispatcher.BeginInvoke(() => LibraryDialogConfirmButton.Focus(), DispatcherPriority.Input);
        LocalizationService.ApplyToTree(LibraryDialogOverlay);
    }

    private void OpenLibraryDialog(MonitorProfile monitor, LibraryDialogMode mode)
    {
        _libraryDialogMonitor = monitor;
        _libraryDialogMode = mode;
        LibraryDialogError.Visibility = Visibility.Collapsed;
        LibraryDialogOverlay.Visibility = Visibility.Visible;
        LibraryDialogConfirmButton.IsDefault = true;

        if (mode == LibraryDialogMode.Rename)
        {
            LibraryDialogTitle.Text = LocalizationService.Translate("重命名");
            LibraryDialogDescription.Visibility = Visibility.Collapsed;
            LibraryNameEditor.Visibility = Visibility.Visible;
            LibraryNameTextBox.Text = monitor.Name;
            LibraryDialogConfirmButton.Content = LocalizationService.Translate("确认");
            LibraryDialogConfirmButton.Background = (Brush)FindResource("CardBrush");
            LibraryDialogConfirmButton.Foreground = (Brush)FindResource("InkBrush");
            LibraryDialogConfirmButton.BorderBrush = (Brush)FindResource("LineBrush");
            Dispatcher.BeginInvoke(() =>
            {
                LibraryNameTextBox.Focus();
                LibraryNameTextBox.SelectAll();
            }, DispatcherPriority.Input);
        }
        else
        {
            LibraryDialogTitle.Text = LocalizationService.Translate("删除");
            LibraryDialogDescription.Text = LocalizationService.Language == "en-US"
                ? $"Delete “{monitor.Name}”? This removes only the saved record from the application folder."
                : $"确定删除“{monitor.Name}”吗？此操作只会删除软件目录中的保存记录。";
            LibraryDialogDescription.Visibility = Visibility.Visible;
            LibraryNameEditor.Visibility = Visibility.Collapsed;
            LibraryDialogConfirmButton.Content = LocalizationService.Translate("删除");
            LibraryDialogConfirmButton.Background = (Brush)FindResource("RedSoftBrush");
            LibraryDialogConfirmButton.Foreground = (Brush)FindResource("RedBrush");
            LibraryDialogConfirmButton.BorderBrush = (Brush)FindResource("RedSoftBrush");
            Dispatcher.BeginInvoke(() => LibraryDialogConfirmButton.Focus(), DispatcherPriority.Input);
        }

        LocalizationService.ApplyToTree(LibraryDialogOverlay);
    }

    private void LibraryDialogCancel_Click(object sender, RoutedEventArgs e)
        => CloseLibraryDialog();

    private void LibraryDialogConfirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_libraryDialogMode == LibraryDialogMode.DeleteAll)
            {
                var removedCount = EdidLibraryService.DeleteAll();
                var wasOfflineSelected = _selectedMonitor?.IsActive == false;
                CloseLibraryDialog();
                if (wasOfflineSelected)
                {
                    _selectedMonitor = _monitors.FirstOrDefault();
                }
                ReloadSavedMonitors();
                _selectedMonitor ??= _monitors.FirstOrDefault();
                ApplyMonitorListSelection(_selectedMonitor);
                Navigate(_currentPageKey);
                RefreshStatus.Text = L(
                    $"已删除 {removedCount} 条已保存记录",
                    $"Deleted {removedCount} saved record(s)");
                return;
            }

            if (_libraryDialogMonitor is not { } monitor)
            {
                CloseLibraryDialog();
                return;
            }

            if (_libraryDialogMode == LibraryDialogMode.Rename)
            {
                var newName = LibraryNameTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    ShowLibraryDialogError(LocalizationService.Language == "en-US" ? "Display name cannot be empty." : "显示器名称不能为空。");
                    return;
                }

                var renamed = EdidLibraryService.Rename(monitor, newName);
                var selected = ReloadSavedMonitors(renamed.LibraryId);
                CloseLibraryDialog();
                if (selected is not null)
                {
                    _selectedMonitor = selected;
                    ApplyMonitorListSelection(selected);
                    Navigate(_currentPageKey);
                }
                RefreshStatus.Text = LocalizationService.Language == "en-US"
                    ? $"Renamed to {newName}"
                    : $"已重命名为 {newName}";
            }
            else
            {
                var deletedId = monitor.LibraryId;
                var wasSelected = _selectedMonitor?.IsActive == false && _selectedMonitor.LibraryId == deletedId;
                EdidLibraryService.Delete(monitor);
                CloseLibraryDialog();
                if (wasSelected)
                {
                    _selectedMonitor = _monitors.FirstOrDefault();
                }
                ReloadSavedMonitors();
                _selectedMonitor ??= _savedMonitors.FirstOrDefault();
                ApplyMonitorListSelection(_selectedMonitor);
                Navigate(_currentPageKey);
                RefreshStatus.Text = LocalizationService.Language == "en-US"
                    ? $"Deleted {monitor.Name}"
                    : $"已删除 {monitor.Name}";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {
            ShowLibraryDialogError(exception.Message);
        }
    }

    private void LibraryDialog_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CloseLibraryDialog();
            e.Handled = true;
        }
    }

    private void ShowLibraryDialogError(string message)
    {
        LibraryDialogError.Text = message;
        LibraryDialogError.Visibility = Visibility.Visible;
    }

    private void CloseLibraryDialog()
    {
        LibraryDialogOverlay.Visibility = Visibility.Collapsed;
        LibraryDialogConfirmButton.IsDefault = false;
        _libraryDialogMonitor = null;
        PageHost.Focus();
    }

    private void ExportEdid_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor is not { RawBytes.Length: >= 128 } monitor)
        {
            RefreshStatus.Text = L("当前显示器没有可导出的原始 EDID", "The current display has no raw EDID to export");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出原始 EDID",
            InitialDirectory = _preferences.DialogDirectory,
            FileName = $"{PortableStorageService.SanitizeFileName(monitor.Name)}.edid",
            DefaultExt = ".edid",
            Filter = "EDID 文件 (*.edid)|*.edid|EDID 二进制文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllBytes(dialog.FileName, monitor.RawBytes);
        _preferences.RememberDialogFile(dialog.FileName);
        RefreshStatus.Text = L(
            $"已导出 {Path.GetFileName(dialog.FileName)}",
            $"Exported {Path.GetFileName(dialog.FileName)}");
    }

    private void ExportSummaryImage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMonitor is not { } monitor)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = L("导出 EDID 图片", "Export EDID image"),
            InitialDirectory = _preferences.DialogDirectory,
            FileName = $"{PortableStorageService.SanitizeFileName(monitor.Name)} EDID.png",
            DefaultExt = ".png",
            Filter = "PNG 图片 (*.png)|*.png",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        EdidSummaryImageService.Export(monitor, dialog.FileName);
        _preferences.RememberDialogFile(dialog.FileName);
        RefreshStatus.Text = L(
            $"图片已导出到 {Path.GetFileName(dialog.FileName)}",
            $"Exported image to {Path.GetFileName(dialog.FileName)}");
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isScanning || _isSelectingMonitor || MonitorList.SelectedItem is not MonitorProfile monitor)
        {
            return;
        }
        SelectMonitor(monitor);
    }

    private void SavedMonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isScanning || _isSelectingMonitor || SavedMonitorList.SelectedItem is not MonitorProfile monitor)
        {
            return;
        }
        SelectMonitor(monitor);
    }

    private void SelectMonitor(MonitorProfile monitor)
    {
        _selectedMonitor = monitor;
        ApplyMonitorListSelection(monitor);
        UpdateMonitorActionState();
        RefreshStatus.Text = L($"已切换到 {monitor.Name}", $"Switched to {monitor.Name}");
        Navigate(_currentPageKey);
    }

    private void Preferences_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPreferences.ThemeMode))
        {
            ThemeService.Apply(_preferences.ThemeMode);
        }
        else if (e.PropertyName == nameof(AppPreferences.Language))
        {
            LocalizationService.Language = _preferences.Language;
            _isChangingLanguage = true;
            try
            {
                Navigate(_currentPageKey);
            }
            finally
            {
                _isChangingLanguage = false;
            }
            Dispatcher.BeginInvoke(() => LocalizationService.ApplyToTree(this), DispatcherPriority.Background);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        SelectNavigationButton(_currentPageKey);
        var args = Environment.GetCommandLineArgs();
        var summaryPath = GetArgumentValue(args, "--summary");
        if (!string.IsNullOrWhiteSpace(summaryPath) && _selectedMonitor is not null)
        {
            Dispatcher.BeginInvoke(() =>
            {
                EdidSummaryImageService.Export(_selectedMonitor, Path.GetFullPath(summaryPath));
                Close();
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        var captureIndex = Array.IndexOf(args, "--capture");
        if (captureIndex < 0 || captureIndex + 1 >= args.Length)
        {
            return;
        }

        var outputPath = Path.GetFullPath(args[captureIndex + 1]);
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(240);
            CaptureToPng(outputPath);
            Close();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void CaptureToPng(string outputPath)
    {
        CaptureRoot.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(CaptureRoot);
        var width = Math.Max(1, (int)Math.Round(CaptureRoot.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(CaptureRoot.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(CaptureRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Create(outputPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }
            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string? GetArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string L(string chinese, string english)
        => LocalizationService.Language == "en-US" ? english : chinese;

    private enum LibraryDialogMode
    {
        Rename,
        Delete,
        DeleteAll
    }

    private sealed record PageDefinition(string Title, Func<SectionedPage> Factory, bool UsesMonitor);
}
