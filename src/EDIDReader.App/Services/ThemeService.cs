using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace EDIDReader.App.Services;

internal static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Palette =
        new Dictionary<string, (string, string)>
        {
            ["InkBrush"] = ("#211F1C", "#F3EFE9"),
            ["MutedBrush"] = ("#8C877F", "#AAA39A"),
            ["SubtleBrush"] = ("#AAA39A", "#817B74"),
            ["AppBackgroundBrush"] = ("#DED5C8", "#12110F"),
            ["ShellBrush"] = ("#F9F6F2", "#1C1916"),
            ["PanelBrush"] = ("#F4F0EB", "#211E1B"),
            ["PanelSoftBrush"] = ("#FAF8F5", "#181614"),
            ["SelectedBrush"] = ("#EEE8E1", "#342F2A"),
            ["LineBrush"] = ("#E5DED6", "#3D3731"),
            ["CardBrush"] = ("#FFFFFF", "#25211E"),
            ["CardHoverBrush"] = ("#FDFBF8", "#2D2925"),
            ["TableHeaderBrush"] = ("#F5F1EC", "#2C2824"),
            ["ChartBackgroundBrush"] = ("#FAF8F5", "#1C1917"),
            ["CodeSurfaceBrush"] = ("#242220", "#0F0E0D"),
            ["RailBrush"] = ("#F0EBE5", "#211D1A"),
            ["GreenBrush"] = ("#1C9B50", "#48C47A"),
            ["GreenSoftBrush"] = ("#E9F7EC", "#193626"),
            ["BlueBrush"] = ("#168CCB", "#55B5E7"),
            ["BlueSoftBrush"] = ("#E8F5FB", "#173344"),
            ["OrangeBrush"] = ("#E47724", "#F59A55"),
            ["OrangeSoftBrush"] = ("#FFF1E4", "#3E2A1B"),
            ["PurpleBrush"] = ("#7954D8", "#A98AFA"),
            ["PurpleSoftBrush"] = ("#F0EBFC", "#2F2744"),
            ["RedBrush"] = ("#E54848", "#FF7777"),
            ["RedSoftBrush"] = ("#FCECEC", "#421F22")
        };

    private static string _mode = "System";
    private static bool _initialized;

    public static bool IsDark { get; private set; }

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Apply(_mode);
    }

    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _initialized = false;
    }

    public static void Apply(string mode)
    {
        _mode = mode is "Light" or "Dark" ? mode : "System";
        IsDark = _mode == "Dark" || (_mode == "System" && SystemUsesDarkTheme());

        foreach (var (key, colors) in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(IsDark ? colors.Dark : colors.Light);
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_mode != "System")
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() => Apply("System"));
    }
}
