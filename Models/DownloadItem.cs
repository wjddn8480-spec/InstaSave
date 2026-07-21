using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using InstaSave.Services;

namespace InstaSave.Models;

public enum DownloadStatus
{
    Pending,
    Analyzing,
    Downloading,
    WaitingToRetry,
    Completed,
    Failed,
    Canceled
}

public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _title = "분석 대기 중";
    private string _author = string.Empty;
    private string _mediaId = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private string _mediaSummary = string.Empty;
    private double _progress;
    private string _speed = string.Empty;
    private string _eta = string.Empty;
    private DownloadStatus _status = DownloadStatus.Pending;
    private string _outputPath = string.Empty;
    private string _errorMessage = string.Empty;
    private string _detail = "대기 중";
    private int _retryAttempt;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public bool AllowDuplicate { get; set; }

    public string Title
    {
        get => _title is "분석 대기 중" or "Instagram 게시물" or "Instagram 사진 게시물"
            ? LocalizationService.Translate(_title)
            : _title;
        set => SetField(ref _title, value);
    }

    public string Author
    {
        get => _author;
        set => SetField(ref _author, value);
    }

    public string MediaId
    {
        get => _mediaId;
        set => SetField(ref _mediaId, value);
    }

    public string ThumbnailUrl
    {
        get => _thumbnailUrl;
        set
        {
            if (SetField(ref _thumbnailUrl, value))
                OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public string MediaSummary
    {
        get => LocalizationService.Translate(_mediaSummary);
        set => SetField(ref _mediaSummary, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string Speed
    {
        get => _speed;
        set => SetField(ref _speed, value);
    }

    public string Eta
    {
        get => _eta;
        set
        {
            if (SetField(ref _eta, value))
                OnPropertyChanged(nameof(EtaDisplay));
        }
    }

    [JsonIgnore]
    public string EtaDisplay => string.IsNullOrWhiteSpace(Eta)
        ? string.Empty
        : LocalizationService.IsKorean ? $"  · 남은 시간 {Eta}" : $"  · Time left {Eta}";

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetField(ref _outputPath, value))
                OnPropertyChanged(nameof(CanOpen));
        }
    }

    public string ErrorMessage
    {
        get => LocalizationService.Translate(_errorMessage);
        set => SetField(ref _errorMessage, value);
    }

    public string Detail
    {
        get => LocalizationService.Translate(_detail);
        set => SetField(ref _detail, value);
    }

    public int RetryAttempt
    {
        get => _retryAttempt;
        set => SetField(ref _retryAttempt, value);
    }

    [JsonIgnore]
    public CancellationTokenSource? Cancellation { get; set; }

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailUrl);

    [JsonIgnore]
    public string StatusText => LocalizationService.Translate(Status switch
    {
        DownloadStatus.Pending => "대기",
        DownloadStatus.Analyzing => "분석 중",
        DownloadStatus.Downloading => "다운로드 중",
        DownloadStatus.WaitingToRetry => "재시도 대기",
        DownloadStatus.Completed => "완료",
        DownloadStatus.Failed => "실패",
        DownloadStatus.Canceled => "취소됨",
        _ => Status.ToString()
    });

    [JsonIgnore]
    public Brush StatusBrush => Status switch
    {
        DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(23, 138, 99)),
        DownloadStatus.Failed => new SolidColorBrush(Color.FromRgb(200, 62, 69)),
        DownloadStatus.Canceled => new SolidColorBrush(Color.FromRgb(104, 115, 134)),
        DownloadStatus.Downloading => new SolidColorBrush(Color.FromRgb(225, 48, 108)),
        DownloadStatus.Analyzing => new SolidColorBrush(Color.FromRgb(169, 104, 19)),
        DownloadStatus.WaitingToRetry => new SolidColorBrush(Color.FromRgb(169, 104, 19)),
        _ => new SolidColorBrush(Color.FromRgb(104, 115, 134))
    };

    [JsonIgnore]
    public bool CanStart => Status is DownloadStatus.Pending or DownloadStatus.Failed or DownloadStatus.Canceled;

    [JsonIgnore]
    public bool CanCancel => Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.WaitingToRetry;

    [JsonIgnore]
    public bool CanOpen => Status == DownloadStatus.Completed && !string.IsNullOrWhiteSpace(OutputPath);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
