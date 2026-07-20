using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace InstaSave.Services;

public sealed record FfmpegInfo(bool IsAvailable, string DisplayName, string ExecutablePath);

public sealed class ToolBootstrapService
{
    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string GalleryDlLatestReleaseApi = "https://codeberg.org/api/v1/repos/mikf/gallery-dl/releases/latest";
    private readonly HttpClient _httpClient;

    public ToolBootstrapService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("InstaSave/1.0.0");
    }

    public bool HasBackup => File.Exists(AppPaths.YtDlpBackupPath);
    public bool HasPhotoBackup => File.Exists(AppPaths.GalleryDlBackupPath);

    public async Task EnsureInstalledAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        if (!IsValidEngine(AppPaths.YtDlpPath))
            await DownloadExecutableAsync(YtDlpDownloadUrl, AppPaths.YtDlpPath, progress, cancellationToken);
    }

    public async Task ReinstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        BackupEngine(AppPaths.YtDlpPath, AppPaths.YtDlpBackupPath);
        await DownloadExecutableAsync(YtDlpDownloadUrl, AppPaths.YtDlpPath, progress, cancellationToken);
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.YtDlpPath))
            return "설치되지 않음";

        return (await RunAsync(AppPaths.YtDlpPath, ["--version"], cancellationToken)).Trim();
    }

    public async Task<string> UpdateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(cancellationToken: cancellationToken);
        BackupEngine(AppPaths.YtDlpPath, AppPaths.YtDlpBackupPath);
        return await RunAsync(AppPaths.YtDlpPath, ["-U"], cancellationToken);
    }

    public async Task RestoreBackupAsync(CancellationToken cancellationToken = default)
    {
        RestoreEngine(AppPaths.YtDlpPath, AppPaths.YtDlpBackupPath, "yt-dlp");
        _ = await GetVersionAsync(cancellationToken);
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(cancellationToken: cancellationToken);
        _ = await RunAsync(AppPaths.YtDlpPath, ["--rm-cache-dir"], cancellationToken);
        ClearLocalCacheDirectory();
    }

    public async Task EnsurePhotoEngineInstalledAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        if (IsValidEngine(AppPaths.GalleryDlPath))
            return;

        var url = await ResolveGalleryDlDownloadUrlAsync(cancellationToken);
        await DownloadExecutableAsync(url, AppPaths.GalleryDlPath, progress, cancellationToken);
    }

    public async Task ReinstallPhotoEngineAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        BackupEngine(AppPaths.GalleryDlPath, AppPaths.GalleryDlBackupPath);
        var url = await ResolveGalleryDlDownloadUrlAsync(cancellationToken);
        await DownloadExecutableAsync(url, AppPaths.GalleryDlPath, progress, cancellationToken);
    }

    public async Task<string> GetPhotoEngineVersionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.GalleryDlPath))
            return "설치되지 않음";

        return (await RunAsync(AppPaths.GalleryDlPath, ["--version"], cancellationToken)).Trim();
    }

    public async Task<string> UpdatePhotoEngineAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePhotoEngineInstalledAsync(cancellationToken: cancellationToken);
        BackupEngine(AppPaths.GalleryDlPath, AppPaths.GalleryDlBackupPath);
        return await RunAsync(AppPaths.GalleryDlPath, ["-U"], cancellationToken);
    }

    public async Task RestorePhotoEngineBackupAsync(CancellationToken cancellationToken = default)
    {
        RestoreEngine(AppPaths.GalleryDlPath, AppPaths.GalleryDlBackupPath, "gallery-dl");
        _ = await GetPhotoEngineVersionAsync(cancellationToken);
    }

    public async Task ClearPhotoEngineCacheAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePhotoEngineInstalledAsync(cancellationToken: cancellationToken);
        _ = await RunAsync(AppPaths.GalleryDlPath, ["--cache-clear", "ALL"], cancellationToken);
    }

    public async Task<FfmpegInfo> GetFfmpegInfoAsync(string configuredDirectory, CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
            candidates.Add(Path.Combine(configuredDirectory, "ffmpeg.exe"));

        candidates.Add(Path.Combine(AppPaths.ToolsDirectory, "ffmpeg.exe"));
        candidates.Add("ffmpeg.exe");

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var output = await RunAsync(candidate, ["-version"], cancellationToken);
                var firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "FFmpeg 사용 가능";
                return new FfmpegInfo(true, firstLine.Trim(), candidate);
            }
            catch
            {
                // 다음 후보를 확인합니다.
            }
        }

        return new FfmpegInfo(false, "FFmpeg를 찾지 못했습니다.", string.Empty);
    }

    private async Task<string> ResolveGalleryDlDownloadUrlAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(GalleryDlLatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (!string.Equals(name, "gallery-dl.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (asset.TryGetProperty("browser_download_url", out var browserUrl) &&
                    !string.IsNullOrWhiteSpace(browserUrl.GetString()))
                {
                    return browserUrl.GetString()!;
                }

                if (asset.TryGetProperty("url", out var urlElement) &&
                    !string.IsNullOrWhiteSpace(urlElement.GetString()))
                {
                    return urlElement.GetString()!;
                }
            }
        }

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!string.IsNullOrWhiteSpace(tag))
            return $"https://codeberg.org/mikf/gallery-dl/releases/download/{tag}/gallery-dl.exe";

        throw new InvalidDataException("gallery-dl 최신 Windows 실행 파일 주소를 확인하지 못했습니다.");
    }

    private async Task DownloadExecutableAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = destinationPath + ".download";
        if (File.Exists(tempPath))
            File.Delete(tempPath);

        using (var response = await _httpClient.GetAsync(
                   downloadUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (total is > 0)
                    progress?.Report(readTotal * 100d / total.Value);
            }

            await destination.FlushAsync(cancellationToken);
        }

        if (!IsValidEngine(tempPath))
            throw new InvalidDataException("다운로드한 엔진 파일이 올바른 Windows 실행 파일이 아닙니다.");

        File.Move(tempPath, destinationPath, true);
    }

    private static void BackupEngine(string sourcePath, string backupPath)
    {
        if (IsValidEngine(sourcePath))
            File.Copy(sourcePath, backupPath, true);
    }

    private static void RestoreEngine(string enginePath, string backupPath, string displayName)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"복원할 이전 {displayName} 엔진이 없습니다.");

        if (File.Exists(enginePath))
            File.Copy(enginePath, enginePath + ".failed", true);

        File.Copy(backupPath, enginePath, true);
    }

    private static bool IsValidEngine(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 500_000 && HasPortableExecutableHeader(path);

    private static void ClearLocalCacheDirectory()
    {
        if (!Directory.Exists(AppPaths.CacheDirectory))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(AppPaths.CacheDirectory))
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, true);
            else
                File.Delete(entry);
        }
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    private static bool HasPortableExecutableHeader(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            // 이미 종료된 프로세스일 수 있습니다.
        }
    }
}
