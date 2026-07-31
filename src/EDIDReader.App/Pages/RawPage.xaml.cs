using EDIDReader.App.Controls;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EDIDReader.App.Models;
using EDIDReader.App.Services;
using Microsoft.Win32;

namespace EDIDReader.App.Pages;

public partial class RawPage : SectionedPage
{
    public RawPage()
    {
        InitializeComponent();
    }

    private void CopyBytes_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor)
        {
            return;
        }

        if (sender is Button button)
        {
            button.Content = LocalizationService.Translate(ClipboardService.TrySetText(monitor.RawHexDump) ? "已复制" : "复制失败");
        }
    }

    private void SaveBinary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorProfile monitor)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存 EDID 二进制数据",
            InitialDirectory = AppPreferences.Current.DialogDirectory,
            FileName = $"{PortableStorageService.SanitizeFileName(monitor.Name)} EDID.bin",
            DefaultExt = ".bin",
            Filter = "EDID 二进制文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        File.WriteAllBytes(dialog.FileName, monitor.RawBytes);
        AppPreferences.Current.RememberDialogFile(dialog.FileName);

        if (sender is Button button)
        {
            button.Content = "已保存";
        }
    }
}
