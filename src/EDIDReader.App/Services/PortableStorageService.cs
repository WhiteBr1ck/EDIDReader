using System.IO;

namespace EDIDReader.App.Services;

public static class PortableStorageService
{
    public static string RootDirectory { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static string DataDirectory { get; } = Path.Combine(RootDirectory, "Data");
    public static string DisplayLibraryDirectory { get; } = Path.Combine(DataDirectory, "Displays");
    public static string ExportsDirectory { get; } = Path.Combine(RootDirectory, "Exports");
    public static string PreferencesPath { get; } = Path.Combine(DataDirectory, "preferences.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DisplayLibraryDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }

    public static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Display" : sanitized;
    }
}
