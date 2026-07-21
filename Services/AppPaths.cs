namespace InstaSave.Services;

public static class AppPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InstaSave");

    public static string ToolsDirectory { get; } = Path.Combine(RootDirectory, "tools");
    public static string CacheDirectory { get; } = Path.Combine(RootDirectory, "cache");
    public static string YtDlpPath { get; } = Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public static string YtDlpBackupPath { get; } = Path.Combine(ToolsDirectory, "yt-dlp.previous.exe");
    public static string GalleryDlPath { get; } = Path.Combine(ToolsDirectory, "gallery-dl.exe");
    public static string GalleryDlBackupPath { get; } = Path.Combine(ToolsDirectory, "gallery-dl.previous.exe");
    public static string SettingsPath { get; } = Path.Combine(RootDirectory, "settings.json");
    public static string HistoryPath { get; } = Path.Combine(RootDirectory, "history.json");
    public static string DownloadRecordsPath { get; } = Path.Combine(RootDirectory, "download-records.json");
    public static string YtDlpArchivePath { get; } = Path.Combine(RootDirectory, "yt-dlp-archive.txt");
    public static string GalleryDlArchivePath { get; } = Path.Combine(RootDirectory, "gallery-dl-archive.sqlite3");
    public static string LogPath { get; } = Path.Combine(RootDirectory, "InstaSave.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ToolsDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
