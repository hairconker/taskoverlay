namespace TaskOverlay.Core.Models;

public sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+`";

    public string Hotkey { get; set; } = DefaultHotkey;
    public bool IsTopmost { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.78;
    public double OverlayLeft { get; set; } = 80;
    public double OverlayTop { get; set; } = 80;
    public double OverlayWidth { get; set; } = 320;
    public double OverlayHeight { get; set; } = 180;
    public bool StartWithWindows { get; set; }
    public TaskStorageBackend StorageBackend { get; set; } = TaskStorageBackend.Json;
    public string MySqlHost { get; set; } = "localhost";
    public uint MySqlPort { get; set; } = 3306;
    public string MySqlDatabase { get; set; } = "task_overlay";
    public string MySqlUser { get; set; } = "root";
    public string MySqlPassword { get; set; } = string.Empty;
    public bool ApiEnabled { get; set; } = true;
    public int ApiPort { get; set; } = 43127;
    public string ApiToken { get; set; } = string.Empty;
}
