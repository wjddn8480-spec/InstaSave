using System.Text.Json;
using InstaSave.Models;

namespace InstaSave.Services;

public sealed class HistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<DownloadItem> Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.HistoryPath))
                return Array.Empty<DownloadItem>();

            var json = File.ReadAllText(AppPaths.HistoryPath);
            var items = JsonSerializer.Deserialize<List<DownloadItem>>(json, JsonOptions) ?? [];

            foreach (var item in items)
            {
                if (item.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading)
                {
                    item.Status = DownloadStatus.Canceled;
                    item.Detail = "이전 실행이 중단됨 · 재시도하면 이어받습니다.";
                }
            }

            return items.OrderByDescending(x => x.AddedAt).Take(100).ToArray();
        }
        catch
        {
            return Array.Empty<DownloadItem>();
        }
    }

    public void Save(IEnumerable<DownloadItem> items)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var snapshot = items.OrderByDescending(x => x.AddedAt).Take(100).ToArray();
            File.WriteAllText(AppPaths.HistoryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch
        {
            // 다운로드 자체에 영향을 주지 않도록 기록 저장 오류는 무시합니다.
        }
    }
}
