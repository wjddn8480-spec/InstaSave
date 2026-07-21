namespace InstaSave.Services;

public static class LogService
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureDirectories();
            lock (Sync)
            {
                File.AppendAllText(
                    AppPaths.LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그 기록 실패는 앱 실행을 막지 않습니다.
        }
    }
}
