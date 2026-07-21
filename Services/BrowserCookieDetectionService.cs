namespace InstaSave.Services;

public sealed record BrowserCookieCandidate(
    string Browser,
    string BrowserDisplayName,
    string Profile,
    string ProfileDisplayName,
    DateTime LastUsedUtc)
{
    public string DisplayText => string.IsNullOrWhiteSpace(ProfileDisplayName)
        ? BrowserDisplayName
        : $"{BrowserDisplayName} · {ProfileDisplayName}";
}

public static class BrowserCookieDetectionService
{
    public static IReadOnlyList<BrowserCookieCandidate> FindCandidates(string? browser = null)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new List<BrowserCookieCandidate>();

        AddFirefoxCandidates(
            candidates,
            Path.Combine(roaming, "Mozilla", "Firefox"),
            "일반 설치");
        AddMicrosoftStoreFirefoxCandidates(candidates, local);

        AddChromiumCandidates(candidates, "brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
        AddChromiumCandidates(candidates, "vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data"));
        AddOperaCandidate(candidates, "Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"));

        var result = candidates
            .Where(candidate => string.IsNullOrWhiteSpace(browser) ||
                                candidate.Browser.Equals(browser, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => $"{candidate.Browser}|{candidate.Profile}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.LastUsedUtc).First())
            .OrderBy(candidate => GetBrowserPriority(candidate.Browser))
            .ThenByDescending(candidate => candidate.LastUsedUtc)
            .ToArray();

        return result;
    }

    public static string ResolveProfile(string browser, string? profile)
    {
        var value = profile?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return GetDefaultProfile(browser);

        var candidates = FindCandidates(browser);
        var direct = candidates.FirstOrDefault(candidate =>
            candidate.Profile.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
            return direct.Profile;

        var byFolderName = candidates.FirstOrDefault(candidate =>
            string.Equals(
                Path.GetFileName(candidate.Profile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                value,
                StringComparison.OrdinalIgnoreCase));
        if (byFolderName is not null)
            return byFolderName.Profile;

        var byDisplayName = candidates.FirstOrDefault(candidate =>
            candidate.ProfileDisplayName.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            candidate.ProfileDisplayName.StartsWith(value + " ·", StringComparison.OrdinalIgnoreCase));
        return byDisplayName?.Profile ?? value;
    }

    public static string GetDefaultProfile(string browser)
    {
        var detected = FindCandidates(browser).FirstOrDefault();
        if (detected is not null)
            return detected.Profile;

        return browser.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
               browser.Equals("opera", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : "Default";
    }

    public static bool IsUsableFirefoxProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return false;

        try
        {
            var resolved = ResolveProfile("firefox", profile);
            return Directory.Exists(resolved) && File.Exists(Path.Combine(resolved, "cookies.sqlite"));
        }
        catch
        {
            return false;
        }
    }

    private static void AddChromiumCandidates(
        ICollection<BrowserCookieCandidate> candidates,
        string browser,
        string displayName,
        string userDataDirectory)
    {
        if (!Directory.Exists(userDataDirectory))
            return;

        var profileDirectories = new List<DirectoryInfo>();
        TryAddDirectory(profileDirectories, Path.Combine(userDataDirectory, "Default"));

        try
        {
            profileDirectories.AddRange(new DirectoryInfo(userDataDirectory)
                .EnumerateDirectories("Profile *", SearchOption.TopDirectoryOnly));
        }
        catch
        {
            // 접근할 수 없는 프로필 폴더는 건너뜁니다.
        }

        if (profileDirectories.Count == 0)
        {
            candidates.Add(new BrowserCookieCandidate(
                browser,
                displayName,
                "Default",
                "Default",
                GetLastUsedUtc(userDataDirectory)));
            return;
        }

        foreach (var directory in profileDirectories
                     .GroupBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            candidates.Add(new BrowserCookieCandidate(
                browser,
                displayName,
                directory.Name,
                directory.Name,
                GetLastUsedUtc(directory.FullName)));
        }
    }

    private static void AddMicrosoftStoreFirefoxCandidates(
        ICollection<BrowserCookieCandidate> candidates,
        string localApplicationData)
    {
        var packagesDirectory = Path.Combine(localApplicationData, "Packages");
        if (!Directory.Exists(packagesDirectory))
            return;

        try
        {
            foreach (var packageDirectory in Directory.EnumerateDirectories(
                         packagesDirectory,
                         "Mozilla.Firefox*",
                         SearchOption.TopDirectoryOnly))
            {
                var firefoxDirectory = Path.Combine(
                    packageDirectory,
                    "LocalCache",
                    "Roaming",
                    "Mozilla",
                    "Firefox");
                AddFirefoxCandidates(candidates, firefoxDirectory, "Microsoft Store");
            }
        }
        catch
        {
            // Microsoft Store 앱 폴더에 접근할 수 없으면 일반 설치 경로만 사용합니다.
        }
    }

    private static void AddFirefoxCandidates(
        ICollection<BrowserCookieCandidate> candidates,
        string firefoxDirectory,
        string sourceLabel)
    {
        var profilesDirectory = Path.Combine(firefoxDirectory, "Profiles");
        if (!Directory.Exists(profilesDirectory))
            return;

        DirectoryInfo[] profiles;
        try
        {
            profiles = new DirectoryInfo(profilesDirectory).GetDirectories();
        }
        catch
        {
            return;
        }

        foreach (var profile in profiles
                     .Where(item => File.Exists(Path.Combine(item.FullName, "cookies.sqlite")))
                     .OrderByDescending(item => item.Name.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase))
                     .ThenByDescending(item => item.Name.EndsWith(".default", StringComparison.OrdinalIgnoreCase))
                     .ThenByDescending(item => GetLastUsedUtc(item.FullName)))
        {
            candidates.Add(new BrowserCookieCandidate(
                "firefox",
                "Firefox",
                profile.FullName,
                $"{profile.Name} · {sourceLabel}",
                GetLastUsedUtc(profile.FullName)));
        }
    }

    private static void AddOperaCandidate(
        ICollection<BrowserCookieCandidate> candidates,
        string displayName,
        string profileDirectory)
    {
        if (!Directory.Exists(profileDirectory))
            return;

        candidates.Add(new BrowserCookieCandidate(
            "opera",
            displayName,
            profileDirectory,
            "기본 프로필",
            GetLastUsedUtc(profileDirectory)));
    }

    private static void TryAddDirectory(ICollection<DirectoryInfo> directories, string path)
    {
        try
        {
            if (Directory.Exists(path))
                directories.Add(new DirectoryInfo(path));
        }
        catch
        {
            // 경로 검사 실패는 무시합니다.
        }
    }

    private static DateTime GetLastUsedUtc(string profileDirectory)
    {
        var cookieCandidates = new[]
        {
            Path.Combine(profileDirectory, "Network", "Cookies"),
            Path.Combine(profileDirectory, "Cookies"),
            Path.Combine(profileDirectory, "cookies.sqlite")
        };

        foreach (var path in cookieCandidates)
        {
            try
            {
                if (File.Exists(path))
                    return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                // 다음 후보를 확인합니다.
            }
        }

        try
        {
            return Directory.GetLastWriteTimeUtc(profileDirectory);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static int GetBrowserPriority(string browser) => browser.ToLowerInvariant() switch
    {
        "firefox" => 0,
        "brave" => 1,
        "vivaldi" => 2,
        "opera" => 3,
        _ => 99
    };
}
