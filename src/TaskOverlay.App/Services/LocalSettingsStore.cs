using System.IO;
using System.Text.Json;
using TaskOverlay.Core.Models;

namespace TaskOverlay.App.Services;

public sealed class LocalSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly bool _usesDefaultSettingsDirectory;
    private string _settingsPath = string.Empty;
    private string _backupPath = string.Empty;
    private string _tempPath = string.Empty;

    public LocalSettingsStore(string? settingsDirectory = null)
    {
        _usesDefaultSettingsDirectory = settingsDirectory is null;
        UseDirectory(settingsDirectory ?? ResolvePreferredSettingsDirectory());
    }

    public AppSettings Current { get; private set; } = new();

    public string SettingsFilePath => _settingsPath;

    public AppSettings Load()
    {
        if (_usesDefaultSettingsDirectory)
        {
            TryMigrateLegacySettings();
        }

        var loadedPrimary = TryLoad(_settingsPath, out var settings);
        if (!loadedPrimary && !TryLoad(_backupPath, out settings))
        {
            Current = Normalize(new AppSettings());
            Save(Current);
            return Current;
        }

        Current = Normalize(settings);
        Save(Current);
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = Normalize(settings);
        try
        {
            WriteCurrent();
        }
        catch (UnauthorizedAccessException)
        {
            UseWorkspaceFallback();
            WriteCurrent();
        }
        catch (IOException)
        {
            UseWorkspaceFallback();
            WriteCurrent();
        }
    }

    private static string ResolvePreferredSettingsDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable("TASKOVERLAY_SETTINGS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        return ResolveWorkspaceFallbackDirectory();
    }

    private void UseWorkspaceFallback()
    {
        UseDirectory(ResolveWorkspaceFallbackDirectory());
    }

    private void UseDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        _backupPath = Path.Combine(directory, "settings.bak.json");
        _tempPath = Path.Combine(directory, "settings.tmp.json");
    }

    private void WriteCurrent()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        try
        {
            File.WriteAllText(_tempPath, json);
            if (File.Exists(_settingsPath))
            {
                File.Replace(_tempPath, _settingsPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_tempPath, _settingsPath);
            }
        }
        finally
        {
            TryDeleteTempFile();
        }
    }

    private void TryDeleteTempFile()
    {
        try
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }
        catch (IOException)
        {
            // A stale temp file is safe to replace during the next save.
        }
    }

    private static bool TryLoad(string path, out AppSettings settings)
    {
        settings = new AppSettings();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Hotkey = string.IsNullOrWhiteSpace(settings.Hotkey) ? AppSettings.DefaultHotkey : settings.Hotkey.Trim();
        settings.OverlayOpacity = double.IsFinite(settings.OverlayOpacity)
            ? Math.Clamp(settings.OverlayOpacity, 0.25, 1.0)
            : 0.78;
        settings.OverlayLeft = double.IsFinite(settings.OverlayLeft) ? settings.OverlayLeft : 80;
        settings.OverlayTop = double.IsFinite(settings.OverlayTop) ? settings.OverlayTop : 80;
        settings.OverlayWidth = double.IsFinite(settings.OverlayWidth) ? Math.Clamp(settings.OverlayWidth, 280, 1200) : 320;
        settings.OverlayHeight = double.IsFinite(settings.OverlayHeight) ? Math.Clamp(settings.OverlayHeight, 120, 1400) : 180;
        settings.StorageBackend = Enum.IsDefined(settings.StorageBackend) ? settings.StorageBackend : TaskStorageBackend.Json;
        settings.MySqlHost = string.IsNullOrWhiteSpace(settings.MySqlHost) ? "localhost" : settings.MySqlHost.Trim();
        settings.MySqlPort = settings.MySqlPort == 0 ? 3306u : settings.MySqlPort;
        settings.MySqlDatabase = string.IsNullOrWhiteSpace(settings.MySqlDatabase) ? "task_overlay" : settings.MySqlDatabase.Trim();
        settings.MySqlUser = string.IsNullOrWhiteSpace(settings.MySqlUser) ? "root" : settings.MySqlUser.Trim();
        settings.MySqlPassword ??= string.Empty;
        settings.ApiPort = settings.ApiPort is >= 1024 and <= 65535 ? settings.ApiPort : 43127;
        settings.ApiToken = string.IsNullOrWhiteSpace(settings.ApiToken)
            ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()
            : settings.ApiToken.Trim();
        return settings;
    }

    private static string ResolveWorkspaceFallbackDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    private void TryMigrateLegacySettings()
    {
        if (File.Exists(_settingsPath))
        {
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            return;
        }

        var legacyDirectory = Path.Combine(appData, "TaskOverlay");
        if (string.Equals(Path.GetFullPath(legacyDirectory), Path.GetFullPath(Path.GetDirectoryName(_settingsPath)!), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CopyIfExists(Path.Combine(legacyDirectory, "settings.json"), _settingsPath);
        CopyIfExists(Path.Combine(legacyDirectory, "settings.bak.json"), _backupPath);
    }

    private static void CopyIfExists(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: false);
    }
}
