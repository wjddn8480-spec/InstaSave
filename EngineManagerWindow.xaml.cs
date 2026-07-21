using System.Diagnostics;
using System.Windows;
using InstaSave.Models;
using InstaSave.Services;
using Forms = System.Windows.Forms;

namespace InstaSave;

public partial class EngineManagerWindow : Window
{
    private readonly ToolBootstrapService _toolService;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _busy;

    public EngineManagerWindow(ToolBootstrapService toolService, SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();
        LocalizationService.LocalizeWindow(this);
        _toolService = toolService;
        _settingsService = settingsService;
        _settings = settings;
        ApplyWorkAreaConstraints();
        Loaded += EngineManagerWindow_Loaded;
    }

    private void ApplyWorkAreaConstraints()
    {
        var workArea = SystemParameters.WorkArea;
        var availableWidth = Math.Max(480, workArea.Width - 32);
        var availableHeight = Math.Max(320, workArea.Height - 32);

        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        MaxWidth = availableWidth;
        MaxHeight = availableHeight;
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
    }

    private async void EngineManagerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        FfmpegFolderTextBox.Text = _settings.FfmpegDirectory;
        await RefreshAsync();
        await Dispatcher.InvokeAsync(() => LocalizationService.LocalizeWindow(this));
    }

    private async Task RefreshAsync()
    {
        try
        {
            YtDlpPathText.Text = AppPaths.YtDlpPath;
            GalleryDlPathText.Text = AppPaths.GalleryDlPath;
            YtDlpVersionText.Text = LocalizationService.Translate(await _toolService.GetVersionAsync());
            GalleryDlVersionText.Text = LocalizationService.Translate(await _toolService.GetPhotoEngineVersionAsync());
            BackupStatusText.Text = LocalizationService.Translate(_toolService.HasBackup ? "복원 가능" : "백업 없음");
            PhotoBackupStatusText.Text = LocalizationService.Translate(_toolService.HasPhotoBackup ? "복원 가능" : "백업 없음");
            RestoreButton.IsEnabled = _toolService.HasBackup && !_busy;
            PhotoRestoreButton.IsEnabled = _toolService.HasPhotoBackup && !_busy;

            var ffmpeg = await _toolService.GetFfmpegInfoAsync(_settings.FfmpegDirectory);
            FfmpegStatusText.Text = LocalizationService.Translate(ffmpeg.IsAvailable
                ? $"사용 가능 · {ffmpeg.DisplayName}\n{ffmpeg.ExecutablePath}"
                : "설치되지 않았거나 경로를 찾지 못했습니다. 영상·음성 병합이 필요한 형식에서는 FFmpeg가 필요할 수 있습니다.");
        }
        catch (Exception ex)
        {
            OperationStatusText.Text = LocalizationService.Translate(ex.Message);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("영상 엔진 yt-dlp를 업데이트하는 중입니다.", async () =>
        {
            var result = await _toolService.UpdateAsync();
            return string.IsNullOrWhiteSpace(result) ? "영상 엔진 업데이트가 완료되었습니다." : result.Trim();
        });
    }

    private async void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        await RunDownloadOperationAsync("영상 엔진 yt-dlp를 새로 내려받는 중입니다.", async progress =>
        {
            await _toolService.ReinstallAsync(progress);
            return "영상 엔진 재설치가 완료되었습니다.";
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("이전 영상 엔진을 복원하는 중입니다.", async () =>
        {
            await _toolService.RestoreBackupAsync();
            return "이전 영상 엔진 복원이 완료되었습니다.";
        });
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("영상 엔진 캐시를 초기화하는 중입니다.", async () =>
        {
            await _toolService.ClearCacheAsync();
            return "영상 엔진 캐시 초기화가 완료되었습니다.";
        });
    }

    private async void PhotoUpdate_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("사진 엔진 gallery-dl을 업데이트하는 중입니다.", async () =>
        {
            var result = await _toolService.UpdatePhotoEngineAsync();
            return string.IsNullOrWhiteSpace(result) ? "사진 엔진 업데이트가 완료되었습니다." : result.Trim();
        });
    }

    private async void PhotoReinstall_Click(object sender, RoutedEventArgs e)
    {
        await RunDownloadOperationAsync("사진 엔진 gallery-dl을 새로 내려받는 중입니다.", async progress =>
        {
            await _toolService.ReinstallPhotoEngineAsync(progress);
            return "사진 엔진 재설치가 완료되었습니다.";
        });
    }

    private async void PhotoRestore_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("이전 사진 엔진을 복원하는 중입니다.", async () =>
        {
            await _toolService.RestorePhotoEngineBackupAsync();
            return "이전 사진 엔진 복원이 완료되었습니다.";
        });
    }

    private async void PhotoClearCache_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync("사진 엔진 캐시를 초기화하는 중입니다.", async () =>
        {
            await _toolService.ClearPhotoEngineCacheAsync();
            return "사진 엔진 캐시 초기화가 완료되었습니다.";
        });
    }

    private async Task RunDownloadOperationAsync(string status, Func<IProgress<double>, Task<string>> action)
    {
        OperationProgressBar.Value = 0;
        OperationProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value => OperationProgressBar.Value = value);
        await RunOperationAsync(status, () => action(progress));
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Translate("ffmpeg.exe가 들어 있는 폴더를 선택하세요."),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_settings.FfmpegDirectory)
                ? _settings.FfmpegDirectory
                : AppPaths.ToolsDirectory,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
            return;

        var executable = Path.Combine(dialog.SelectedPath, "ffmpeg.exe");
        if (!File.Exists(executable))
        {
            LocalizedMessageBox.Show("선택한 폴더에서 ffmpeg.exe를 찾지 못했습니다.", "FFmpeg 경로", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.FfmpegDirectory = dialog.SelectedPath;
        FfmpegFolderTextBox.Text = dialog.SelectedPath;
        _settingsService.Save(_settings);
        _ = RefreshAsync();
    }

    private void ClearFfmpegPath_Click(object sender, RoutedEventArgs e)
    {
        _settings.FfmpegDirectory = string.Empty;
        FfmpegFolderTextBox.Clear();
        _settingsService.Save(_settings);
        _ = RefreshAsync();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void OpenTools_Click(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.ToolsDirectory);

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.LogPath))
            File.WriteAllText(AppPaths.LogPath, string.Empty);
        Process.Start(new ProcessStartInfo(AppPaths.LogPath) { UseShellExecute = true });
    }

    private async Task RunOperationAsync(string status, Func<Task<string>> action)
    {
        if (_busy)
            return;

        _busy = true;
        SetButtons(false);
        OperationStatusText.Text = LocalizationService.Translate(status);
        try
        {
            var result = await action();
            OperationStatusText.Text = LocalizationService.Translate(result);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LogService.Write($"Engine manager operation failed: {ex}");
            OperationStatusText.Text = LocalizationService.Translate(ex.Message);
            LocalizedMessageBox.Show(ex.Message, "엔진 관리 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            _busy = false;
            SetButtons(true);
        }
    }

    private void SetButtons(bool enabled)
    {
        UpdateButton.IsEnabled = enabled;
        ReinstallButton.IsEnabled = enabled;
        RestoreButton.IsEnabled = enabled && _toolService.HasBackup;
        ClearCacheButton.IsEnabled = enabled;
        PhotoUpdateButton.IsEnabled = enabled;
        PhotoReinstallButton.IsEnabled = enabled;
        PhotoRestoreButton.IsEnabled = enabled && _toolService.HasPhotoBackup;
        PhotoClearCacheButton.IsEnabled = enabled;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
