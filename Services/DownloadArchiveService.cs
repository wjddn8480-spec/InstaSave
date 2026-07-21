using System.Text.Json;

namespace InstaSave.Services;

public sealed record DownloadRecord(
    string NormalizedUrl,
    string MediaId,
    string Title,
    string OutputPath,
    DateTime CompletedAt);

public sealed class DownloadArchiveService
{
    private readonly object _sync = new();
    private readonly List<DownloadRecord> _records;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public DownloadArchiveService()
    {
        _records = LoadRecords();
    }

    public bool ContainsUrl(string url)
    {
        var normalized = NormalizeUrl(url);
        lock (_sync)
            return _records.Any(x => string.Equals(x.NormalizedUrl, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public bool ContainsMediaId(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return false;

        lock (_sync)
            return _records.Any(x => string.Equals(x.MediaId, mediaId, StringComparison.OrdinalIgnoreCase));
    }

    public void Record(string url, string mediaId, string title, string outputPath)
    {
        var normalized = NormalizeUrl(url);
        lock (_sync)
        {
            _records.RemoveAll(x =>
                string.Equals(x.NormalizedUrl, normalized, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(mediaId) && string.Equals(x.MediaId, mediaId, StringComparison.OrdinalIgnoreCase)));

            _records.Add(new DownloadRecord(normalized, mediaId, title, outputPath, DateTime.Now));
            SaveUnsafe();
        }
    }

    public static string NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim();

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
    }

    private static List<DownloadRecord> LoadRecords()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.DownloadRecordsPath))
                return [];

            var json = File.ReadAllText(AppPaths.DownloadRecordsPath);
            return JsonSerializer.Deserialize<List<DownloadRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveUnsafe()
    {
        AppPaths.EnsureDirectories();
        var snapshot = _records
            .OrderByDescending(x => x.CompletedAt)
            .Take(2000)
            .ToArray();
        File.WriteAllText(AppPaths.DownloadRecordsPath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }
}
