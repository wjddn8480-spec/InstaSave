namespace InstaSave.Models;

public sealed class AppSettings
{
    public string LanguageMode { get; set; } = "auto";
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "InstaSave");

    public string Quality { get; set; } = "best";
    public string CookieBrowser { get; set; } = "auto";
    public string CookieProfile { get; set; } = "Default";
    public string CookieFilePath { get; set; } = string.Empty;
    public bool DownloadAllMedia { get; set; } = true;
    public bool DownloadPhotos { get; set; } = true;
    public bool SaveThumbnail { get; set; }
    public bool AutoStartQueue { get; set; }
    public bool AutoPasteClipboardUrl { get; set; } = true;
    public bool ClipboardMonitoringEnabled { get; set; }
    public bool PreventDuplicateDownloads { get; set; } = true;
    public bool AutoRetryEnabled { get; set; } = true;
    public int AutoRetryCount { get; set; } = 2;
    public int AutoRetryDelaySeconds { get; set; } = 5;
    public string FileNamePreset { get; set; } = "uploader-date-id";
    public string CustomFileNameTemplate { get; set; } = "{uploader}_{date}_{id}";
    public string FfmpegDirectory { get; set; } = string.Empty;
}
