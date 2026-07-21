using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace InstaSave.Services;

/// <summary>
/// Shows non-blocking Windows notifications from the notification area.
/// No modal MessageBox is used for download completion.
/// </summary>
public sealed class WindowsNotificationService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public WindowsNotificationService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "InstaSave",
            Visible = true,
            Icon = LoadApplicationIcon()
        };
    }

    public void ShowDownloadSummary(int completedCount, int failedCount)
    {
        if (completedCount <= 0 && failedCount <= 0)
            return;

        string title;
        string message;
        ToolTipIcon icon;

        if (failedCount == 0)
        {
            title = LocalizationService.Translate("다운로드 완료");
            message = completedCount == 1
                ? LocalizationService.Translate("파일 다운로드가 완료되었습니다.")
                : LocalizationService.Translate($"{completedCount}개 항목의 다운로드가 완료되었습니다.");
            icon = ToolTipIcon.Info;
        }
        else if (completedCount == 0)
        {
            title = LocalizationService.Translate("다운로드 실패");
            message = failedCount == 1
                ? LocalizationService.Translate("파일 다운로드에 실패했습니다.")
                : LocalizationService.Translate($"{failedCount}개 항목의 다운로드에 실패했습니다.");
            icon = ToolTipIcon.Error;
        }
        else
        {
            title = LocalizationService.Translate("다운로드 작업 완료");
            message = LocalizationService.Translate($"완료 {completedCount}개, 실패 {failedCount}개");
            icon = ToolTipIcon.Warning;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                var icon = Icon.ExtractAssociatedIcon(executablePath);
                if (icon is not null)
                    return icon;
            }
        }
        catch
        {
            // Fall back to a standard Windows icon.
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
