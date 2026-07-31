using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EDIDReader.App.Services;

namespace EDIDReader.App.Models;

public sealed class AppPreferences : INotifyPropertyChanged
{
    private static readonly string PreferencesPath = PortableStorageService.PreferencesPath;
    public static AppPreferences Current { get; } = Load();

    private string _themeMode = "System";
    private string _language = "zh-CN";
    private string _lastDialogDirectory = string.Empty;

    public string ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    public string Language
    {
        get => _language;
        set => SetField(ref _language, value);
    }

    public string DialogDirectory
        => Directory.Exists(_lastDialogDirectory)
            ? _lastDialogDirectory
            : PortableStorageService.ExportsDirectory;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
            {
                return new AppPreferences();
            }

            var stored = JsonSerializer.Deserialize<StoredPreferences>(File.ReadAllText(PreferencesPath));
            return new AppPreferences
            {
                _themeMode = stored?.ThemeMode is "Light" or "Dark" ? stored.ThemeMode : "System",
                _language = stored?.Language == "en-US" ? "en-US" : "zh-CN",
                _lastDialogDirectory = stored?.LastDialogDirectory ?? string.Empty
            };
        }
        catch (IOException)
        {
            return new AppPreferences();
        }
        catch (JsonException)
        {
            return new AppPreferences();
        }
    }

    public void RememberDialogFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            SetField(ref _lastDialogDirectory, directory, nameof(DialogDirectory));
        }
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Save();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            File.WriteAllText(
                PreferencesPath,
                JsonSerializer.Serialize(new StoredPreferences(ThemeMode, Language, _lastDialogDirectory)));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record StoredPreferences(string ThemeMode, string Language, string? LastDialogDirectory = null);
}
