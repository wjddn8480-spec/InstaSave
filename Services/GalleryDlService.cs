using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InstaSave.Models;

namespace InstaSave.Services;

public sealed class GalleryDlService
{
    private static readonly string[] ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".heic", ".heif", ".gif", ".jxl"
    ];

    private const string ImageFilter =
        "extension and extension.lower() in ('jpg','jpeg','png','webp','avif','heic','heif','gif','jxl')";

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
        var arguments = CreateCommonArguments(cookieBrowser, cookieProfile);
        arguments.Add("--simulate");
        arguments.Add("--dump-json");
        arguments.Add("--filter");
        arguments.Add(ImageFilter);
        arguments.Add(url);

        var output = await RunCaptureAsync(arguments, cancellationToken);
        var objects = ParseJsonObjects(output).ToArray();
        var images = objects.Where(IsImageObject).ToArray();
        var primary = images.Length > 0 ? images[0] : objects.FirstOrDefault();

        if (primary.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new MediaInfo(
                "Instagram 사진 게시물",
                "Instagram",
                ExtractShortcodeFromUrl(url),
                string.Empty,
                string.Empty,
                null,
                null,
                1,
                BuildAccessText(cookieBrowser),
                "사진");
        }

        var description = FirstNonEmpty(
            FindString(primary, "description"),
            FindString(primary, "caption"),
            FindString(primary, "content"));
        var title = FirstNonEmpty(
            FindString(primary, "title"),
            MakeTitleFromDescription(description),
            "Instagram 사진 게시물");
        var author = FirstNonEmpty(
            FindString(primary, "username"),
            FindString(primary, "owner_name"),
            FindNestedString(primary, "owner", "username"),
            "Instagram");
        var mediaId = FirstNonEmpty(
            FindString(primary, "post_shortcode"),
            FindString(primary, "shortcode"),
            FindString(primary, "post_id"),
            FindString(primary, "id"),
            ExtractShortcodeFromUrl(url));
        var thumbnail = FirstNonEmpty(
            FindString(primary, "url"),
            FindString(primary, "image_url"),
            FindString(primary, "display_url"),
            FindString(primary, "thumbnail"));
        var uploadDate = FindDate(primary);
        var distinctImages = images
            .Select(GetMediaUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var count = Math.Max(1, distinctImages > 0 ? distinctImages : images.Length);

        return new MediaInfo(
            title,
            author,
            mediaId,
            thumbnail,
            description,
            null,
            uploadDate,
            count,
            BuildAccessText(cookieBrowser),
            "사진");
    }

    public async Task<DownloadResult> DownloadPhotosAsync(
        DownloadItem item,
        AppSettings settings,
        IProgress<DownloadProgress> progress,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        var before = SnapshotImages(settings.OutputDirectory);
        var arguments = BuildDownloadArguments(item, settings);
        var startInfo = CreateStartInfo(arguments);
        var alreadyDownloaded = false;
        var downloadedCount = 0;

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
                if (LooksLikeDownloadedFile(line))
                {
                    downloadedCount++;
                    var percent = Math.Min(95, 10 + downloadedCount * 12);
                    progress.Report(new DownloadProgress(percent, string.Empty, string.Empty, $"사진을 내려받는 중 · {downloadedCount}장"));
                }

                if (LooksLikeArchiveSkip(line))
                    alreadyDownloaded = true;
            }, cancellationToken);

            var stderr = new StringBuilder();
            var stderrTask = PumpAsync(process.StandardError, line =>
            {
                log?.Invoke(line);
                if (LooksLikeArchiveSkip(line))
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
                if (YtDlpService.IsCookieDpapiError(rawError))
                    throw new BrowserCookieDecryptException(
                        "선택한 Chromium 브라우저 쿠키가 App-Bound Encryption으로 보호되어 복호화하지 못했습니다. cookies.txt 파일 또는 Firefox 쿠키를 사용해 주세요.");
                if (YtDlpService.IsFirefoxCookieProfileNotFoundError(rawError))
                    throw new BrowserCookieProfileNotFoundException(
                        "선택한 Firefox 프로필에서 cookies.sqlite를 찾지 못했습니다. 실제 Firefox 프로필을 다시 검색합니다.");
                if (YtDlpService.IsCookieDatabaseLockedError(rawError))
                    throw new BrowserCookieLockedException(
                        "선택한 브라우저가 쿠키 데이터베이스를 사용 중입니다. InstaSave에서 브라우저를 안전하게 종료한 뒤 자동으로 다시 시도할 수 있습니다.");

                var error = SimplifyError(rawError);
                throw new YtDlpException(error.Message, error.Retryable);
            }

            progress.Report(new DownloadProgress(100, string.Empty, string.Empty, "사진 다운로드 완료"));
            var after = SnapshotImages(settings.OutputDirectory);
            var outputFiles = after
                .Where(pair => !before.TryGetValue(pair.Key, out var old) || old != pair.Value)
                .Select(pair => pair.Key)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new DownloadResult(outputFiles, alreadyDownloaded);
        }
        finally
        {
            _activeProcesses.TryRemove(process.Id, out _);
        }
    }

    private static List<string> BuildDownloadArguments(DownloadItem item, AppSettings settings)
    {
        var retryCount = Math.Clamp(settings.AutoRetryCount + 2, 2, 10);
        var arguments = CreateCommonArguments(
            settings.CookieBrowser,
            settings.CookieBrowser == "file" ? settings.CookieFilePath : settings.CookieProfile);
        arguments.AddRange(
        [
            "--retries", retryCount.ToString(CultureInfo.InvariantCulture),
            "--http-timeout", "30",
            "--destination", settings.OutputDirectory,
            "--filename", GetOutputTemplate(settings),
            "--filter", ImageFilter
        ]);

        if (!settings.DownloadAllMedia)
        {
            arguments.Add("--range");
            arguments.Add("1");
        }

        if (item.AllowDuplicate)
        {
            arguments.Add("--no-skip");
        }
        else if (settings.PreventDuplicateDownloads)
        {
            arguments.Add("--download-archive");
            arguments.Add(AppPaths.GalleryDlArchivePath);
        }

        arguments.Add(item.Url);
        return arguments;
    }

    private static List<string> CreateCommonArguments(string cookieBrowser, string cookieProfile)
    {
        var arguments = new List<string>
        {
            "--config-ignore",
            "--no-colors",
            "--no-input",
            "--windows-filenames"
        };

        if (cookieBrowser.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            var cookieFile = cookieProfile?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cookieFile))
            {
                arguments.Add("--cookies");
                arguments.Add(cookieFile);
            }
            return arguments;
        }

        var browserSpec = YtDlpService.BuildBrowserSpec(cookieBrowser, cookieProfile);
        if (!string.IsNullOrWhiteSpace(browserSpec))
        {
            arguments.Add("--cookies-from-browser");
            arguments.Add(browserSpec);
        }

        return arguments;
    }

    private static string GetOutputTemplate(AppSettings settings) => settings.FileNamePreset switch
    {
        "date-uploader-title" => "{date:%Y-%m-%d}_{username}_{description[:80]}_{post_shortcode}_{num}.{extension}",
        "title-id" => "{description[:100]}_{post_shortcode}_{num}.{extension}",
        "date-title" => "{date:%Y-%m-%d}_{description[:100]}_{post_shortcode}_{num}.{extension}",
        "uploader-folder" => "{username}\\{date:%Y-%m-%d}_{post_shortcode}_{num}.{extension}",
        "id" => "{post_shortcode}_{num}.{extension}",
        "custom" => ConvertCustomTemplate(settings.CustomFileNameTemplate),
        _ => "{username}_{date:%Y-%m-%d}_{post_shortcode}_{num}.{extension}"
    };

    private static string ConvertCustomTemplate(string value)
    {
        var template = string.IsNullOrWhiteSpace(value) ? "{uploader}_{date}_{id}_{index}" : value.Trim();
        template = InvalidTemplateCharacters.Replace(template, "_");
        template = template
            .Replace("{uploader}", "{username}", StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", "{date:%Y-%m-%d}", StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", "{post_shortcode}", StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", "{description[:100]}", StringComparison.OrdinalIgnoreCase)
            .Replace("{resolution}", "{width}x{height}", StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", "{num}", StringComparison.OrdinalIgnoreCase);

        if (!template.Contains("{post_shortcode}", StringComparison.OrdinalIgnoreCase) &&
            !template.Contains("{num}", StringComparison.OrdinalIgnoreCase))
        {
            template += "_{post_shortcode}_{num}";
        }

        return template + ".{extension}";
    }

    private static Dictionary<string, FileFingerprint> SnapshotImages(string root)
    {
        var result = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
            return result;

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!IsImagePath(path))
                    continue;

                try
                {
                    var info = new FileInfo(path);
                    result[path] = new FileFingerprint(info.Length, info.LastWriteTimeUtc.Ticks);
                }
                catch
                {
                    // 다운로드 도중 잠긴 파일은 다음 스냅샷에서 다시 확인합니다.
                }
            }
        }
        catch
        {
            // 접근 불가능한 하위 폴더는 무시합니다.
        }

        return result;
    }

    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeDownloadedFile(string line) =>
        line.Contains("[download]", StringComparison.OrdinalIgnoreCase) &&
        ImageExtensions.Any(ext => line.Contains(ext, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeArchiveSkip(string line) =>
        line.Contains("archive", StringComparison.OrdinalIgnoreCase) &&
        (line.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
         line.Contains("already", StringComparison.OrdinalIgnoreCase));

    private static ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = AppPaths.GalleryDlPath,
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
                if (YtDlpService.IsCookieDpapiError(stderr))
                    throw new BrowserCookieDecryptException(
                        "선택한 Chromium 브라우저 쿠키가 App-Bound Encryption으로 보호되어 복호화하지 못했습니다. cookies.txt 파일 또는 Firefox 쿠키를 사용해 주세요.");
                if (YtDlpService.IsFirefoxCookieProfileNotFoundError(stderr))
                    throw new BrowserCookieProfileNotFoundException(
                        "선택한 Firefox 프로필에서 cookies.sqlite를 찾지 못했습니다. 실제 Firefox 프로필을 다시 검색합니다.");
                if (YtDlpService.IsCookieDatabaseLockedError(stderr))
                    throw new BrowserCookieLockedException(
                        "선택한 브라우저가 쿠키 데이터베이스를 사용 중입니다. InstaSave에서 브라우저를 안전하게 종료한 뒤 자동으로 다시 시도할 수 있습니다.");

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

    private static IEnumerable<JsonElement> ParseJsonObjects(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var root = TryParseJson(line);
            if (root is null)
                continue;

            foreach (var element in Flatten(root.Value))
                yield return element.Clone();
        }
    }

    private static JsonElement? TryParseJson(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // gallery-dl의 일반 로그 줄은 건너뜁니다.
            return null;
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var child in Flatten(property.Value))
                    yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
            {
                foreach (var child in Flatten(value))
                    yield return child;
            }
        }
    }

    private static bool IsImageObject(JsonElement element)
    {
        var extension = FindString(element, "extension");
        if (!string.IsNullOrWhiteSpace(extension))
            return ImageExtensions.Contains("." + extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase);

        var url = GetMediaUrl(element);
        return !string.IsNullOrWhiteSpace(url) && IsImageUrl(url);
    }

    private static string GetMediaUrl(JsonElement element) => FirstNonEmpty(
        FindString(element, "url"),
        FindString(element, "image_url"),
        FindString(element, "display_url"),
        FindString(element, "download_url"));

    private static bool IsImageUrl(string value)
    {
        var path = value.Split('?', '#')[0];
        return ImageExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static string FindNestedString(JsonElement element, string parent, string child)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(parent, out var nested) ||
            nested.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return FindString(nested, child);
    }

    private static DateTime? FindDate(JsonElement element)
    {
        var value = FirstNonEmpty(
            FindString(element, "date"),
            FindString(element, "created_at"),
            FindString(element, "timestamp"));

        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
            }
            catch
            {
                return null;
            }
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string MakeTitleFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var oneLine = Regex.Replace(description, @"\s+", " ").Trim();
        return oneLine.Length <= 80 ? oneLine : oneLine[..80] + "…";
    }

    private static string ExtractShortcodeFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.Empty;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index] is "p" or "reel" or "tv")
                return segments[index + 1];
        }

        return string.Empty;
    }

    private static string BuildAccessText(string cookieBrowser) => cookieBrowser.ToLowerInvariant() switch
    {
        "none" => "공개 접근으로 사진 분석됨",
        "file" => "cookies.txt 파일로 사진 분석됨",
        _ => $"{cookieBrowser} 브라우저 쿠키로 사진 분석됨"
    };

    private static (string Message, bool Retryable) SimplifyError(string raw)
    {
        var text = string.IsNullOrWhiteSpace(raw) ? "사진 다운로드 엔진에서 알 수 없는 오류가 발생했습니다." : raw.Trim();
        if (YtDlpService.IsCookieDpapiError(text))
        {
            return ("선택한 Chromium 브라우저 쿠키가 Windows App-Bound Encryption으로 보호되어 복호화하지 못했습니다. cookies.txt 파일 또는 Firefox 쿠키를 사용해 주세요.", false);
        }

        if (text.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return ("Instagram 로그인이 필요하거나 브라우저 쿠키가 만료되었습니다. 로그인된 브라우저와 프로필을 선택해 주세요. 쿠키 DB 잠금이 감지되면 앱의 자동 종료 후 재시도를 이용할 수 있습니다.", false);
        }

        if (text.Contains("private", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            return ("게시물이 비공개이거나 삭제되었거나 현재 계정에 접근 권한이 없습니다.", false);
        }

        if (text.Contains("429", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("too many", StringComparison.OrdinalIgnoreCase))
        {
            return ("Instagram 요청 제한에 도달했습니다. 잠시 후 다시 시도해 주세요.", true);
        }

        if (text.Contains("403", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("temporarily", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return ("Instagram 또는 네트워크의 일시적인 오류로 사진을 받지 못했습니다. 자동 재시도를 진행합니다.", true);
        }

        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return (firstLine.Length > 420 ? firstLine[..420] : firstLine, false);
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

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

    private readonly record struct FileFingerprint(long Length, long LastWriteTicks);
}
