using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Forms;
using TaskOverlay.Core.Models;
using TaskOverlay.Infrastructure.SystemIntegration;

var options = BridgeOptions.Parse(args);
var settings = LoadSettings(options);
var hotkeyGesture = options.Hotkey ?? settings.Hotkey;
var baseUrl = options.Url ?? $"http://127.0.0.1:{settings.ApiPort}/";
var token = options.Token ?? settings.ApiToken;

if (string.IsNullOrWhiteSpace(token))
{
    MessageBox.Show("找不到 TaskOverlay API 令牌。请用 --token 指定，或设置 TASKOVERLAY_SETTINGS_DIR。", "TaskOverlay Hotkey Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return 1;
}

using var client = new HttpClient
{
    BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(3)
};
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

using var hotkeyService = new KeyboardChordHotkeyService();
using var gate = new SemaphoreSlim(1, 1);
NotifyIcon? notifyIcon = null;
hotkeyService.HotkeyPressed += async (_, _) =>
{
    if (!await gate.WaitAsync(0))
    {
        return;
    }

    try
    {
        using var response = await client.PostAsync("api/overlay/toggle-edit", content: null);
        if (!response.IsSuccessStatusCode)
        {
            ShowTransientError($"TaskOverlay 未接受快捷键请求：{(int)response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        ShowTransientError($"无法连接 TaskOverlay：{ex.Message}");
    }
    finally
    {
        gate.Release();
    }
};

if (!hotkeyService.Register(hotkeyGesture))
{
    MessageBox.Show($"无法监听快捷键：{hotkeyGesture}", "TaskOverlay Hotkey Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return 1;
}

notifyIcon = new NotifyIcon
{
    Icon = System.Drawing.SystemIcons.Application,
    Text = $"TaskOverlay Hotkey Bridge: {hotkeyGesture}",
    Visible = true,
    ContextMenuStrip = BuildMenu()
};
notifyIcon.ShowBalloonTip(3000, "TaskOverlay Hotkey Bridge", $"正在监听：{hotkeyGesture}", ToolTipIcon.Info);
Application.Run();
notifyIcon.Dispose();
return 0;

ContextMenuStrip BuildMenu()
{
    var menu = new ContextMenuStrip();
    menu.Items.Add($"快捷键：{hotkeyGesture}", null, (_, _) => { });
    menu.Items.Add("测试切换编辑态", null, async (_, _) =>
    {
        try
        {
            await client.PostAsync("api/overlay/toggle-edit", content: null);
        }
        catch (Exception ex)
        {
            ShowTransientError(ex.Message);
        }
    });
    menu.Items.Add("退出", null, (_, _) => Application.Exit());
    return menu;
}

void ShowTransientError(string message)
{
    notifyIcon?.ShowBalloonTip(3000, "TaskOverlay Hotkey Bridge", message, ToolTipIcon.Warning);
}

static AppSettings LoadSettings(BridgeOptions options)
{
    var settingsPath = ResolveSettingsPath(options);
    if (settingsPath is not null && File.Exists(settingsPath))
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    return new AppSettings();
}

static string? ResolveSettingsPath(BridgeOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.SettingsPath))
    {
        return Path.GetFullPath(options.SettingsPath);
    }

    var settingsDirectory = options.SettingsDirectory ?? Environment.GetEnvironmentVariable("TASKOVERLAY_SETTINGS_DIR");
    if (!string.IsNullOrWhiteSpace(settingsDirectory))
    {
        return Path.Combine(Path.GetFullPath(settingsDirectory), "settings.json");
    }

    var localSettings = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
    if (File.Exists(localSettings))
    {
        return localSettings;
    }

    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var appRelease = Path.Combine(current.FullName, "src", "TaskOverlay.App", "bin", "Release", "net8.0-windows", "data", "settings.json");
        if (File.Exists(appRelease))
        {
            return appRelease;
        }

        var appDebug = Path.Combine(current.FullName, "src", "TaskOverlay.App", "bin", "Debug", "net8.0-windows", "data", "settings.json");
        if (File.Exists(appDebug))
        {
            return appDebug;
        }

        current = current.Parent;
    }

    return null;
}

sealed class BridgeOptions
{
    public string? Url { get; private init; }
    public string? Token { get; private init; }
    public string? Hotkey { get; private init; }
    public string? SettingsDirectory { get; private init; }
    public string? SettingsPath { get; private init; }

    public static BridgeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            values[key] = value;
        }

        return new BridgeOptions
        {
            Url = values.GetValueOrDefault("url") ?? Environment.GetEnvironmentVariable("TASKOVERLAY_URL"),
            Token = values.GetValueOrDefault("token") ?? Environment.GetEnvironmentVariable("TASKOVERLAY_TOKEN"),
            Hotkey = values.GetValueOrDefault("hotkey"),
            SettingsDirectory = values.GetValueOrDefault("settings-dir"),
            SettingsPath = values.GetValueOrDefault("settings")
        };
    }
}
