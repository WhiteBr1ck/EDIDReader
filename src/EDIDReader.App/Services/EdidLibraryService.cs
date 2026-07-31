using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using EDIDReader.App.Models;

namespace EDIDReader.App.Services;

public static class EdidLibraryService
{
    private const string FormatVersion = "2";
    private static readonly byte[] EdidHeader = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<MonitorProfile> LoadAll()
    {
        PortableStorageService.EnsureDirectories();
        var profiles = new List<(DateTimeOffset SavedAt, MonitorProfile Profile)>();
        var colorIndex = 0;
        foreach (var path in Directory.EnumerateFiles(PortableStorageService.DisplayLibraryDirectory, "*.edid.json")
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<LibraryEntry>(File.ReadAllText(path));
                if (entry is null || string.IsNullOrWhiteSpace(entry.RawEdidBase64))
                {
                    continue;
                }

                var bytes = Convert.FromBase64String(entry.RawEdidBase64);
                ValidateRawEdid(bytes);
                var id = string.IsNullOrWhiteSpace(entry.Id) ? GetId(bytes) : entry.Id;
                var savedAt = entry.SavedAt == default
                    ? new DateTimeOffset(File.GetLastWriteTime(path))
                    : entry.SavedAt;
                profiles.Add((savedAt, WindowsDisplayService.CreateOfflineProfile(
                    bytes,
                    id,
                    NormalizeLibraryKind(entry.LibraryKind),
                    path,
                    entry.DisplayName,
                    colorIndex++,
                    savedAt)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidDataException)
            {
            }
        }

        return profiles
            .OrderByDescending(item => item.SavedAt)
            .ThenBy(item => item.Profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Profile)
            .ToArray();
    }

    public static MonitorProfile Store(byte[] rawEdid, string libraryKind, string displayName, string originalFileName)
    {
        ValidateRawEdid(rawEdid);
        PortableStorageService.EnsureDirectories();
        var id = Guid.NewGuid().ToString("N");
        var savedAt = DateTimeOffset.Now;
        var normalizedKind = NormalizeLibraryKind(libraryKind);
        var normalizedName = NormalizeDisplayName(displayName);
        var path = CreateAvailableLibraryPath(savedAt, normalizedName, id);

        var entry = new LibraryEntry(
            FormatVersion,
            id,
            normalizedKind,
            normalizedName,
            originalFileName,
            savedAt,
            Convert.ToBase64String(rawEdid));
        WriteEntry(path, entry);
        return WindowsDisplayService.CreateOfflineProfile(rawEdid, id, normalizedKind, path, normalizedName, 0, savedAt);
    }

    public static MonitorProfile Rename(MonitorProfile monitor, string displayName)
    {
        if (monitor.IsActive || string.IsNullOrWhiteSpace(monitor.LibraryPath))
        {
            throw new InvalidOperationException("只有显示器库中的记录可以重命名。");
        }

        var sourcePath = ResolveLibraryPath(monitor.LibraryPath);
        var entry = ReadEntry(sourcePath);
        var bytes = Convert.FromBase64String(entry.RawEdidBase64);
        ValidateRawEdid(bytes);
        var id = string.IsNullOrWhiteSpace(entry.Id) ? GetId(bytes) : entry.Id;
        var savedAt = entry.SavedAt == default
            ? new DateTimeOffset(File.GetLastWriteTime(sourcePath))
            : entry.SavedAt;
        var normalizedName = NormalizeDisplayName(displayName);
        var updatedEntry = entry with { FormatVersion = FormatVersion, Id = id, DisplayName = normalizedName, SavedAt = savedAt };
        var targetPath = CreateAvailableLibraryPath(savedAt, normalizedName, id, sourcePath);
        WriteEntry(targetPath, updatedEntry);
        if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(sourcePath);
        }

        return WindowsDisplayService.CreateOfflineProfile(
            bytes,
            id,
            NormalizeLibraryKind(entry.LibraryKind),
            targetPath,
            normalizedName,
            0,
            savedAt);
    }

    public static void Delete(MonitorProfile monitor)
    {
        if (monitor.IsActive || string.IsNullOrWhiteSpace(monitor.LibraryPath))
        {
            throw new InvalidOperationException("只有显示器库中的记录可以删除。");
        }

        var path = ResolveLibraryPath(monitor.LibraryPath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static int DeleteAll()
    {
        PortableStorageService.EnsureDirectories();
        var paths = Directory
            .EnumerateFiles(PortableStorageService.DisplayLibraryDirectory, "*.edid.json")
            .Select(ResolveLibraryPath)
            .ToArray();
        var deletedCount = 0;
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                deletedCount++;
            }
        }
        return deletedCount;
    }

    public static byte[] ReadImportFile(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!TryReadJsonBytes(document.RootElement, out var jsonBytes))
            {
                throw new InvalidDataException("JSON 文件中没有可导入的原始 EDID 字节。");
            }
            ValidateRawEdid(jsonBytes);
            return jsonBytes;
        }

        var bytes = File.ReadAllBytes(path);
        ValidateRawEdid(bytes);
        return bytes;
    }

    public static string GetId(byte[] rawEdid)
        => Convert.ToHexStringLower(SHA256.HashData(rawEdid));

    public static void ValidateRawEdid(byte[] bytes)
    {
        if (bytes.Length < 128)
        {
            throw new InvalidDataException("EDID 文件少于 128 字节。");
        }
        if (bytes.Length % 128 != 0)
        {
            throw new InvalidDataException("EDID 文件长度不是 128 字节数据块的整数倍。");
        }
        if (!bytes.AsSpan(0, EdidHeader.Length).SequenceEqual(EdidHeader))
        {
            throw new InvalidDataException("文件没有有效的 EDID 头标识。");
        }

        var requiredLength = (bytes[126] + 1) * 128;
        if (bytes.Length < requiredLength)
        {
            throw new InvalidDataException($"EDID 声明需要 {requiredLength} 字节，但文件只有 {bytes.Length} 字节。");
        }
    }

    private static bool TryReadJsonBytes(JsonElement element, out byte[] bytes)
    {
        bytes = [];
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "RawEdidBase64", "RawBytes" })
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                try
                {
                    bytes = Convert.FromBase64String(value.GetString() ?? string.Empty);
                    return bytes.Length > 0;
                }
                catch (FormatException)
                {
                }
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    bytes = value.EnumerateArray().Select(item => item.GetByte()).ToArray();
                    return bytes.Length > 0;
                }
                catch (Exception exception) when (exception is FormatException or InvalidOperationException)
                {
                }
            }
        }

        return element.TryGetProperty("Monitor", out var monitor) && TryReadJsonBytes(monitor, out bytes);
    }

    private static string NormalizeLibraryKind(string value) => value == "已保存" ? "已保存" : "导入";

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException("显示器名称不能为空。");
        }
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static LibraryEntry ReadEntry(string path)
    {
        var entry = JsonSerializer.Deserialize<LibraryEntry>(File.ReadAllText(path));
        if (entry is null || string.IsNullOrWhiteSpace(entry.RawEdidBase64))
        {
            throw new InvalidDataException("显示器库记录不完整。");
        }
        return entry;
    }

    private static void WriteEntry(string path, LibraryEntry entry)
    {
        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateAvailableLibraryPath(
        DateTimeOffset savedAt,
        string displayName,
        string id,
        string? currentPath = null)
    {
        var timestamp = savedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff");
        var safeName = PortableStorageService.SanitizeFileName(displayName);
        var idSuffix = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..8] : id[..Math.Min(8, id.Length)];
        var baseName = $"{timestamp}_{safeName}_{idSuffix}";
        var path = Path.Combine(PortableStorageService.DisplayLibraryDirectory, $"{baseName}.edid.json");
        var suffix = 2;
        while (File.Exists(path) && !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(PortableStorageService.DisplayLibraryDirectory, $"{baseName}_{suffix++}.edid.json");
        }
        return path;
    }

    private static string ResolveLibraryPath(string path)
    {
        var libraryRoot = Path.GetFullPath(PortableStorageService.DisplayLibraryDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".edid.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("显示器库文件路径无效。");
        }
        return fullPath;
    }

    private sealed record LibraryEntry(
        string FormatVersion,
        string Id,
        string LibraryKind,
        string DisplayName,
        string OriginalFileName,
        DateTimeOffset SavedAt,
        string RawEdidBase64);
}
