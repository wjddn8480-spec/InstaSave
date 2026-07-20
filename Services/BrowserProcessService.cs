using System.Diagnostics;

namespace InstaSave.Services;

public sealed record BrowserCloseResult(
    string BrowserDisplayName,
    int FoundProcessCount,
    int ClosedProcessCount,
    int RemainingProcessCount);

public static class BrowserProcessService
{
    private static readonly IReadOnlyDictionary<string, BrowserDefinition> Browsers =
        new Dictionary<string, BrowserDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["firefox"] = new("Firefox", ["firefox"]),
            ["brave"] = new("Brave", ["brave"]),
            ["opera"] = new("Opera", ["opera"]),
            ["vivaldi"] = new("Vivaldi", ["vivaldi"])
        };

    public static string GetDisplayName(string browser) =>
        Browsers.TryGetValue(browser, out var definition) ? definition.DisplayName : browser;

    public static int GetRunningProcessCount(string browser)
    {
        var processes = GetProcesses(browser);
        try
        {
            return processes.Count;
        }
        finally
        {
            DisposeProcesses(processes);
        }
    }

    public static async Task<BrowserCloseResult> CloseBrowserAsync(
        string browser,
        CancellationToken cancellationToken)
    {
        if (!Browsers.TryGetValue(browser, out var definition))
            return new BrowserCloseResult(browser, 0, 0, 0);

        var processes = GetProcesses(browser);
        var foundCount = processes.Count;

        // 먼저 정상 종료를 요청해 세션 복원 정보와 브라우저 설정이 안전하게 저장되도록 합니다.
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    process.CloseMainWindow();
            }
            catch
            {
                // 보호된 하위 프로세스는 강제 종료 단계에서 다시 처리합니다.
            }
        }
        DisposeProcesses(processes);

        var gracefulDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < gracefulDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetRunningProcessCount(browser) == 0)
                break;
            await Task.Delay(150, cancellationToken);
        }

        // 백그라운드 실행과 트레이 프로세스가 남아 있으면 프로세스 트리까지 종료합니다.
        var remainingBeforeKill = GetProcesses(browser);
        foreach (var process in remainingBeforeKill)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 권한이 부족한 프로세스는 결과의 RemainingProcessCount로 안내합니다.
            }
        }
        DisposeProcesses(remainingBeforeKill);

        var forceDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < forceDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetRunningProcessCount(browser) == 0)
                break;
            await Task.Delay(150, cancellationToken);
        }

        var remainingCount = GetRunningProcessCount(browser);
        var closedCount = Math.Max(0, foundCount - remainingCount);
        return new BrowserCloseResult(definition.DisplayName, foundCount, closedCount, remainingCount);
    }

    private static List<Process> GetProcesses(string browser)
    {
        if (!Browsers.TryGetValue(browser, out var definition))
            return [];

        var result = new Dictionary<int, Process>();
        foreach (var processName in definition.ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                if (!result.TryAdd(process.Id, process))
                    process.Dispose();
            }
        }

        return result.Values.ToList();
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Dispose();
            }
            catch
            {
                // 프로세스 종료와 동시에 핸들이 무효화될 수 있습니다.
            }
        }
    }

    private sealed record BrowserDefinition(string DisplayName, string[] ProcessNames);
}
