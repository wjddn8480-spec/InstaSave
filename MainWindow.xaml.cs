using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InstaSave.Models;
using InstaSave.Services;
using Forms = System.Windows.Forms;

namespace InstaSave;

public partial class MainWindow : Window
{
    private static readonly Regex UrlRegex = new("https?://[^\\s<>\"]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HttpClient ThumbnailClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly SettingsService _settingsService = new();
    private readonly HistoryService _historyService = new();
    private readonly DownloadArchiveService _downloadArchiveService = new();
    private readonly ToolBootstrapService _toolService = new();
    private readonly YtDlpService _ytDlpService = new();
    private readonly GalleryDlService _galleryDlService = new();
    private readonly SemaphoreSlim _cookieRecoveryGate = new(1, 1);
    private readonly DispatcherTimer _clipboardTimer;
    private AppSettings _settings = new();
    private CancellationTokenSource? _previewCancellation;
    private bool _isLoaded;
    private bool _isQueueRunning;
    private bool _clipboardBusy;
    private bool _isDetectingCookieBrowser;
    private bool _isInitializingEngine;
    private bool _suppressCookieSelectionChange;
    private bool _isRefreshingCookieProfiles;
    private string _lastClipboardText = string.Empty;

    public ObservableCollection<DownloadItem> DownloadItems { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        LocalizationService.LocalizeWindow(this);
        DataContext = this;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        DownloadItems.CollectionChanged += (_, _) => UpdateQueueUi();

        _clipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _clipboardTimer.Tick += ClipboardTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        ApplySettingsToUi();

        foreach (var item in _historyService.Load())
        {
            item.PropertyChanged += Item_PropertyChanged;
            DownloadItems.Add(item);
        }

        _isLoaded = true;
        UpdateCustomTemplateState();
        UpdateClipboardMonitoring();
        UpdateQueueUi();

        _isInitializingEngine = true;
        LanguageComboBox.IsEnabled = false;
        try
        {
            await InitializeEngineAsync();
        }
        finally
        {
            _isInitializingEngine = false;
            LanguageComboBox.IsEnabled = true;
        }

        await Dispatcher.InvokeAsync(() => LocalizationService.LocalizeWindow(this), DispatcherPriority.ContextIdle);
    }

    private async Task InitializeEngineAsync()
    {
        try
        {
            SetStatus("미디어 엔진을 확인하는 중입니다.");
            if (!File.Exists(AppPaths.YtDlpPath))
            {
                EngineVersionText.Text = LocalizationService.Translate("영상 엔진 설치 중");
                var progress = new Progress<double>(value => EngineVersionText.Text = LocalizationService.Translate($"영상 엔진 설치 중 {value:F0}%"));
                await _toolService.EnsureInstalledAsync(progress);
            }

            if (_settings.DownloadPhotos && !File.Exists(AppPaths.GalleryDlPath))
            {
                EngineVersionText.Text = LocalizationService.Translate("사진 엔진 설치 중");
                var progress = new Progress<double>(value => EngineVersionText.Text = LocalizationService.Translate($"사진 엔진 설치 중 {value:F0}%"));
                await _toolService.EnsurePhotoEngineInstalledAsync(progress);
            }

            EngineVersionText.Text = LocalizationService.Translate(await GetEngineVersionTextAsync());
            SetStatus("준비됨");
        }
        catch (Exception ex)
        {
            EngineVersionText.Text = LocalizationService.Translate("설치 실패");
            SetStatus("미디어 엔진 설치에 실패했습니다.");
            LogService.Write($"Engine initialization failed: {ex}");
            LocalizedMessageBox.Show(
                "미디어 엔진을 설치하지 못했습니다. 인터넷 연결 또는 보안 프로그램 차단 여부를 확인한 뒤 엔진 관리에서 재설치해 주세요.\n\n" + ex.Message,
                "엔진 설치 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task<string> GetEngineVersionTextAsync()
    {
        var videoVersion = await _toolService.GetVersionAsync();
        if (!_settings.DownloadPhotos)
            return $"yt-dlp {videoVersion}";

        var photoVersion = await _toolService.GetPhotoEngineVersionAsync();
        return $"yt-dlp {videoVersion} · gallery-dl {photoVersion}";
    }

    private void ApplySettingsToUi()
    {
        SelectComboByTag(LanguageComboBox, _settings.LanguageMode);
        OutputFolderTextBox.Text = _settings.OutputDirectory;
        SelectComboByTag(QualityComboBox, _settings.Quality);
        SelectComboByTag(CookieBrowserComboBox, _settings.CookieBrowser);
        RefreshCookieProfileChoices(_settings.CookieBrowser, _settings.CookieProfile);
        CookieFileTextBox.Text = _settings.CookieFilePath ?? string.Empty;
        UpdateCookieUiState();
        SelectComboByTag(FileNameComboBox, _settings.FileNamePreset);
        CustomFileNameTextBox.Text = string.IsNullOrWhiteSpace(_settings.CustomFileNameTemplate)
            ? "{uploader}_{date}_{id}"
            : _settings.CustomFileNameTemplate;
        SelectComboByTag(RetryCountComboBox, Math.Clamp(_settings.AutoRetryCount, 1, 5).ToString());
        SelectComboByTag(RetryDelayComboBox, Math.Clamp(_settings.AutoRetryDelaySeconds, 3, 20).ToString());
        DownloadAllCheckBox.IsChecked = _settings.DownloadAllMedia;
        DownloadPhotosCheckBox.IsChecked = _settings.DownloadPhotos;
        SaveThumbnailCheckBox.IsChecked = _settings.SaveThumbnail;
        AutoStartCheckBox.IsChecked = _settings.AutoStartQueue;
        AutoPasteClipboardCheckBox.IsChecked = _settings.AutoPasteClipboardUrl;
        ClipboardMonitorCheckBox.IsChecked = _settings.ClipboardMonitoringEnabled;
        PreventDuplicatesCheckBox.IsChecked = _settings.PreventDuplicateDownloads;
        AutoRetryCheckBox.IsChecked = _settings.AutoRetryEnabled;
    }

    private void SaveSettingsFromUi()
    {
        if (!_isLoaded)
            return;

        _settings.LanguageMode = GetSelectedTag(LanguageComboBox, "auto");
        _settings.OutputDirectory = OutputFolderTextBox.Text.Trim();
        _settings.Quality = GetSelectedTag(QualityComboBox, "best");
        _settings.CookieBrowser = GetSelectedTag(CookieBrowserComboBox, "none");
        _settings.CookieProfile = GetCookieProfileFromUi();
        _settings.CookieFilePath = CookieFileTextBox.Text.Trim();
        _settings.FileNamePreset = GetSelectedTag(FileNameComboBox, "uploader-date-id");
        _settings.CustomFileNameTemplate = CustomFileNameTextBox.Text.Trim();
        _settings.AutoRetryCount = ParseSelectedInt(RetryCountComboBox, 2);
        _settings.AutoRetryDelaySeconds = ParseSelectedInt(RetryDelayComboBox, 5);
        _settings.DownloadAllMedia = DownloadAllCheckBox.IsChecked == true;
        _settings.DownloadPhotos = DownloadPhotosCheckBox.IsChecked == true;
        _settings.SaveThumbnail = SaveThumbnailCheckBox.IsChecked == true;
        _settings.AutoStartQueue = AutoStartCheckBox.IsChecked == true;
        _settings.AutoPasteClipboardUrl = AutoPasteClipboardCheckBox.IsChecked == true;
        _settings.ClipboardMonitoringEnabled = ClipboardMonitorCheckBox.IsChecked == true;
        _settings.PreventDuplicateDownloads = PreventDuplicatesCheckBox.IsChecked == true;
        _settings.AutoRetryEnabled = AutoRetryCheckBox.IsChecked == true;
        _settingsService.Save(_settings);

        UpdateCustomTemplateState();
        UpdateClipboardMonitoring();
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded)
            return;

        var selectedMode = GetSelectedTag(LanguageComboBox, "auto");
        if (string.Equals(selectedMode, _settings.LanguageMode, StringComparison.OrdinalIgnoreCase))
            return;

        var downloadActive = _isInitializingEngine || _isQueueRunning || DownloadItems.Any(item =>
            item.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry);
        if (downloadActive)
        {
            SelectComboByTag(LanguageComboBox, _settings.LanguageMode);
            LocalizedMessageBox.Show(
                "다운로드가 진행 중일 때는 언어를 변경할 수 없습니다. 다운로드가 끝난 뒤 다시 시도해 주세요.",
                "언어 변경",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previousLanguageCode = LocalizationService.LanguageCode;
        _settings.LanguageMode = selectedMode;
        SaveSettingsFromUi();
        LocalizationService.SetLanguageMode(selectedMode);

        if (string.Equals(previousLanguageCode, LocalizationService.LanguageCode, StringComparison.OrdinalIgnoreCase))
            return;

        SaveHistory();
        var replacement = new MainWindow();
        System.Windows.Application.Current.MainWindow = replacement;
        replacement.Show();
        Close();
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingCookieProfiles)
            return;

        SaveSettingsFromUi();
    }

    private async void CookieBrowser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCookieUiState();
        if (_suppressCookieSelectionChange)
            return;

        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        if (source != "none" && source != "file" && source != "auto")
            RefreshCookieProfileChoices(source, _settings.CookieProfile);

        if (_isLoaded && source == "auto")
        {
            await AutoDetectBrowserAsync(showResult: true, verifyWithUrl: true);
            return;
        }

        SaveSettingsFromUi();
    }

    private void CookieProfile_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingCookieProfiles)
            return;

        var cookieSource = GetSelectedTag(CookieBrowserComboBox, "none");
        if (cookieSource == "none" || cookieSource == "file" || cookieSource == "auto")
            return;

        if (string.IsNullOrWhiteSpace(CookieProfileComboBox.Text))
        {
            var defaultProfile = BrowserCookieDetectionService.GetDefaultProfile(cookieSource);
            if (!string.IsNullOrWhiteSpace(defaultProfile))
                SelectCookieProfileValue(defaultProfile);
        }

        SaveSettingsFromUi();
    }

    private void RefreshCookieProfiles_Click(object sender, RoutedEventArgs e)
    {
        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        if (source == "none" || source == "file" || source == "auto")
        {
            LocalizedMessageBox.Show(
                "프로필을 새로 검색할 브라우저를 먼저 선택해 주세요.",
                "브라우저 프로필 새로고침",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var previous = GetCookieProfileFromUi();
        var count = RefreshCookieProfileChoices(source, previous);
        SaveSettingsFromUi();
        SetStatus(count > 0
            ? $"{BrowserProcessService.GetDisplayName(source)} 프로필 {count}개를 찾았습니다."
            : $"{BrowserProcessService.GetDisplayName(source)} 프로필을 찾지 못했습니다.");
    }
    private int RefreshCookieProfileChoices(string source, string? preferredProfile = null)
    {
        if (CookieProfileComboBox is null)
            return 0;

        var candidates = BrowserCookieDetectionService.FindCandidates(source);
        var preferred = BrowserCookieDetectionService.ResolveProfile(source, preferredProfile);
        var options = new List<(string Value, string Display, string ToolTip)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (seen.Add(candidate.Profile))
                options.Add((candidate.Profile, candidate.ProfileDisplayName, candidate.Profile));
        }

        if (source is "brave" or "vivaldi")
        {
            foreach (var name in new[] { "Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5" })
            {
                if (seen.Add(name))
                    options.Add((name, name, name));
            }
        }

        _isRefreshingCookieProfiles = true;
        try
        {
            CookieProfileComboBox.Items.Clear();
            foreach (var option in options)
            {
                CookieProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = LocalizationService.Translate(option.Display),
                    Tag = option.Value,
                    ToolTip = option.ToolTip
                });
            }

            var selected = !string.IsNullOrWhiteSpace(preferred) && SelectCookieProfileValue(preferred);
            if (!selected && CookieProfileComboBox.Items.Count > 0)
                CookieProfileComboBox.SelectedIndex = 0;
            else if (!selected)
                CookieProfileComboBox.Text = string.Empty;
        }
        finally
        {
            _isRefreshingCookieProfiles = false;
        }

        if (CookieProfileHelpText is not null)
        {
            CookieProfileHelpText.Text = LocalizationService.Translate(source == "firefox"
                ? candidates.Count > 0
                    ? $"Firefox 실제 프로필 {candidates.Count}개를 찾았습니다. 일반 설치와 Microsoft Store 설치 경로를 모두 검사합니다."
                    : "Firefox 쿠키 프로필을 찾지 못했습니다. Firefox에서 Instagram에 로그인한 뒤 프로필 새로고침을 눌러 주세요."
                : candidates.Count > 0
                    ? $"감지된 프로필 {candidates.Count}개와 기본 프로필 이름을 표시합니다."
                    : "기본 프로필 이름을 선택하거나 실제 프로필 이름을 직접 입력할 수 있습니다.");
        }

        return candidates.Count;
    }

    private bool SelectCookieProfileValue(string? value)
    {
        var requested = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requested))
            return false;

        foreach (var item in CookieProfileComboBox.Items.OfType<ComboBoxItem>())
        {
            var itemValue = item.Tag?.ToString() ?? item.Content?.ToString() ?? string.Empty;
            var itemFolder = Path.GetFileName(itemValue.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var requestedFolder = Path.GetFileName(requested.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (itemValue.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(itemFolder) && itemFolder.Equals(requestedFolder, StringComparison.OrdinalIgnoreCase)))
            {
                CookieProfileComboBox.SelectedItem = item;
                return true;
            }
        }

        CookieProfileComboBox.SelectedItem = null;
        CookieProfileComboBox.Text = requested;
        return false;
    }

    private bool TrySelectDetectedFirefoxProfile(string? excludedProfile = null)
    {
        var candidates = BrowserCookieDetectionService.FindCandidates("firefox");
        var candidate = candidates.FirstOrDefault(item =>
                            !item.Profile.Equals(excludedProfile, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault();
        if (candidate is null)
            return false;

        _suppressCookieSelectionChange = true;
        try
        {
            SelectComboByTag(CookieBrowserComboBox, "firefox");
            RefreshCookieProfileChoices("firefox", candidate.Profile);
            SelectCookieProfileValue(candidate.Profile);
        }
        finally
        {
            _suppressCookieSelectionChange = false;
        }

        UpdateCookieUiState();
        SaveSettingsFromUi();
        return true;
    }

    private void CustomFileName_TextChanged(object sender, TextChangedEventArgs e) => SaveSettingsFromUi();

    private void UpdateCustomTemplateState()
    {
        var isCustom = GetSelectedTag(FileNameComboBox, "uploader-date-id") == "custom";
        CustomFileNameTextBox.IsEnabled = isCustom;
        CustomFileNameTextBox.Opacity = isCustom ? 1 : 0.65;
    }

    private void UpdateCookieUiState()
    {
        if (CookieBrowserComboBox is null || CookieProfilePanel is null || CookieFilePanel is null ||
            CookieProfileComboBox is null || CookieFileTextBox is null || CheckCookiesButton is null ||
            AutoDetectCookieBrowserButton is null || CloseCookieBrowserButton is null || CookieHelpText is null)
        {
            return;
        }

        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        var useAuto = source == "auto";
        var useBrowser = source != "none" && source != "file" && !useAuto;
        var useFile = source == "file";
        var useCookies = source != "none";

        CookieProfilePanel.Visibility = useBrowser ? Visibility.Visible : Visibility.Collapsed;
        CookieFilePanel.Visibility = useFile ? Visibility.Visible : Visibility.Collapsed;
        CookieProfilePanel.IsEnabled = useBrowser;
        CookieProfilePanel.Opacity = useBrowser ? 1 : 0.55;

        AutoDetectCookieBrowserButton.IsEnabled = !_isDetectingCookieBrowser;
        AutoDetectCookieBrowserButton.Content = LocalizationService.Translate(_isDetectingCookieBrowser ? "자동 감지 중..." : "브라우저 자동 감지");
        CheckCookiesButton.IsEnabled = useCookies && !_isDetectingCookieBrowser;
        CheckCookiesButton.Content = LocalizationService.Translate(useFile
            ? "cookies.txt 접근 검사"
            : useAuto ? "자동 감지 후 쿠키 검사" : "선택한 브라우저 쿠키 검사");
        CloseCookieBrowserButton.Visibility = useBrowser ? Visibility.Visible : Visibility.Collapsed;
        CloseCookieBrowserButton.IsEnabled = useBrowser;
        CookieHelpText.Text = LocalizationService.Translate(useFile
            ? "파일 내용은 앱에 복사하지 않으며 선택한 경로에서 직접 읽습니다."
            : useAuto
                ? "설치된 브라우저와 프로필을 순서대로 검사하고, 성공한 항목을 자동 선택합니다."
                : source == "none"
                    ? "로그인이 필요한 게시물은 브라우저 자동 감지 또는 수동 쿠키 선택을 사용하세요."
                    : source == "firefox"
                        ? "Firefox는 실제 프로필 폴더를 선택해야 합니다. Default라는 고정 이름은 사용하지 않습니다."
                        : "DPAPI 오류가 발생하면 자동 감지를 다시 실행하거나 cookies.txt·Firefox 쿠키를 사용하세요.");

        if (useBrowser && string.IsNullOrWhiteSpace(CookieProfileComboBox.Text))
        {
            var defaultProfile = BrowserCookieDetectionService.GetDefaultProfile(source);
            if (!string.IsNullOrWhiteSpace(defaultProfile))
                SelectCookieProfileValue(defaultProfile);
        }
    }

    private async void AutoDetectCookieBrowser_Click(object sender, RoutedEventArgs e)
    {
        await AutoDetectBrowserAsync(showResult: true, verifyWithUrl: true);
    }

    private async Task<bool> AutoDetectBrowserAsync(
        bool showResult,
        bool verifyWithUrl,
        string? preferredUrl = null)
    {
        if (_isDetectingCookieBrowser)
        {
            if (showResult)
            {
                LocalizedMessageBox.Show(
                    "브라우저 쿠키 자동 감지가 이미 진행 중입니다.",
                    "브라우저 자동 감지",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return false;
        }

        _isDetectingCookieBrowser = true;
        UpdateCookieUiState();
        try
        {
            var candidates = BrowserCookieDetectionService.FindCandidates();
            if (candidates.Count == 0)
            {
                SetStatus("쿠키 프로필이 있는 브라우저를 찾지 못했습니다.");
                if (showResult)
                {
                    LocalizedMessageBox.Show(
                        "Firefox, Brave, Opera 또는 Vivaldi의 사용자 프로필을 찾지 못했습니다.\n\n브라우저를 한 번 실행한 뒤 다시 감지하거나 cookies.txt 파일을 선택해 주세요.",
                        "브라우저 자동 감지 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return false;
            }

            var url = preferredUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = ExtractInstagramUrls(UrlTextBox.Text).FirstOrDefault()
                      ?? DownloadItems.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Url))?.Url;
            }

            BrowserCookieCandidate? selectedCandidate = null;
            MediaInfo? verifiedInfo = null;
            var failures = new List<string>();

            if (verifyWithUrl && !string.IsNullOrWhiteSpace(url))
            {
                await EnsureEngineReadyAsync();
                foreach (var candidate in candidates)
                {
                    SetStatus($"쿠키 자동 검사 중: {candidate.DisplayText}");
                    try
                    {
                        try
                        {
                            verifiedInfo = await _ytDlpService.CheckCookieAccessAsync(
                                url,
                                candidate.Browser,
                                candidate.Profile,
                                CancellationToken.None);
                        }
                        catch (YtDlpException ex) when (IsNoVideoFormatsError(ex.Message))
                        {
                            await _toolService.EnsurePhotoEngineInstalledAsync();
                            verifiedInfo = await _galleryDlService.AnalyzeAsync(
                                url,
                                candidate.Browser,
                                candidate.Profile,
                                CancellationToken.None);
                        }

                        selectedCandidate = candidate;
                        break;
                    }
                    catch (BrowserCookieDecryptException)
                    {
                        failures.Add($"{candidate.DisplayText}: DPAPI 복호화 실패");
                    }
                    catch (BrowserCookieLockedException)
                    {
                        failures.Add($"{candidate.DisplayText}: 쿠키 DB 사용 중");
                    }
                    catch (YtDlpException ex)
                    {
                        failures.Add($"{candidate.DisplayText}: {FirstLine(ex.Message)}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{candidate.DisplayText}: {FirstLine(ex.Message)}");
                    }
                }
            }
            else
            {
                selectedCandidate = candidates[0];
            }

            if (selectedCandidate is null)
            {
                SetStatus("사용 가능한 브라우저 쿠키를 자동으로 찾지 못했습니다.");
                if (showResult)
                {
                    var details = failures.Count == 0
                        ? string.Empty
                        : "\n\n검사 결과:\n" + string.Join("\n", failures.Take(6));
                    LocalizedMessageBox.Show(
                        "설치된 브라우저 프로필을 찾았지만 Instagram 게시물에 접근 가능한 쿠키를 확인하지 못했습니다.\n\n" +
                        "브라우저에서 Instagram에 로그인한 뒤 다시 시도하거나 Firefox 또는 cookies.txt 파일을 사용해 주세요." + details,
                        "브라우저 자동 감지 실패",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return false;
            }

            ApplyDetectedCookieCandidate(selectedCandidate);
            var verificationText = verifiedInfo is null
                ? "설치된 프로필 정보를 기준으로 선택했습니다. '선택한 쿠키 검사'를 눌러 실제 접근을 확인할 수 있습니다."
                : $"Instagram 접근 확인 완료 · 게시자: {verifiedInfo.Author}";
            SetStatus($"브라우저 자동 감지 완료: {selectedCandidate.DisplayText}");

            if (showResult)
            {
                LocalizedMessageBox.Show(
                    $"브라우저 쿠키를 자동으로 선택했습니다.\n\n브라우저: {selectedCandidate.BrowserDisplayName}\n프로필: {selectedCandidate.ProfileDisplayName}\n{verificationText}",
                    "브라우저 자동 감지 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return true;
        }
        finally
        {
            _isDetectingCookieBrowser = false;
            UpdateCookieUiState();
        }
    }

    private void ApplyDetectedCookieCandidate(BrowserCookieCandidate candidate)
    {
        _suppressCookieSelectionChange = true;
        try
        {
            SelectComboByTag(CookieBrowserComboBox, candidate.Browser);
            RefreshCookieProfileChoices(candidate.Browser, candidate.Profile);
            SelectCookieProfileValue(candidate.Profile);
        }
        finally
        {
            _suppressCookieSelectionChange = false;
        }

        UpdateCookieUiState();
        SaveSettingsFromUi();
    }

    private async Task<bool> EnsureCookieSelectionReadyAsync(string? url = null)
    {
        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        if (source == "auto")
        {
            var detected = await AutoDetectBrowserAsync(showResult: false, verifyWithUrl: true, preferredUrl: url);
            if (!detected)
            {
                LocalizedMessageBox.Show(
                    "사용 가능한 브라우저 쿠키를 자동으로 찾지 못했습니다.\n\n브라우저에서 Instagram에 로그인한 뒤 다시 감지하거나, Firefox·수동 프로필·cookies.txt 방식을 사용해 주세요.",
                    "브라우저 자동 감지 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return detected;
        }

        return ValidateCookieSelection();
    }

    private static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "알 수 없는 오류";

        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? value.Trim();
    }

    private void UpdateClipboardMonitoring()
    {
        if (!_isLoaded)
            return;

        if (_settings.AutoPasteClipboardUrl || _settings.ClipboardMonitoringEnabled)
        {
            if (!_clipboardTimer.IsEnabled)
            {
                try
                {
                    _lastClipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                }
                catch
                {
                    _lastClipboardText = string.Empty;
                }
                _clipboardTimer.Start();
            }
        }
        else
        {
            _clipboardTimer.Stop();
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Translate("Instagram 사진과 영상을 저장할 폴더를 선택하세요."),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputFolderTextBox.Text)
                ? OutputFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputFolderTextBox.Text = dialog.SelectedPath;
            SaveSettingsFromUi();
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText())
                return;

            var urls = ExtractInstagramUrls(Clipboard.GetText());
            if (urls.Count == 0)
            {
                SetStatus("클립보드에서 Instagram URL을 찾지 못했습니다.");
                return;
            }

            var appended = AppendUrlsToInput(urls);
            SetStatus($"클립보드에서 Instagram URL {appended}개를 붙여넣었습니다.");
        }
        catch (Exception ex)
        {
            SetStatus("클립보드를 읽지 못했습니다: " + ex.Message);
        }
    }

    private int AppendUrlsToInput(IEnumerable<string> urls)
    {
        var current = ExtractInstagramUrls(UrlTextBox.Text);
        var known = current
            .Select(DownloadArchiveService.NormalizeUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<string>();

        foreach (var url in urls)
        {
            var normalized = DownloadArchiveService.NormalizeUrl(url);
            if (known.Add(normalized))
                additions.Add(url);
        }

        if (additions.Count == 0)
            return 0;

        var existingText = UrlTextBox.Text.Trim();
        UrlTextBox.Text = string.IsNullOrWhiteSpace(existingText)
            ? string.Join(Environment.NewLine, additions)
            : existingText + Environment.NewLine + string.Join(Environment.NewLine, additions);
        UrlTextBox.CaretIndex = UrlTextBox.Text.Length;
        UrlTextBox.ScrollToEnd();
        return additions.Count;
    }

    private void BrowseCookieFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Translate("Instagram cookies.txt 파일 선택"),
            Filter = LocalizationService.Translate("Netscape cookies.txt (*.txt)|*.txt|모든 파일 (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(CookieFileTextBox.Text))
        {
            try
            {
                var directory = Path.GetDirectoryName(CookieFileTextBox.Text);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    dialog.InitialDirectory = directory;
            }
            catch
            {
                // 잘못된 기존 경로는 무시합니다.
            }
        }

        if (dialog.ShowDialog(this) != true)
            return;

        CookieFileTextBox.Text = dialog.FileName;
        SaveSettingsFromUi();
        SetStatus("cookies.txt 파일을 선택했습니다. 쿠키 검사를 실행해 주세요.");
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        var url = ExtractInstagramUrls(UrlTextBox.Text).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            LocalizedMessageBox.Show("미리 볼 Instagram 게시물 URL을 입력해 주세요.", "미리보기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await EnsureCookieSelectionReadyAsync(url))
            return;

        SaveSettingsFromUi();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();

        try
        {
            await EnsureEngineReadyAsync();
            SetStatus("게시물 미리보기를 불러오는 중입니다.");
            MediaInfo info;
            try
            {
                info = await ExecuteWithCookieLockRecoveryAsync(
                    token => _ytDlpService.AnalyzeAsync(
                        url,
                        GetSelectedTag(CookieBrowserComboBox, "none"),
                        GetCookieCredentialFromUi(),
                        token),
                    _previewCancellation.Token);
            }
            catch (YtDlpException ex) when (_settings.DownloadPhotos && IsNoVideoFormatsError(ex.Message))
            {
                await _toolService.EnsurePhotoEngineInstalledAsync(cancellationToken: _previewCancellation.Token);
                info = await ExecuteWithCookieLockRecoveryAsync(
                    token => _galleryDlService.AnalyzeAsync(
                        url,
                        GetSelectedTag(CookieBrowserComboBox, "none"),
                        GetCookieCredentialFromUi(),
                        token),
                    _previewCancellation.Token);
            }

            ShowPreview(info);
            await LoadPreviewImageAsync(info.ThumbnailUrl, _previewCancellation.Token);
            SetStatus("게시물 미리보기를 불러왔습니다.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("미리보기를 취소했습니다.");
        }
        catch (Exception ex)
        {
            LogService.Write($"Preview failed: {ex}");
            LocalizedMessageBox.Show(ex.Message, "미리보기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("미리보기를 불러오지 못했습니다.");
        }
    }

    private void ShowPreview(MediaInfo info)
    {
        PreviewBorder.Visibility = Visibility.Visible;
        PreviewTitleText.Text = info.Title is "Instagram 게시물" or "Instagram 사진 게시물"
            ? LocalizationService.Translate(info.Title)
            : info.Title;
        PreviewAuthorText.Text = $"{LocalizationService.Translate("게시자")}: {info.Author}";
        PreviewIdText.Text = $"ID {(!string.IsNullOrWhiteSpace(info.MediaId) ? info.MediaId : LocalizationService.Translate("확인 불가"))}";
        PreviewDateText.Text = $"{LocalizationService.Translate("업로드")} {info.UploadDateText}";
        PreviewDurationText.Text = $"{LocalizationService.Translate("재생 시간")} {info.DurationText}";
        PreviewCountText.Text = LocalizationService.Translate(info.MediaCountText);
        PreviewAccessText.Text = LocalizationService.Translate(info.AccessText);
        PreviewDescriptionText.Text = string.IsNullOrWhiteSpace(info.Description) ? LocalizationService.Translate("게시물 설명이 없습니다.") : info.Description;
        PreviewImage.Source = null;
    }

    private static BitmapImage CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task LoadPreviewImageAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            var bytes = await ThumbnailClient.GetByteArrayAsync(uri, cancellationToken);
            PreviewImage.Source = CreateBitmap(bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogService.Write($"Thumbnail preview failed: {ex.Message}");
            PreviewImage.Source = null;
        }
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        _previewCancellation?.Cancel();
        PreviewBorder.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
    }

    private async void CheckCookies_Click(object sender, RoutedEventArgs e)
    {
        var url = ExtractInstagramUrls(UrlTextBox.Text).FirstOrDefault()
                  ?? DownloadItems.FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            LocalizedMessageBox.Show("쿠키 접근을 검사할 Instagram 게시물 URL을 먼저 입력해 주세요.", "쿠키 검사", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await EnsureCookieSelectionReadyAsync(url))
            return;

        var browser = GetSelectedTag(CookieBrowserComboBox, "none");
        if (browser == "none")
        {
            LocalizedMessageBox.Show("검사할 쿠키 사용 방식을 먼저 선택해 주세요.", "쿠키 검사", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettingsFromUi();
        try
        {
            await EnsureEngineReadyAsync();
            SetStatus("선택한 쿠키와 게시물 접근 권한을 검사하는 중입니다.");
            MediaInfo info;
            try
            {
                info = await ExecuteWithCookieLockRecoveryAsync(
                    token => _ytDlpService.CheckCookieAccessAsync(url, browser, GetCookieCredentialFromUi(), token),
                    CancellationToken.None);
            }
            catch (YtDlpException ex) when (_settings.DownloadPhotos && IsNoVideoFormatsError(ex.Message))
            {
                await _toolService.EnsurePhotoEngineInstalledAsync();
                info = await ExecuteWithCookieLockRecoveryAsync(
                    token => _galleryDlService.AnalyzeAsync(url, browser, GetCookieCredentialFromUi(), token),
                    CancellationToken.None);
            }

            LocalizedMessageBox.Show(
                $"쿠키 읽기와 게시물 접근에 성공했습니다.\n\n게시자: {info.Author}\n제목: {info.Title}\n쿠키 소스: {GetCookieSourceDisplay(browser)}",
                "쿠키 검사 성공",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SetStatus("선택한 쿠키를 정상적으로 읽었습니다.");
        }
        catch (Exception ex)
        {
            LogService.Write($"Cookie check failed: {ex}");
            LocalizedMessageBox.Show(ex.Message, "쿠키 검사 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("쿠키 검사에 실패했습니다.");
        }
    }

    private async void CloseCookieBrowser_Click(object sender, RoutedEventArgs e)
    {
        var browser = GetSelectedTag(CookieBrowserComboBox, "none");
        if (browser == "none" || browser == "file" || browser == "auto")
        {
            LocalizedMessageBox.Show("종료할 브라우저를 먼저 선택해 주세요.", "브라우저 종료", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var displayName = BrowserProcessService.GetDisplayName(browser);
        var runningCount = BrowserProcessService.GetRunningProcessCount(browser);
        if (runningCount == 0)
        {
            LocalizedMessageBox.Show($"현재 실행 중인 {displayName} 프로세스를 찾지 못했습니다.", "브라우저 종료", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = LocalizedMessageBox.Show(
            $"쿠키 데이터베이스 잠금을 해제하기 위해 {displayName}의 모든 창과 백그라운드 프로세스를 종료합니다.\n\n" +
            "열려 있던 탭은 보통 다음 실행 시 복원되지만, 작성 중인 내용은 먼저 저장하는 것이 안전합니다. 계속할까요?",
            "선택한 브라우저 종료",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            SetStatus($"{displayName}을 종료하는 중입니다.");
            var result = await BrowserProcessService.CloseBrowserAsync(browser, CancellationToken.None);
            if (result.RemainingProcessCount > 0)
            {
                throw new InvalidOperationException(
                    $"{displayName} 프로세스 {result.RemainingProcessCount}개를 종료하지 못했습니다. InstaSave를 관리자 권한으로 실행하거나 작업 관리자에서 직접 종료해 주세요.");
            }

            LocalizedMessageBox.Show(
                result.FoundProcessCount == 0
                    ? $"실행 중인 {displayName} 프로세스가 없습니다."
                    : $"{displayName} 프로세스 {result.ClosedProcessCount}개를 종료했습니다. 이제 쿠키 검사를 다시 실행해 주세요.",
                "브라우저 종료 완료",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SetStatus($"{displayName} 종료 완료");
        }
        catch (Exception ex)
        {
            LogService.Write($"Browser close failed: {ex}");
            LocalizedMessageBox.Show(ex.Message, "브라우저 종료 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("브라우저를 종료하지 못했습니다.");
        }
    }

    private async void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        var urls = ExtractInstagramUrls(UrlTextBox.Text);
        if (urls.Count == 0)
        {
            LocalizedMessageBox.Show(
                "올바른 Instagram URL을 찾지 못했습니다. 사진, 캐러셀, 릴스 또는 동영상 게시물 주소를 확인해 주세요.",
                "URL 확인",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var added = AddUrlsToQueue(urls, showDuplicatePrompt: true);
        if (added == 0)
        {
            SetStatus("추가할 새 URL이 없습니다.");
            return;
        }

        UrlTextBox.Clear();
        SaveHistory();
        SetStatus($"{added}개 URL을 목록에 추가했습니다.");

        if (_settings.AutoStartQueue)
            await StartQueueAsync();
    }

    private int AddUrlsToQueue(IEnumerable<string> urls, bool showDuplicatePrompt)
    {
        var existingUrls = DownloadItems
            .Where(x => x.Status != DownloadStatus.Completed)
            .Select(x => DownloadArchiveService.NormalizeUrl(x.Url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var url in urls)
        {
            var normalized = DownloadArchiveService.NormalizeUrl(url);
            if (!existingUrls.Add(normalized))
                continue;

            var wasDownloaded = _downloadArchiveService.ContainsUrl(url) ||
                                DownloadItems.Any(x =>
                                    x.Status == DownloadStatus.Completed &&
                                    string.Equals(DownloadArchiveService.NormalizeUrl(x.Url), normalized, StringComparison.OrdinalIgnoreCase));
            var allowDuplicate = false;

            if (_settings.PreventDuplicateDownloads && wasDownloaded)
            {
                if (!showDuplicatePrompt)
                    continue;

                var answer = LocalizedMessageBox.Show(
                    "이미 다운로드한 기록이 있는 게시물입니다. 기존 파일을 덮어쓰고 다시 다운로드하시겠습니까?",
                    "중복 다운로드 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    continue;

                allowDuplicate = true;
            }

            var item = new DownloadItem
            {
                Url = url,
                Title = "분석 대기 중",
                Detail = allowDuplicate ? "강제 재다운로드 대기" : "대기 중",
                Status = DownloadStatus.Pending,
                AllowDuplicate = allowDuplicate
            };
            item.PropertyChanged += Item_PropertyChanged;
            DownloadItems.Insert(0, item);
            added++;
        }

        return added;
    }

    private async void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        if (_clipboardBusy || (!_settings.AutoPasteClipboardUrl && !_settings.ClipboardMonitoringEnabled))
            return;

        _clipboardBusy = true;
        try
        {
            if (!Clipboard.ContainsText())
                return;

            var text = Clipboard.GetText().Trim();
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _lastClipboardText, StringComparison.Ordinal))
                return;

            _lastClipboardText = text;
            var urls = ExtractInstagramUrls(text);
            if (urls.Count == 0)
                return;

            var pasted = _settings.AutoPasteClipboardUrl ? AppendUrlsToInput(urls) : 0;
            var added = _settings.ClipboardMonitoringEnabled
                ? AddUrlsToQueue(urls, showDuplicatePrompt: false)
                : 0;

            if (added > 0)
            {
                SaveHistory();
                SetStatus($"복사한 Instagram URL {added}개를 대기열에 자동 추가했습니다.");
                if (_settings.AutoStartQueue && !_isQueueRunning)
                    await StartQueueAsync();
            }
            else if (pasted > 0)
            {
                SetStatus($"복사한 Instagram URL {pasted}개를 입력창에 자동으로 붙여넣었습니다.");
            }
        }
        catch
        {
            // 다른 프로그램이 클립보드를 잠시 사용 중일 수 있습니다.
        }
        finally
        {
            _clipboardBusy = false;
        }
    }

    private async void StartAll_Click(object sender, RoutedEventArgs e) => await StartQueueAsync();

    private async Task StartQueueAsync()
    {
        if (_isQueueRunning)
            return;

        var firstTargetUrl = DownloadItems
            .Where(x => x.Status is DownloadStatus.Pending or DownloadStatus.Failed or DownloadStatus.Canceled)
            .OrderBy(x => x.AddedAt)
            .Select(x => x.Url)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstTargetUrl))
        {
            SetStatus("시작할 다운로드 항목이 없습니다.");
            return;
        }

        if (!await EnsureCookieSelectionReadyAsync(firstTargetUrl))
            return;

        SaveSettingsFromUi();
        if (DownloadItems.Any(x => x.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry))
        {
            SetStatus("현재 다운로드가 끝난 뒤 전체 시작을 눌러 주세요.");
            return;
        }

        _isQueueRunning = true;
        StartAllButton.IsEnabled = false;
        try
        {
            await EnsureEngineReadyAsync();
            var targets = DownloadItems
                .Where(x => x.Status is DownloadStatus.Pending or DownloadStatus.Failed or DownloadStatus.Canceled)
                .OrderBy(x => x.AddedAt)
                .ToArray();

            if (targets.Length == 0)
            {
                SetStatus("시작할 다운로드 항목이 없습니다.");
                return;
            }

            foreach (var item in targets)
            {
                if (!DownloadItems.Contains(item) || !item.CanStart)
                    continue;

                await RunItemAsync(item);
            }

            var completedCount = targets.Count(x => x.Status == DownloadStatus.Completed);
            var failedCount = targets.Count(x => x.Status == DownloadStatus.Failed);
            App.Notifications.ShowDownloadSummary(completedCount, failedCount);
        }
        catch (Exception ex)
        {
            LogService.Write($"Queue failed: {ex}");
            LocalizedMessageBox.Show(ex.Message, "다운로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isQueueRunning = false;
            StartAllButton.IsEnabled = true;
            SaveHistory();
            SetStatus("대기열 처리가 끝났습니다.");
        }
    }

    private async void StartItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item } || !item.CanStart)
            return;

        if (!await EnsureCookieSelectionReadyAsync(item.Url))
            return;

        SaveSettingsFromUi();
        if (_isQueueRunning || DownloadItems.Any(x => x.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry))
        {
            SetStatus("다른 다운로드가 끝난 뒤 다시 시도해 주세요.");
            return;
        }

        try
        {
            await EnsureEngineReadyAsync();
            await RunItemAsync(item);
            App.Notifications.ShowDownloadSummary(
                item.Status == DownloadStatus.Completed ? 1 : 0,
                item.Status == DownloadStatus.Failed ? 1 : 0);
        }
        catch (Exception ex)
        {
            LogService.Write($"Single item start failed: {ex}");
            LocalizedMessageBox.Show(ex.Message, "다운로드 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EnsureEngineReadyAsync()
    {
        SetStatus("미디어 엔진을 확인하는 중입니다.");
        await _toolService.EnsureInstalledAsync();
        if (_settings.DownloadPhotos)
            await _toolService.EnsurePhotoEngineInstalledAsync();
        EngineVersionText.Text = LocalizationService.Translate(await GetEngineVersionTextAsync());
    }

    private async Task RunItemAsync(DownloadItem item)
    {
        if (item.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry)
            return;

        item.Cancellation?.Dispose();
        item.Cancellation = new CancellationTokenSource();
        var cancellationToken = item.Cancellation.Token;
        var totalAttempts = _settings.AutoRetryEnabled ? Math.Clamp(_settings.AutoRetryCount + 1, 2, 6) : 1;

        item.ErrorMessage = string.Empty;
        item.Progress = 0;
        item.Speed = string.Empty;
        item.Eta = string.Empty;
        item.OutputPath = string.Empty;

        try
        {
            for (var attempt = 1; attempt <= totalAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.RetryAttempt = attempt - 1;
                item.Progress = 0;
                item.Speed = string.Empty;
                item.Eta = string.Empty;
                item.ErrorMessage = string.Empty;
                item.Status = DownloadStatus.Analyzing;
                item.Detail = attempt == 1
                    ? "게시물 정보를 확인하는 중"
                    : $"재시도 {attempt - 1}/{totalAttempts - 1} · 게시물 분석 중";
                SetStatus("Instagram 게시물을 분석하는 중입니다.");

                try
                {
                    var photoOnlyPost = false;
                    try
                    {
                        var info = await ExecuteWithCookieLockRecoveryAsync(
                            token => _ytDlpService.AnalyzeAsync(
                                item.Url,
                                _settings.CookieBrowser,
                                _settings.CookieBrowser == "file" ? _settings.CookieFilePath : _settings.CookieProfile,
                                token),
                            cancellationToken);
                        ApplyMediaInfo(item, info);
                    }
                    catch (YtDlpException ex) when (_settings.DownloadPhotos && IsNoVideoFormatsError(ex.Message))
                    {
                        await _toolService.EnsurePhotoEngineInstalledAsync(cancellationToken: cancellationToken);
                        var photoInfo = await ExecuteWithCookieLockRecoveryAsync(
                            token => _galleryDlService.AnalyzeAsync(
                                item.Url,
                                _settings.CookieBrowser,
                                _settings.CookieBrowser == "file" ? _settings.CookieFilePath : _settings.CookieProfile,
                                token),
                            cancellationToken);
                        ApplyMediaInfo(item, photoInfo);
                        photoOnlyPost = true;
                    }

                    item.Status = DownloadStatus.Downloading;
                    item.Detail = attempt == 1 ? "다운로드 준비 중" : $"재시도 {attempt - 1} · 다운로드 준비 중";
                    var outputFiles = new List<string>();
                    var wasAlreadyDownloaded = false;
                    string? photoWarning = null;

                    if (!photoOnlyPost)
                    {
                        var videoProgress = new Progress<DownloadProgress>(value =>
                        {
                            item.Progress = _settings.DownloadPhotos ? value.Percent * 0.7 : value.Percent;
                            item.Speed = value.Speed;
                            item.Eta = value.Eta;
                            item.Detail = "영상 · " + value.Detail;
                            SetStatus($"영상 다운로드 중: {item.Title}");
                        });

                        try
                        {
                            var videoResult = await ExecuteWithCookieLockRecoveryAsync(
                                token => _ytDlpService.DownloadAsync(
                                    item,
                                    _settings,
                                    videoProgress,
                                    line => LogService.Write($"[{item.Id}][yt-dlp] {line}"),
                                    token),
                                cancellationToken);
                            outputFiles.AddRange(videoResult.OutputFiles);
                            wasAlreadyDownloaded |= videoResult.WasAlreadyDownloaded;
                        }
                        catch (YtDlpException ex) when (_settings.DownloadPhotos && IsNoVideoFormatsError(ex.Message))
                        {
                            photoOnlyPost = true;
                            LogService.Write($"[{item.Id}] 영상 형식 없음. 사진 엔진으로 계속합니다: {ex.Message}");
                        }
                    }

                    if (_settings.DownloadPhotos)
                    {
                        await _toolService.EnsurePhotoEngineInstalledAsync(cancellationToken: cancellationToken);
                        var photoProgress = new Progress<DownloadProgress>(value =>
                        {
                            item.Progress = photoOnlyPost ? value.Percent : 70 + value.Percent * 0.3;
                            item.Speed = value.Speed;
                            item.Eta = value.Eta;
                            item.Detail = value.Detail;
                            SetStatus($"사진 다운로드 중: {item.Title}");
                        });

                        try
                        {
                            var photoResult = await ExecuteWithCookieLockRecoveryAsync(
                                token => _galleryDlService.DownloadPhotosAsync(
                                    item,
                                    _settings,
                                    photoProgress,
                                    line => LogService.Write($"[{item.Id}][gallery-dl] {line}"),
                                    token),
                                cancellationToken);
                            outputFiles.AddRange(photoResult.OutputFiles);
                            wasAlreadyDownloaded |= photoResult.WasAlreadyDownloaded;
                        }
                        catch (YtDlpException ex) when (outputFiles.Count > 0)
                        {
                            photoWarning = ex.Message;
                            LogService.Write($"[{item.Id}] 사진 다운로드 일부 실패: {ex}");
                        }
                    }

                    var distinctFiles = outputFiles
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (distinctFiles.Length == 0 && !wasAlreadyDownloaded)
                        throw new YtDlpException("게시물에서 다운로드할 사진이나 영상을 찾지 못했습니다. 로그인 쿠키와 게시물 접근 권한을 확인해 주세요.", false);

                    var photoCount = distinctFiles.Count(IsPhotoFile);
                    var videoCount = distinctFiles.Length - photoCount;
                    item.Progress = 100;
                    item.Speed = string.Empty;
                    item.Eta = string.Empty;
                    item.Status = DownloadStatus.Completed;
                    item.CompletedAt = DateTime.Now;
                    item.OutputPath = distinctFiles.LastOrDefault() ?? _settings.OutputDirectory;
                    item.ErrorMessage = photoWarning ?? string.Empty;
                    item.Detail = BuildCompletionText(photoCount, videoCount, wasAlreadyDownloaded, photoWarning);

                    _downloadArchiveService.Record(item.Url, item.MediaId, item.Title, item.OutputPath);
                    SetStatus(wasAlreadyDownloaded && distinctFiles.Length == 0
                        ? $"이미 다운로드됨: {item.Title}"
                        : $"완료: {item.Title}");
                    return;
                }
                catch (YtDlpException ex) when (ex.Retryable && attempt < totalAttempts)
                {
                    item.ErrorMessage = ex.Message;
                    item.Status = DownloadStatus.WaitingToRetry;
                    var delay = Math.Clamp(_settings.AutoRetryDelaySeconds, 1, 60);
                    for (var remaining = delay; remaining > 0; remaining--)
                    {
                        item.Detail = $"오류 발생 · {remaining}초 후 자동 재시도 ({attempt}/{totalAttempts - 1})";
                        SetStatus($"{remaining}초 후 재시도: {item.Title}");
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Canceled;
            item.Detail = "사용자가 취소했습니다. 다시 시작하면 임시 파일에서 이어받습니다.";
            item.ErrorMessage = string.Empty;
            SetStatus("다운로드를 취소했습니다.");
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.Detail = "다운로드 실패";
            item.ErrorMessage = ex.Message;
            LogService.Write($"[{item.Id}] Download failed: {ex}");
            SetStatus($"실패: {item.Title}");
        }
        finally
        {
            item.Cancellation?.Dispose();
            item.Cancellation = null;
            SaveHistory();
        }
    }

    private static bool IsNoVideoFormatsError(string message) =>
        message.Contains("No video formats", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("no formats found", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("requested format is not available", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhotoFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".heif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jxl", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompletionText(int photoCount, int videoCount, bool alreadyDownloaded, string? warning)
    {
        if (alreadyDownloaded && photoCount == 0 && videoCount == 0)
            return "중복 방지 · 이미 다운로드된 게시물";

        var parts = new List<string>();
        if (photoCount > 0)
            parts.Add($"사진 {photoCount}장");
        if (videoCount > 0)
            parts.Add($"영상 {videoCount}개");
        if (parts.Count == 0)
            parts.Add("다운로드 완료");
        if (!string.IsNullOrWhiteSpace(warning))
            parts.Add("사진 일부 실패");

        return "완료 · " + string.Join(" · ", parts);
    }

    private static void ApplyMediaInfo(DownloadItem item, MediaInfo info)
    {
        item.Title = info.Title;
        item.Author = info.Author;
        item.MediaId = info.MediaId;
        item.ThumbnailUrl = info.ThumbnailUrl;
        item.MediaSummary = $"{info.MediaCountText} · {info.DurationText} · {info.UploadDateText}";
    }

    private void CancelItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DownloadItem item })
            item.Cancellation?.Cancel();
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item })
            return;

        try
        {
            var target = item.OutputPath;
            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            }
            else
            {
                var folder = Directory.Exists(target) ? target : _settings.OutputDirectory;
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            LocalizedMessageBox.Show(ex.Message, "폴더 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DownloadItem item })
            return;

        item.Cancellation?.Cancel();
        item.PropertyChanged -= Item_PropertyChanged;
        DownloadItems.Remove(item);
        SaveHistory();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        var completed = DownloadItems.Where(x => x.Status == DownloadStatus.Completed).ToArray();
        foreach (var item in completed)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            DownloadItems.Remove(item);
        }
        SaveHistory();
        SetStatus($"완료 항목 {completed.Length}개를 지웠습니다.");
    }

    private async void RetryFailed_Click(object sender, RoutedEventArgs e)
    {
        var targets = DownloadItems.Where(x => x.Status is DownloadStatus.Failed or DownloadStatus.Canceled).ToArray();
        foreach (var item in targets)
        {
            item.Status = DownloadStatus.Pending;
            item.Detail = "재시도 대기";
            item.ErrorMessage = string.Empty;
        }

        if (targets.Length == 0)
        {
            SetStatus("재시도할 실패 또는 취소 항목이 없습니다.");
            return;
        }

        await StartQueueAsync();
    }

    private async void EngineManager_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadItems.Any(x => x.Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry))
        {
            LocalizedMessageBox.Show("다운로드가 끝난 뒤 엔진 관리 화면을 열어 주세요.", "엔진 관리", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettingsFromUi();
        var window = new EngineManagerWindow(_toolService, _settingsService, _settings)
        {
            Owner = this
        };
        window.ShowDialog();

        try
        {
            EngineVersionText.Text = LocalizationService.Translate(await GetEngineVersionTextAsync());
        }
        catch
        {
            EngineVersionText.Text = LocalizationService.Translate("확인 실패");
        }
    }

    private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            AddToQueue_Click(sender, e);
        }
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItem.Status))
            UpdateQueueUi();
    }

    private void UpdateQueueUi()
    {
        QueueCountText.Text = LocalizationService.Translate($"{DownloadItems.Count}개");
        EmptyStateBorder.Visibility = DownloadItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!LocalizationService.IsKorean)
            _ = Dispatcher.InvokeAsync(() => LocalizationService.LocalizeWindow(this), DispatcherPriority.ContextIdle);
    }

    private void SaveHistory() => _historyService.Save(DownloadItems);

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _clipboardTimer.Stop();
        _previewCancellation?.Cancel();
        foreach (var item in DownloadItems)
            item.Cancellation?.Cancel();

        _ytDlpService.CancelAll();
        _galleryDlService.CancelAll();
        SaveSettingsFromUi();
        SaveHistory();
    }

    private static List<string> ExtractInstagramUrls(string text)
    {
        var urls = new List<string>();
        foreach (Match match in UrlRegex.Matches(text ?? string.Empty))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']', '}', '"', '\'');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;

            var host = uri.Host.ToLowerInvariant();
            if (host == "instagram.com" || host.EndsWith(".instagram.com", StringComparison.Ordinal))
                urls.Add(candidate);
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<T> ExecuteWithCookieLockRecoveryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (BrowserCookieProfileNotFoundException) when (_settings.CookieBrowser == "firefox")
        {
            var previousProfile = GetCookieProfileFromUi();
            if (!TrySelectDetectedFirefoxProfile(previousProfile))
            {
                throw new YtDlpException(
                    "Firefox 쿠키 프로필을 찾지 못했습니다. Firefox를 실행해 Instagram에 로그인한 뒤 '프로필 새로고침' 또는 '브라우저 자동 감지'를 눌러 주세요.",
                    false);
            }

            SetStatus("실제 Firefox 프로필을 찾아 작업을 다시 시도합니다.");
            try
            {
                return await operation(cancellationToken);
            }
            catch (BrowserCookieProfileNotFoundException)
            {
                throw new YtDlpException(
                    "감지된 Firefox 프로필에서도 cookies.sqlite를 찾지 못했습니다. Firefox에서 Instagram에 로그인한 뒤 프로필 새로고침을 실행해 주세요.",
                    false);
            }
        }
        catch (BrowserCookieLockedException) when (_settings.CookieBrowser != "none" && _settings.CookieBrowser != "file" && _settings.CookieBrowser != "auto")
        {
            await _cookieRecoveryGate.WaitAsync(cancellationToken);
            try
            {
                var browser = _settings.CookieBrowser;
                var displayName = BrowserProcessService.GetDisplayName(browser);
                var runningCount = BrowserProcessService.GetRunningProcessCount(browser);

                if (runningCount > 0)
                {
                    var answer = LocalizedMessageBox.Show(
                        $"{displayName}가 쿠키 데이터베이스를 사용 중이라 읽을 수 없습니다.\n\n" +
                        $"InstaSave가 {displayName}의 모든 창과 백그라운드 프로세스를 종료하고 자동으로 다시 시도할까요?\n\n" +
                        "열려 있던 탭은 일반적으로 다음 실행 시 복원되지만, 작성 중인 내용은 저장되지 않을 수 있습니다.",
                        "브라우저 쿠키 잠금 감지",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                    if (answer != MessageBoxResult.Yes)
                    {
                        throw new YtDlpException(
                            $"{displayName}를 완전히 종료한 뒤 다시 시도해 주세요. 설정 영역의 '선택 브라우저 종료' 버튼으로 백그라운드 프로세스까지 정리할 수 있습니다.",
                            false);
                    }

                    SetStatus($"쿠키 잠금 해제를 위해 {displayName}을 종료하는 중입니다.");
                    var result = await BrowserProcessService.CloseBrowserAsync(browser, cancellationToken);
                    if (result.RemainingProcessCount > 0)
                    {
                        throw new YtDlpException(
                            $"{displayName} 프로세스 {result.RemainingProcessCount}개가 남아 있어 쿠키 잠금을 해제하지 못했습니다. InstaSave를 관리자 권한으로 실행하거나 작업 관리자에서 직접 종료해 주세요.",
                            false);
                    }
                }

                await Task.Delay(700, cancellationToken);
                SetStatus("브라우저 쿠키 잠금이 해제되어 작업을 다시 시도합니다.");

                try
                {
                    return await operation(cancellationToken);
                }
                catch (BrowserCookieLockedException)
                {
                    throw new YtDlpException(
                        $"{displayName} 종료 후에도 쿠키 데이터베이스 잠금이 유지되고 있습니다. Windows를 다시 시작한 뒤 시도하거나 Firefox 쿠키를 선택해 주세요.",
                        false);
                }
            }
            finally
            {
                _cookieRecoveryGate.Release();
            }
        }
    }

    private string GetCookieProfileFromUi()
    {
        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        var selectedValue = (CookieProfileComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var profile = !string.IsNullOrWhiteSpace(selectedValue)
            ? selectedValue.Trim()
            : CookieProfileComboBox.Text.Trim();

        var resolved = BrowserCookieDetectionService.ResolveProfile(source, profile);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        return source is "firefox" or "opera" ? string.Empty : "Default";
    }

    private string GetCookieCredentialFromUi()
    {
        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        return source == "file" ? CookieFileTextBox.Text.Trim() : GetCookieProfileFromUi();
    }

    private string GetCookieSourceDisplay(string source)
    {
        if (source == "file")
            return $"cookies.txt · {Path.GetFileName(CookieFileTextBox.Text)}";
        if (source == "none")
            return "사용 안 함";
        if (source == "auto")
            return "브라우저 자동 감지";

        var profile = GetCookieProfileFromUi();
        var displayProfile = Path.IsPathRooted(profile)
            ? Path.GetFileName(profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : profile;
        return string.IsNullOrWhiteSpace(displayProfile) ? source : $"{source}:{displayProfile}";
    }

    private bool ValidateCookieSelection()
    {
        var source = GetSelectedTag(CookieBrowserComboBox, "none");
        if (source == "none")
            return true;

        if (source == "auto")
        {
            LocalizedMessageBox.Show(
                "브라우저 자동 감지가 완료되지 않았습니다. '브라우저 자동 감지' 버튼을 눌러 다시 시도해 주세요.",
                "브라우저 자동 감지",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (source == "file")
        {
            var path = CookieFileTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LocalizedMessageBox.Show(
                    "사용할 cookies.txt 파일을 먼저 선택해 주세요.",
                    "cookies.txt 선택",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            try
            {
                var validCookieLine = File.ReadLines(path)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                    .Take(200)
                    .Any(line => line.Split('\t').Length >= 7);

                if (!validCookieLine)
                {
                    LocalizedMessageBox.Show(
                        "선택한 파일이 Netscape 형식 cookies.txt로 보이지 않습니다.\n\n브라우저 확장 프로그램에서 Instagram 쿠키를 Netscape 형식으로 내보낸 파일을 선택해 주세요.",
                        "cookies.txt 형식 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.Show(
                    "cookies.txt 파일을 읽지 못했습니다.\n\n" + ex.Message,
                    "cookies.txt 읽기 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        if (source == "firefox")
        {
            var profile = GetCookieProfileFromUi();
            if (!BrowserCookieDetectionService.IsUsableFirefoxProfile(profile))
            {
                RefreshCookieProfileChoices("firefox", profile);
                profile = GetCookieProfileFromUi();
            }

            if (BrowserCookieDetectionService.IsUsableFirefoxProfile(profile))
                return true;

            LocalizedMessageBox.Show(
                "Firefox의 실제 쿠키 프로필을 찾지 못했습니다.\n\nFirefox에서 Instagram에 로그인한 뒤 '프로필 새로고침' 또는 '브라우저 자동 감지'를 눌러 주세요. Firefox 프로필은 Default가 아니라 임의문자.default-release 형태입니다.",
                "Firefox 프로필을 찾지 못함",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            CookieProfileComboBox.Focus();
            return false;
        }

        if (!string.IsNullOrWhiteSpace(GetCookieProfileFromUi()))
            return true;

        LocalizedMessageBox.Show(
            "브라우저 쿠키를 사용하려면 프로필을 직접 선택하거나 입력해 주세요.\n\nBrave/Vivaldi 예: Default, Profile 1",
            "브라우저 프로필 선택",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        CookieProfileComboBox.Focus();
        return false;
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static string GetSelectedTag(System.Windows.Controls.ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static int ParseSelectedInt(System.Windows.Controls.ComboBox comboBox, int fallback) =>
        int.TryParse(GetSelectedTag(comboBox, fallback.ToString()), out var value) ? value : fallback;

    private void SetStatus(string message) => StatusText.Text = LocalizationService.Translate(message);
}
