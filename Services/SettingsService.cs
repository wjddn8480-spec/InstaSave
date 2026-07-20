using System.Text.Json;
using InstaSave.Models;

namespace InstaSave.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            if (!string.Equals(settings.LanguageMode, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(settings.LanguageMode, "ko", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(settings.LanguageMode, "en", StringComparison.OrdinalIgnoreCase))
            {
                settings.LanguageMode = "auto";
            }

            // v1.2.6부터 Chrome과 Microsoft Edge는 쿠키 소스에서 지원하지 않습니다.
            // 이전 설정이 남아 있으면 사용 가능한 브라우저를 찾도록 자동 감지로 전환합니다.
            if (string.Equals(settings.CookieBrowser, "chrome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settings.CookieBrowser, "edge", StringComparison.OrdinalIgnoreCase))
            {
                settings.CookieBrowser = "auto";
                settings.CookieProfile = string.Empty;
            }

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
