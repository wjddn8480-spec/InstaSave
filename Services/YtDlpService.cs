using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InstaSave.Models;

namespace InstaSave.Services;

public sealed record MediaInfo(
    string Title,
    string Author,
    string MediaId,
    string ThumbnailUrl,
    string Description,
    double? DurationSeconds,
    DateTime? UploadDate,
    int MediaCount,
    string AccessText,
    string MediaKind = "영상")
{
    public string DurationText => DurationSeconds is > 0
        ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
        : "확인 불가";

    public string UploadDateText => UploadDate?.ToString("yyyy-MM-dd") ?? "확인 불가";
    public string MediaCountText => MediaCount > 1 ? $"{MediaCount}개 미디어" : $"{MediaKind} 1개";
}

public sealed record DownloadProgress(double Percent, string Speed, string Eta, string Detail);
public sealed record DownloadResult(IReadOnlyList<string> OutputFiles, bool WasAlreadyDownloaded);

public class YtDlpException : InvalidOperationException
{
    public YtDlpException(string message, bool retryable) : base(message)
    {
        Retryable = retryable;
    }

    public bool Retryable { get; }
}

public sealed class BrowserCookieLockedException : YtDlpException
{
    public BrowserCookieLockedException(string message) : base(message, false)
    {
    }
}

public sealed class BrowserCookieDecryptException : YtDlpException
{
    public BrowserCookieDecryptException(string message) : base(message, false)
    {
    }
}

public sealed class BrowserCookieProfileNotFoundException : YtDlpException
{
    public BrowserCookieProfileNotFoundException(string message) : base(message, false)
    {
    }
}

public sealed class YtDlpService
{
    private static readonly Regex PercentRegex = new(@"(?<value>\d+(?:\.\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex InvalidTemplateCharacters = new("[<>:\"/\\\\|?*]", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();

    public void CancelAll()
    {
        foreach (var process in _activeProcesses.Values)
            TryKill(process);
    }

    public async Task<MediaInfo> AnalyzeAsync(
        string url,
        string cookieBrowser,
        string cookieProfile,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--ignore-config",
            "--no-colors",
            "--no-warnings",
            "--skip-download",
            "--dump-single-json"
        };

        AddCookieArguments(arguments, cookieBrowser, cookieProfile);
        arguments.Add(url);

        var result = await RunCaptureAsync(arguments, cancellationToken);
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        var primary = root;
        var mediaCount = 1;

        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var validEntries = entries.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .ToArray();
            mediaCount = Math.Max(1, validEntries.Length);
            if (validEntries.Length > 0)
                primary = validEntries[0];
        }
        else if (root.TryGetProperty("playlist_count", out var countElement) && countElement.TryGetInt32(out var count))
        {
            mediaCount = Math.Max(1, count);
        }

        var title = FirstNonEmpty(GetString(root, "title"), GetString(primary, "title"), "Instagram 게시물");
        var author = FirstNonEmpty(
            GetString(root, "uploader"),
            GetString(root, "channel"),
            GetString(primary, "uploader"),
            GetString(primary, "channel"),
            "Instagram");
        var mediaId = FirstNonEmpty(GetString(root, "id"), GetString(primary, "id"));
        var thumbnail = FirstNonEmpty(GetString(root, "thumbnail"), GetString(primary, "thumbnail"), GetLastThumbnail(root), GetLastThumbnail(primary));
        var description = FirstNonEmpty(GetString(root, "description"), GetString(primary, "description"));
        var duration = GetDouble(root, "duration") ?? GetDouble(primary, "duration");
        var uploadDate = ParseUploadDate(FirstNonEmpty(GetString(root, "upload_date"), GetString(primary, "upload_date")));
        var accessText = BuildCookieAccessText(cookieBrowser);

        return new MediaInfo(title, author, mediaId, thumbnail, description, duration, uploadDate, mediaCount, accessText);
    }

    public Task<MediaInfo> CheckCookieAccessAsync(
        string url,
        string cookieBrowser,
        string cookieProfile,
        CancellationToken cancellationToken) =>
        AnalyzeAsync(url, cookieBrowser, cookieProfile, cancellationToken);

    public async Task<DownloadResult> DownloadAsync(
        DownloadItem item,
        AppSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.OutputDirectory);

        var outputFiles = new List<string>();
        var alreadyDownloaded = false;
        var arguments = BuildDownloadArguments(item, settings);
        var startInfo = CreateStartInfo(arguments);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();
        _activeProcesses[process.Id] = process;

        try
        {
            var stdoutTask = PumpAsync(process.StandardOutput, line =>
            {
                log?.Invoke(line);
                if (line.StartsWith("__PROGRESS__|", StringComparison.Ordinal))
                {
                    var parts = line.Split('|');
                    var percent = parts.Length > 1 ? ParsePercent(parts[1]) : 0;
                    var speed = parts.Length > 2 ? CleanValue(parts[2]) : string.Empty;
                    var eta = parts.Length > 3 ? CleanValue(parts[3]) : string.Empty;
                    progress.Report(new DownloadProgress(percent, speed, eta, "파일을 내려받는 중"));
                }
                else if (line.StartsWith("__FILE__:", StringComparison.Ordinal))
                {
                    var path = line["__FILE__:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        lock (outputFiles)
                            outputFiles.Add(path);
                    }
                }
                else if (line.StartsWith("__TITLE__:", StringComparison.Ordinal))
                {
                    var title = line["__TITLE__:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(title))
                        item.Title = title;
                }
                else if (line.Contains("already been recorded in the archive", StringComparison.OrdinalIgnoreCase))
                {
                    alreadyDownloaded = true;
                }
            }, cancellationToken);

            var stderr = new StringBuilder();
            var stderrTask = PumpAsync(process.StandardError, line =>
            {
                log?.Invoke(line);
                if (line.Contains("already been recorded in the archive", StringComparison.OrdinalIgnoreCase))
                    alreadyDownloaded = true;
                if (!string.IsNullOrWhiteSpace(line))
                    stderr.AppendLine(line);
            }, cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            if (process.ExitCode != 0)
            {
                var rawError = stderr.ToString();
                if (IsCookieDpapiError(rawError))
                    throw new BrowserCookieDecryptException(BuildCookieDecryptMessage());
                if (IsFirefoxCookieProfileNotFoundError(rawError))
                    throw new BrowserCookieProfileNotFoundException(BuildFirefoxCookieProfileNotFoundMessage());
                if (IsCookieDatabaseLockedError(rawError))
                    throw new BrowserCookieLockedException(BuildCookieLockedMessage());

                var error = SimplifyError(rawError);
                throw new YtDlpException(error.Message, error.Retryable);
            }

            lock (outputFiles)
            {
                return new DownloadResult(
                    outputFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    alreadyDownloaded);
            }
        }
        finally
        {
            _activeProcesses.TryRemove(process.Id, out _);
        }
    }

    private static List<string> BuildDownloadArguments(DownloadItem item, AppSettings settings)
    {
        var retryCount = Math.Clamp(settings.AutoRetryCount + 2, 2, 10);
        var retryDelay = Math.Clamp(settings.AutoRetryDelaySeconds, 1, 60);
        var arguments = new List<string>
        {
            "--ignore-config",
            "--no-colors",
            "--newline",
            "--windows-filenames",
            "--trim-filenames", "180",
            "--continue",
            "--part",
            "--no-abort-on-error",
            "--retries", retryCount.ToString(CultureInfo.InvariantCulture),
            "--fragment-retries", retryCount.ToString(CultureInfo.InvariantCulture),
            "--extractor-retries", retryCount.ToString(CultureInfo.InvariantCulture),
            "--file-access-retries", "3",
            "--retry-sleep", retryDelay.ToString(CultureInfo.InvariantCulture),
            "--socket-timeout", "30",
            "--concurrent-fragments", "2",
            "--progress-template", "download:__PROGRESS__|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
            "--print", "before_dl:__TITLE__:%(title)s",
            "--print", "after_move:__FILE__:%(filepath)s",
            "--format", GetFormat(settings.Quality),
            "--output", Path.Combine(settings.OutputDirectory, GetOutputTemplate(settings))
        };

        arguments.Add(settings.DownloadAllMedia ? "--yes-playlist" : "--no-playlist");

        if (item.AllowDuplicate)
        {
            arguments.Add("--no-download-archive");
            arguments.Add("--force-overwrites");
        }
        else if (settings.PreventDuplicateDownloads)
        {
            arguments.Add("--download-archive");
            arguments.Add(AppPaths.YtDlpArchivePath);
            arguments.Add("--no-overwrites");
        }
        else
        {
            arguments.Add("--no-overwrites");
        }

        if (settings.SaveThumbnail)
            arguments.Add("--write-thumbnail");

        var ffmpegLocation = ResolveFfmpegLocation(settings.FfmpegDirectory);
        if (!string.IsNullOrWhiteSpace(ffmpegLocation))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(ffmpegLocation);
        }

        AddCookieArguments(
            arguments,
            settings.CookieBrowser,
            settings.CookieBrowser == "file" ? settings.CookieFilePath : settings.CookieProfile);
        arguments.Add(item.Url);
        return arguments;
    }

    private static string GetFormat(string quality) => quality switch
    {
        "1080" => "best[ext=mp4][height<=1080]/best[height<=1080]/best[ext=mp4]/best",
        "720" => "best[ext=mp4][height<=720]/best[height<=720]/best[ext=mp4]/best",
        "480" => "best[ext=mp4][height<=480]/best[height<=480]/best[ext=mp4]/best",
        _ => "best[ext=mp4]/best"
    };

    private static string GetOutputTemplate(AppSettings settings) => settings.FileNamePreset switch
    {
        "date-uploader-title" => "%(upload_date>%Y-%m-%d)s_%(uploader).60B_%(title).100B_%(id)s.%(ext)s",
        "title-id" => "%(title).120B_%(id)s.%(ext)s",
        "date-title" => "%(upload_date>%Y-%m-%d)s_%(title).120B_%(id)s.%(ext)s",
        "uploader-folder" => "%(uploader).60B\\%(upload_date>%Y-%m-%d)s_%(title).100B_%(id)s.%(ext)s",
        "id" => "%(id)s.%(ext)s",
        "custom" => ConvertCustomTemplate(settings.CustomFileNameTemplate),
        _ => "%(uploader).60B_%(upload_date>%Y-%m-%d)s_%(id)s.%(ext)s"
    };

    private static string ConvertCustomTemplate(string value)
    {
        var template = string.IsNullOrWhiteSpace(value) ? "{uploader}_{date}_{id}" : value.Trim();
        template = InvalidTemplateCharacters.Replace(template, "_");
        template = template
            .Replace("{uploader}", "%(uploader).60B", StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", "%(upload_date>%Y-%m-%d)s", StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", "%(id)s", StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", "%(title).120B", StringComparison.OrdinalIgnoreCase)
            .Replace("{resolution}", "%(resolution)s", StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", "%(playlist_index)03d", StringComparison.OrdinalIgnoreCase);

        if (!template.Contains("%(id)", StringComparison.Ordinal) &&
            !template.Contains("%(playlist_index)", StringComparison.Ordinal))
        {
            template += "_%(id)s";
        }

        return template + ".%(ext)s";
    }

    public static string BuildBrowserSpec(string cookieBrowser, string cookieProfile)
    {
        if (string.IsNullOrWhiteSpace(cookieBrowser) ||
            cookieBrowser.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            cookieBrowser.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            cookieBrowser.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var profile = cookieProfile?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(profile)
            ? cookieBrowser
            : $"{cookieBrowser}:{profile}";
    }

    private static void AddCookieArguments(List<string> arguments, string cookieBrowser, string cookieProfile)
    {
        if (cookieBrowser.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            var cookieFile = cookieProfile?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cookieFile))
            {
                arguments.Add("--cookies");
                arguments.Add(cookieFile);
            }
            return;
        }

        var browserSpec = BuildBrowserSpec(cookieBrowser, cookieProfile);
        if (string.IsNullOrWhiteSpace(browserSpec))
            return;

        arguments.Add("--cookies-from-browser");
        arguments.Add(browserSpec);
    }

    private static string ResolveFfmpegLocation(string configuredDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredDirectory) && Directory.Exists(configuredDirectory))
            return configuredDirectory;

        return File.Exists(Path.Combine(AppPaths.ToolsDirectory, "ffmpeg.exe"))
            ? AppPaths.ToolsDirectory
            : string.Empty;
    }

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = AppPaths.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private async Task<string> RunCaptureAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        process.Start();
        _activeProcesses[process.Id] = process;

        try
        {
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
            {
                if (IsCookieDpapiError(stderr))
                    throw new BrowserCookieDecryptException(BuildCookieDecryptMessage());
                if (IsFirefoxCookieProfileNotFoundError(stderr))
                    throw new BrowserCookieProfileNotFoundException(BuildFirefoxCookieProfileNotFoundMessage());
                if (IsCookieDatabaseLockedError(stderr))
                    throw new BrowserCookieLockedException(BuildCookieLockedMessage());

                var error = SimplifyError(stderr);
                throw new YtDlpException(error.Message, error.Retryable);
            }

            return stdout;
        }
        finally
        {
            _activeProcesses.TryRemove(process.Id, out _);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;
            onLine(line);
        }
    }

    private static double ParsePercent(string value)
    {
        var match = PercentRegex.Match(value);
        if (!match.Success)
            return 0;

        return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static string CleanValue(string value)
    {
        var cleaned = value.Trim();
        return cleaned.Equals("NA", StringComparison.OrdinalIgnoreCase) ? string.Empty : cleaned;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }

    private static string GetLastThumbnail(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        string result = string.Empty;
        foreach (var thumbnail in thumbnails.EnumerateArray())
        {
            var url = GetString(thumbnail, "url");
            if (!string.IsNullOrWhiteSpace(url))
                result = url;
        }

        return result;
    }

    private static DateTime? ParseUploadDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    public static bool IsCookieDpapiError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Contains("failed to decrypt with dpapi", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("issues/10927", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("app_bound_encrypted_key", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("app-bound encryption", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildCookieAccessText(string cookieBrowser) => cookieBrowser.ToLowerInvariant() switch
    {
        "none" => "공개 접근으로 분석됨",
        "file" => "cookies.txt 파일로 분석됨",
        _ => $"{cookieBrowser} 브라우저 쿠키로 분석됨"
    };

    public static bool IsFirefoxCookieProfileNotFoundError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return raw.Contains("could not find firefox cookies database", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("firefox cookies database", StringComparison.OrdinalIgnoreCase) &&
               raw.Contains("could not find", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCookieDatabaseLockedError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var mentionsCookie = raw.Contains("cookie", StringComparison.OrdinalIgnoreCase);
        if (!mentionsCookie)
            return false;

        return raw.Contains("Could not copy", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
               raw.Contains("cookie database", StringComparison.OrdinalIgnoreCase) &&
               (raw.Contains("access", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("open", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCookieLockedMessage() =>
        "선택한 브라우저가 쿠키 데이터베이스를 사용 중입니다. InstaSave에서 브라우저를 안전하게 종료한 뒤 자동으로 다시 시도할 수 있습니다.";

    private static string BuildCookieDecryptMessage() =>
        "선택한 Chromium 브라우저의 쿠키가 Windows App-Bound Encryption으로 보호되어 yt-dlp가 DPAPI로 복호화하지 못했습니다. 브라우저를 종료해도 해결되지 않을 수 있습니다. 설정에서 'cookies.txt 파일 직접 선택'을 사용하거나 Firefox 쿠키를 선택해 주세요.";

    private static string BuildFirefoxCookieProfileNotFoundMessage() =>
        "선택한 Firefox 프로필에서 cookies.sqlite를 찾지 못했습니다. Firefox 프로필 이름은 Default가 아니라 임의 문자.default-release 형태입니다. InstaSave가 실제 Firefox 프로필을 다시 검색해 자동으로 선택합니다.";

    private static (string Message, bool Retryable) SimplifyError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("다운로드 엔진에서 알 수 없는 오류가 발생했습니다.", true);

        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var errorLine = lines.LastOrDefault(x => x.Contains("ERROR:", StringComparison.OrdinalIgnoreCase));
        var message = errorLine ?? lines.LastOrDefault() ?? raw.Trim();

        if (IsCookieDpapiError(raw))
        {
            return (BuildCookieDecryptMessage(), false);
        }

        if (IsFirefoxCookieProfileNotFoundError(raw))
        {
            return (BuildFirefoxCookieProfileNotFoundMessage(), false);
        }

        if (message.Contains("Could not copy", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase))
        {
            return ("브라우저 쿠키 데이터베이스가 잠겨 있습니다. 설정의 선택 브라우저 종료 버튼을 사용하거나, 잠금 감지 창에서 자동 종료 후 재시도를 선택해 주세요.", false);
        }

        if (message.Contains("login required", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("empty media response", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("requested content is not available", StringComparison.OrdinalIgnoreCase))
        {
            return ("Instagram 로그인 또는 접근 권한이 필요합니다. 올바른 브라우저와 프로필을 선택하고 Instagram에 로그인한 뒤 다시 시도해 주세요.", false);
        }

        if (message.Contains("private", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("unsupported url", StringComparison.OrdinalIgnoreCase))
        {
            return ("게시물을 찾을 수 없거나 비공개 콘텐츠입니다. URL과 계정 접근 권한을 확인해 주세요.", false);
        }

        if (message.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return ("Instagram 요청이 일시적으로 제한되었거나 네트워크 연결이 불안정합니다.", true);
        }

        return (message.Replace("ERROR:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(), true);
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
