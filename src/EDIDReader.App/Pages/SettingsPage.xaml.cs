using System.Windows;
using System.Windows.Controls;
using EDIDReader.App.Controls;
using EDIDReader.App.Models;

namespace EDIDReader.App.Pages;

public partial class SettingsPage : SectionedPage
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AppPreferences preferences)
        {
            return;
        }

        _loading = true;
        ThemeSystem.IsChecked = preferences.ThemeMode == "System";
        ThemeLight.IsChecked = preferences.ThemeMode == "Light";
        ThemeDark.IsChecked = preferences.ThemeMode == "Dark";
        LanguageChinese.IsChecked = preferences.Language == "zh-CN";
        LanguageEnglish.IsChecked = preferences.Language == "en-US";
        _loading = false;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loading && DataContext is AppPreferences preferences && sender is RadioButton { Tag: string mode })
        {
            preferences.ThemeMode = mode;
        }
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loading && DataContext is AppPreferences preferences && sender is RadioButton { Tag: string language })
        {
            preferences.Language = language;
        }
    }
}
