using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClearOfficeViewer.Core.Models;

namespace ClearOfficeViewer.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new ResilientBackdropConverter(), new JsonStringEnumConverter() }
    };

    private sealed class ResilientBackdropConverter : JsonConverter<BackdropMaterial>
    {
        public override BackdropMaterial Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? raw = reader.GetString();
                if (Enum.TryParse<BackdropMaterial>(raw, ignoreCase: true, out var parsed))
                    return parsed;
                return BackdropMaterial.DeepAcrylic;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int num) && Enum.IsDefined(typeof(BackdropMaterial), num))
            {
                return (BackdropMaterial)num;
            }
            return BackdropMaterial.DeepAcrylic;
        }

        public override void Write(Utf8JsonWriter writer, BackdropMaterial value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private readonly string _settingsFilePath;
    private readonly object _sync = new();

    public AppSettings Current { get; private set; } = new();
    public string SettingsFilePath => _settingsFilePath;

    public SettingsService(string? customSettingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsFilePath = Path.Combine(localAppData, "ClearOfficeViewer", "settings.json");
        }

        Load();
    }

    public void Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (loaded != null)
                    {
                        Current = loaded;
                        SyncFavoriteFlags();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(nameof(SettingsService), "Failed to load settings.json; using defaults.", ex);
            }

            Current = new AppSettings();
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string tempPath = _settingsFilePath + ".tmp";
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _settingsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error(nameof(SettingsService), "Failed to save settings.json.", ex);
            }
        }
    }

    public void RecordFileOpened(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        lock (_sync)
        {
            string normalized = Path.GetFullPath(filePath);
            long size = 0;
            try
            {
                if (File.Exists(normalized))
                    size = new FileInfo(normalized).Length;
            }
            catch
            {
                // Ignore stat failure.
            }

            bool isFav = Current.FavoritePaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));

            Current.RecentFiles.RemoveAll(r => string.Equals(r.FilePath, normalized, StringComparison.OrdinalIgnoreCase));
            Current.RecentFiles.Insert(0, new RecentFileEntry
            {
                FilePath = normalized,
                FileName = Path.GetFileName(normalized),
                FileKind = FileFormatDetector.DetectFromFile(normalized),
                LastOpenedUtc = DateTimeOffset.UtcNow,
                FileSizeBytes = size,
                IsFavorite = isFav
            });

            int max = Math.Clamp(Current.MaxRecentFiles, 5, 100);
            if (Current.RecentFiles.Count > max)
                Current.RecentFiles.RemoveRange(max, Current.RecentFiles.Count - max);

            Save();
        }
    }

    public bool ToggleFavorite(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        lock (_sync)
        {
            string normalized = Path.GetFullPath(filePath);
            int idx = Current.FavoritePaths.FindIndex(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
            bool nowFavorite;
            if (idx >= 0)
            {
                Current.FavoritePaths.RemoveAt(idx);
                nowFavorite = false;
            }
            else
            {
                Current.FavoritePaths.Insert(0, normalized);
                nowFavorite = true;
            }

            SyncFavoriteFlags();
            Save();
            return nowFavorite;
        }
    }

    public bool IsFavorite(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        lock (_sync)
        {
            string normalized = Path.GetFullPath(filePath);
            return Current.FavoritePaths.Any(p => string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<RecentFileEntry> GetFavorites()
    {
        lock (_sync)
        {
            var result = new List<RecentFileEntry>();
            foreach (string path in Current.FavoritePaths)
            {
                var recent = Current.RecentFiles.FirstOrDefault(r => string.Equals(r.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (recent != null)
                {
                    result.Add(recent);
                }
                else
                {
                    long size = 0;
                    DateTimeOffset modified = DateTimeOffset.UtcNow;
                    try
                    {
                        if (File.Exists(path))
                        {
                            var fi = new FileInfo(path);
                            size = fi.Length;
                            modified = fi.LastWriteTimeUtc;
                        }
                    }
                    catch
                    {
                        // Ignore stat error.
                    }

                    result.Add(new RecentFileEntry
                    {
                        FilePath = path,
                        FileName = Path.GetFileName(path),
                        FileKind = FileFormatDetector.DetectByExtension(path),
                        LastOpenedUtc = modified,
                        FileSizeBytes = size,
                        IsFavorite = true
                    });
                }
            }
            return result;
        }
    }

    public void ClearRecentFiles()
    {
        lock (_sync)
        {
            Current.RecentFiles.Clear();
            Save();
        }
    }

    private void SyncFavoriteFlags()
    {
        var favSet = new HashSet<string>(Current.FavoritePaths, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Current.RecentFiles)
        {
            entry.IsFavorite = favSet.Contains(entry.FilePath);
        }
    }
}
