using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace InstaSave.Services;

public static partial class LocalizationService
{
    private static string _languageMode = "auto";

    public static string LanguageMode => _languageMode;

    private static readonly HashSet<string> SupportedLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ko", "en", "ja", "zh-Hans", "zh-Hant", "de", "fr", "es", "it", "pt", "ru", "tr", "pl", "nl", "cs", "uk", "ar", "th", "vi", "id"
    };

    public static string LanguageCode => ResolveLanguageCode(_languageMode);
    public static bool IsKorean => string.Equals(LanguageCode, "ko", StringComparison.OrdinalIgnoreCase);

    public static void SetLanguageMode(string? mode)
    {
        var normalized = NormalizeLanguageCode(mode);
        _languageMode = string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : SupportedLanguageCodes.Contains(normalized) ? normalized : "auto";

        var cultureName = LanguageCode switch
        {
            "zh-Hans" => "zh-CN",
            "zh-Hant" => "zh-TW",
            _ => LanguageCode
        };

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch
        {
            // Keep the current Windows culture if a culture cannot be loaded.
        }
    }

    private static string ResolveLanguageCode(string mode)
    {
        if (!string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
            return SupportedLanguageCodes.Contains(mode) ? mode : "en";

        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }

        var twoLetter = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return SupportedLanguageCodes.Contains(twoLetter) ? twoLetter : "en";
    }

    private static string NormalizeLanguageCode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "auto";
        var value = mode.Trim();
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                ? "zh-Hant"
                : "zh-Hans";
        }
        return value.ToLowerInvariant();
    }

    private static readonly HashSet<string> NonLocalizedElementNames = new(StringComparer.Ordinal)
    {
        "PreviewTitleText",
        "PreviewAuthorText",
        "PreviewDescriptionText",
        "YtDlpPathText",
        "GalleryDlPathText",
        "FfmpegStatusText"
    };

    private static readonly Dictionary<string, string> ExactEnglish = new(StringComparer.Ordinal)
    {
        ["Instagram 사진·영상 다운로드 · v1.0.1"] = "Instagram photo & video downloader · v1.0.1",
        ["Instagram 사진·영상 다운로드 · v1.1.0"] = "Instagram photo & video downloader · v1.1.0",
        ["미디어 엔진"] = "Media engines",
        ["언어"] = "Language",
        ["자동"] = "Automatic",
        ["한국어"] = "Korean",
        ["언어 변경"] = "Language change",
        ["다운로드가 진행 중일 때는 언어를 변경할 수 없습니다. 다운로드가 끝난 뒤 다시 시도해 주세요."] = "The language cannot be changed while a download is in progress. Try again after the download finishes.",
        ["확인 중"] = "Checking",
        ["확인 실패"] = "Check failed",
        ["엔진 관리"] = "Engine manager",
        ["여러 URL을 줄바꿈이나 공백으로 한 번에 입력할 수 있습니다. Ctrl+Enter로 모두 대기열에 추가합니다."] = "Enter multiple URLs separated by line breaks or spaces. Press Ctrl+Enter to add all of them to the queue.",
        ["붙여넣기"] = "Paste",
        ["미리보기"] = "Preview",
        ["목록에 추가"] = "Add to queue",
        ["여러 URL 대기열 추가"] = "Add multiple URLs",
        ["Instagram 링크를 복사하면 자동으로 입력할 수 있으며, 여러 URL을 한 번에 대기열에 추가할 수 있습니다."] = "Copied Instagram links can be pasted automatically, and multiple URLs can be added to the queue at once.",
        ["게시물 미리보기"] = "Post preview",
        ["제목"] = "Title",
        ["게시자"] = "Author",
        ["업로드"] = "Uploaded",
        ["재생 시간"] = "Duration",
        ["닫기"] = "Close",
        ["저장 폴더"] = "Save folder",
        ["찾아보기"] = "Browse",
        ["화질"] = "Quality",
        ["최고 화질"] = "Best quality",
        ["최대 1080p"] = "Up to 1080p",
        ["최대 720p"] = "Up to 720p",
        ["최대 480p"] = "Up to 480p",
        ["쿠키 사용 방식"] = "Cookie source",
        ["사용 안 함"] = "Do not use cookies",
        ["브라우저 자동 감지"] = "Auto-detect browser",
        ["cookies.txt 파일 직접 선택"] = "Select cookies.txt file",
        ["자동 감지는 Firefox, Brave, Opera, Vivaldi 프로필을 검사하며, 사용할 수 없는 후보는 건너뜁니다."] = "Auto-detection checks Firefox, Brave, Opera, and Vivaldi profiles and skips unavailable candidates.",
        ["브라우저 프로필"] = "Browser profile",
        ["감지된 브라우저 프로필을 선택하거나 실제 프로필 경로를 직접 입력하세요."] = "Select a detected browser profile or enter the actual profile path.",
        ["새로고침"] = "Refresh",
        ["선택한 브라우저의 실제 프로필 목록을 다시 검색합니다."] = "Search again for the selected browser's actual profiles.",
        ["Firefox는 실제 프로필 폴더를 자동으로 검색하며, Microsoft Store 설치 경로도 지원합니다."] = "Firefox profile folders are detected automatically, including Microsoft Store installations.",
        ["cookies.txt 파일"] = "cookies.txt file",
        ["Netscape 형식 cookies.txt 파일 경로"] = "Path to a Netscape-format cookies.txt file",
        ["파일 선택"] = "Choose file",
        ["Instagram 로그인 쿠키가 포함된 Netscape 형식 파일만 사용합니다."] = "Use only a Netscape-format file that contains Instagram login cookies.",
        ["파일 이름 형식"] = "File name format",
        ["작성자_날짜_ID"] = "Uploader_Date_ID",
        ["날짜_작성자_제목_ID"] = "Date_Uploader_Title_ID",
        ["제목_ID"] = "Title_ID",
        ["날짜_제목_ID"] = "Date_Title_ID",
        ["작성자별 폴더"] = "Folder by uploader",
        ["게시물 ID"] = "Post ID",
        ["사용자 지정"] = "Custom",
        ["사용자 지정 파일 이름"] = "Custom file name",
        ["사용 가능: {uploader}, {date}, {id}, {title}, {resolution}, {index}"] = "Available: {uploader}, {date}, {id}, {title}, {resolution}, {index}",
        ["사용 가능: {uploader} {date} {id} {title} {resolution} {index}"] = "Available: {uploader} {date} {id} {title} {resolution} {index}",
        ["자동 재시도 횟수"] = "Automatic retry count",
        ["1회"] = "1 time",
        ["2회"] = "2 times",
        ["3회"] = "3 times",
        ["4회"] = "4 times",
        ["5회"] = "5 times",
        ["재시도 대기 시간"] = "Retry delay",
        ["3초"] = "3 seconds",
        ["5초"] = "5 seconds",
        ["10초"] = "10 seconds",
        ["20초"] = "20 seconds",
        ["쿠키 접근 진단"] = "Cookie access diagnostics",
        ["자동 감지 중..."] = "Detecting...",
        ["선택한 쿠키 검사"] = "Test selected cookies",
        ["선택한 브라우저 쿠키 검사"] = "Test selected browser cookies",
        ["자동 감지 후 쿠키 검사"] = "Auto-detect and test cookies",
        ["cookies.txt 접근 검사"] = "Test cookies.txt access",
        ["선택 브라우저 종료"] = "Close selected browser",
        ["쿠키 DB 잠금을 해제하기 위해 선택한 브라우저의 창과 백그라운드 프로세스를 종료합니다."] = "Close the selected browser's windows and background processes to release its cookie database.",
        ["브라우저 DB 잠금은 자동 종료 후 재시도할 수 있습니다."] = "A locked browser database can be handled by closing the browser and retrying automatically.",
        ["엔진 및 FFmpeg"] = "Engines and FFmpeg",
        ["엔진 관리 열기"] = "Open engine manager",
        ["게시물의 모든 미디어 다운로드"] = "Download all media in the post",
        ["사진도 다운로드"] = "Download photos",
        ["사진 전용 게시물과 사진·영상 혼합 게시물의 사진을 함께 저장합니다."] = "Save photos from photo-only and mixed photo/video posts.",
        ["썸네일 함께 저장"] = "Save thumbnail",
        ["URL 추가 후 자동 시작"] = "Start automatically after adding a URL",
        ["클립보드 Instagram URL 자동 감지"] = "Detect Instagram URLs in the clipboard",
        ["복사한 Instagram 링크 자동 붙여넣기"] = "Automatically paste copied Instagram links",
        ["복사한 링크 자동 대기열 추가"] = "Automatically add copied links to the queue",
        ["중복 다운로드 방지"] = "Prevent duplicate downloads",
        ["실패 시 자동 재시도"] = "Retry automatically after failure",
        ["다운로드 목록"] = "Download queue",
        ["0개"] = "0 items",
        ["전체 시작"] = "Start all",
        ["실패 항목 재시도"] = "Retry failed items",
        ["완료 항목 지우기"] = "Clear completed items",
        ["시작/재시도"] = "Start/Retry",
        ["취소"] = "Cancel",
        ["폴더 열기"] = "Open folder",
        ["삭제"] = "Remove",
        ["아직 다운로드 항목이 없습니다."] = "There are no download items yet.",
        ["위에 Instagram URL을 입력하거나 클립보드 자동 감지를 켜 주세요."] = "Enter an Instagram URL above or enable clipboard detection.",
        ["준비됨"] = "Ready",
        ["본인이 소유하거나 다운로드 권한이 있는 콘텐츠만 이용하세요."] = "Use only content you own or have permission to download.",

        ["미디어 엔진 관리"] = "Media engine manager",
        ["영상용 yt-dlp, 사진용 gallery-dl, FFmpeg 경로와 진단 파일을 관리합니다."] = "Manage yt-dlp for videos, gallery-dl for photos, FFmpeg paths, and diagnostic files.",
        ["영상 엔진 · yt-dlp"] = "Video engine · yt-dlp",
        ["사진 엔진 · gallery-dl"] = "Photo engine · gallery-dl",
        ["버전"] = "Version",
        ["엔진 위치"] = "Engine location",
        ["이전 버전 백업"] = "Previous-version backup",
        ["복원 가능"] = "Restore available",
        ["백업 없음"] = "No backup",
        ["영상 엔진 관리"] = "Video engine management",
        ["영상 엔진 업데이트"] = "Update video engine",
        ["영상 엔진 재설치"] = "Reinstall video engine",
        ["영상 엔진 복원"] = "Restore video engine",
        ["영상 캐시 초기화"] = "Clear video cache",
        ["사진 엔진 관리"] = "Photo engine management",
        ["사진 엔진 업데이트"] = "Update photo engine",
        ["사진 엔진 재설치"] = "Reinstall photo engine",
        ["사진 엔진 복원"] = "Restore photo engine",
        ["사진 캐시 초기화"] = "Clear photo cache",
        ["도구 폴더 열기"] = "Open tools folder",
        ["로그 열기"] = "Open log",
        ["FFmpeg 경로"] = "FFmpeg path",
        ["ffmpeg.exe가 들어 있는 폴더를 지정하세요. 비워 두면 시스템 PATH와 앱 도구 폴더에서 자동 검색합니다."] = "Choose the folder containing ffmpeg.exe. Leave it blank to search the system PATH and the app tools folder automatically.",
        ["폴더 선택"] = "Choose folder",
        ["경로 지우기"] = "Clear path",

        ["분석 대기 중"] = "Waiting for analysis",
        ["대기 중"] = "Waiting",
        ["대기"] = "Pending",
        ["분석 중"] = "Analyzing",
        ["다운로드 중"] = "Downloading",
        ["재시도 대기"] = "Waiting to retry",
        ["완료"] = "Completed",
        ["실패"] = "Failed",
        ["취소됨"] = "Canceled",
        ["다운로드 실패"] = "Download failed",
        ["다운로드 준비 중"] = "Preparing download",
        ["게시물 정보를 확인하는 중"] = "Checking post information",
        ["강제 재다운로드 대기"] = "Waiting for forced re-download",
        ["이전 실행이 중단됨 · 재시도하면 이어받습니다."] = "The previous run was interrupted · Retry to resume.",
        ["사용자가 취소했습니다. 다시 시작하면 임시 파일에서 이어받습니다."] = "Canceled by the user. Restart to resume from the temporary file.",
        ["다운로드 완료"] = "Download complete",
        ["사진 다운로드 완료"] = "Photo download complete",
        ["사진 일부 실패"] = "Some photos failed",
        ["중복 방지 · 이미 다운로드된 게시물"] = "Duplicate prevention · Post already downloaded",
        ["확인 불가"] = "Unavailable",
        ["게시물 설명이 없습니다."] = "No post description is available.",

        ["InstaSave 오류"] = "InstaSave error",
        ["엔진 설치 실패"] = "Engine installation failed",
        ["설치 실패"] = "Installation failed",
        ["미리보기 실패"] = "Preview failed",
        ["쿠키 검사"] = "Cookie test",
        ["쿠키 검사 성공"] = "Cookie test successful",
        ["쿠키 검사 실패"] = "Cookie test failed",
        ["브라우저 종료"] = "Close browser",
        ["브라우저 종료 완료"] = "Browser closed",
        ["브라우저 종료 실패"] = "Failed to close browser",
        ["URL 확인"] = "Check URL",
        ["중복 다운로드 확인"] = "Duplicate download confirmation",
        ["다운로드 오류"] = "Download error",
        ["폴더 열기 실패"] = "Failed to open folder",
        ["엔진 관리 오류"] = "Engine manager error",
        ["브라우저 자동 감지 실패"] = "Browser auto-detection failed",
        ["브라우저 자동 감지 완료"] = "Browser auto-detection complete",
        ["브라우저 프로필 새로고침"] = "Refresh browser profiles",
        ["브라우저 쿠키 잠금 감지"] = "Browser cookie lock detected",
        ["cookies.txt 선택"] = "Select cookies.txt",
        ["cookies.txt 형식 오류"] = "Invalid cookies.txt format",
        ["cookies.txt 읽기 오류"] = "Could not read cookies.txt",
        ["Firefox 프로필을 찾지 못함"] = "Firefox profile not found",
        ["브라우저 프로필 선택"] = "Select browser profile",

        ["미디어 엔진을 확인하는 중입니다."] = "Checking media engines.",
        ["미디어 엔진 설치에 실패했습니다."] = "Failed to install the media engines.",
        ["영상 엔진 설치 중"] = "Installing video engine",
        ["사진 엔진 설치 중"] = "Installing photo engine",
        ["쿠키 프로필이 있는 브라우저를 찾지 못했습니다."] = "No browser with a cookie profile was found.",
        ["사용 가능한 브라우저 쿠키를 자동으로 찾지 못했습니다."] = "No usable browser cookies were found automatically.",
        ["선택한 쿠키를 정상적으로 읽었습니다."] = "The selected cookies were read successfully.",
        ["쿠키 검사에 실패했습니다."] = "Cookie testing failed.",
        ["브라우저를 종료하지 못했습니다."] = "The browser could not be closed.",
        ["추가할 새 URL이 없습니다."] = "There are no new URLs to add.",
        ["시작할 다운로드 항목이 없습니다."] = "There are no download items to start.",
        ["현재 다운로드가 끝난 뒤 전체 시작을 눌러 주세요."] = "Wait for the current download to finish, then select Start all.",
        ["대기열 처리가 끝났습니다."] = "Queue processing is complete.",
        ["다른 다운로드가 끝난 뒤 다시 시도해 주세요."] = "Try again after the other download finishes.",
        ["게시물 미리보기를 불러오는 중입니다."] = "Loading the post preview.",
        ["게시물 미리보기를 불러왔습니다."] = "The post preview has loaded.",
        ["미리보기를 취소했습니다."] = "Preview canceled.",
        ["미리보기를 불러오지 못했습니다."] = "Could not load the preview.",
        ["선택한 쿠키와 게시물 접근 권한을 검사하는 중입니다."] = "Testing the selected cookies and post access.",
        ["Instagram 게시물을 분석하는 중입니다."] = "Analyzing the Instagram post.",
        ["다운로드를 취소했습니다."] = "The download was canceled.",
        ["재시도할 실패 또는 취소 항목이 없습니다."] = "There are no failed or canceled items to retry.",
        ["실제 Firefox 프로필을 찾아 작업을 다시 시도합니다."] = "An actual Firefox profile was found. Retrying the operation.",
        ["브라우저 쿠키 잠금이 해제되어 작업을 다시 시도합니다."] = "The browser cookie lock was released. Retrying the operation.",
        ["파일 내용은 앱에 복사하지 않으며 선택한 경로에서 직접 읽습니다."] = "The file is not copied into the app; it is read directly from the selected path.",
        ["설치된 브라우저와 프로필을 순서대로 검사하고, 성공한 항목을 자동 선택합니다."] = "Installed browsers and profiles are checked in order, and the first successful one is selected automatically.",
        ["로그인이 필요한 게시물은 브라우저 자동 감지 또는 수동 쿠키 선택을 사용하세요."] = "For posts that require login, use browser auto-detection or select cookies manually.",
        ["Firefox는 실제 프로필 폴더를 선택해야 합니다. Default라는 고정 이름은 사용하지 않습니다."] = "Firefox requires an actual profile folder; the fixed name Default is not used.",
        ["DPAPI 오류가 발생하면 자동 감지를 다시 실행하거나 cookies.txt·Firefox 쿠키를 사용하세요."] = "If a DPAPI error occurs, run auto-detection again or use cookies.txt or Firefox cookies.",
        ["FFmpeg 사용 가능"] = "FFmpeg available",
        ["FFmpeg를 찾지 못했습니다."] = "FFmpeg was not found.",
        ["설치되지 않음"] = "Not installed",
        ["알 수 없는 오류"] = "Unknown error",
        ["파일을 내려받는 중"] = "Downloading file",
        ["사진"] = "Photo",
        ["영상"] = "Video",
        ["공개 접근으로 분석됨"] = "Analyzed using public access",
        ["공개 접근으로 사진 분석됨"] = "Photos analyzed using public access",
        ["cookies.txt 파일로 분석됨"] = "Analyzed using cookies.txt",
        ["cookies.txt 파일로 사진 분석됨"] = "Photos analyzed using cookies.txt",
        ["일반 설치"] = "Standard installation",
        ["기본 프로필"] = "Default profile",
        ["자동 검색"] = "Auto search",
        ["Instagram cookies.txt 파일 선택"] = "Select Instagram cookies.txt file",
        ["Instagram 사진과 영상을 저장할 폴더를 선택하세요."] = "Select a folder for Instagram photos and videos.",
        ["Netscape cookies.txt (*.txt)|*.txt|모든 파일 (*.*)|*.*"] = "Netscape cookies.txt (*.txt)|*.txt|All files (*.*)|*.*",
        ["cookies.txt 파일을 선택했습니다. 쿠키 검사를 실행해 주세요."] = "The cookies.txt file was selected. Run the cookie test.",
        ["다운로드가 끝난 뒤 엔진 관리 화면을 열어 주세요."] = "Open Engine manager after the download finishes.",
        ["선택한 브라우저 종료"] = "Close selected browser",
        ["Instagram 게시물"] = "Instagram post",
        ["Instagram 사진 게시물"] = "Instagram photo post",
        ["gallery-dl 최신 Windows 실행 파일 주소를 확인하지 못했습니다."] = "Could not find the latest gallery-dl Windows executable URL.",
        ["다운로드한 엔진 파일이 올바른 Windows 실행 파일이 아닙니다."] = "The downloaded engine file is not a valid Windows executable.",
        ["Firefox 쿠키 프로필을 찾지 못했습니다. Firefox에서 Instagram에 로그인한 뒤 프로필 새로고침을 눌러 주세요."] = "No Firefox cookie profile was found. Log in to Instagram in Firefox, then refresh the profiles.",
        ["Firefox, Brave, Opera 또는 Vivaldi의 사용자 프로필을 찾지 못했습니다.\n\n브라우저를 한 번 실행한 뒤 다시 감지하거나 cookies.txt 파일을 선택해 주세요."] = "No user profile was found for Firefox, Brave, Opera, or Vivaldi.\n\nRun the browser once and detect again, or select a cookies.txt file.",
        ["브라우저 쿠키 데이터베이스가 잠겨 있습니다. 설정의 선택 브라우저 종료 버튼을 사용하거나, 잠금 감지 창에서 자동 종료 후 재시도를 선택해 주세요."] = "The browser cookie database is locked. Use Close selected browser in settings, or allow automatic browser closure and retry when prompted.",
        ["선택한 Firefox 프로필에서 cookies.sqlite를 찾지 못했습니다. Firefox 프로필 이름은 Default가 아니라 임의 문자.default-release 형태입니다. InstaSave가 실제 Firefox 프로필을 다시 검색해 자동으로 선택합니다."] = "cookies.sqlite was not found in the selected Firefox profile. Firefox profiles use a random-name.default-release folder rather than Default. InstaSave will search for and select the actual profile automatically.",
        ["Instagram 또는 네트워크의 일시적인 오류로 사진을 받지 못했습니다. 자동 재시도를 진행합니다."] = "Photos could not be downloaded because of a temporary Instagram or network error. Automatic retry will continue.",
        ["Instagram 로그인이 필요하거나 브라우저 쿠키가 만료되었습니다. 로그인된 브라우저와 프로필을 선택해 주세요. 쿠키 DB 잠금이 감지되면 앱의 자동 종료 후 재시도를 이용할 수 있습니다."] = "Instagram login is required or the browser cookies have expired. Select a logged-in browser and profile. If the cookie database is locked, allow the app to close the browser and retry.",
        ["선택한 Chromium 브라우저 쿠키가 App-Bound Encryption으로 보호되어 복호화하지 못했습니다. cookies.txt 파일 또는 Firefox 쿠키를 사용해 주세요."] = "The selected Chromium browser cookies are protected by App-Bound Encryption and could not be decrypted. Use a cookies.txt file or Firefox cookies.",
    };

    private static readonly (string Korean, string English)[] PhraseReplacements =
    [
        ("Instagram 사진·영상 다운로드", "Instagram photo & video downloader"),
        ("확인 불가", "Unavailable"),
        ("설치되지 않음", "Not installed"),
        ("FFmpeg 사용 가능", "FFmpeg available"),
        ("일반 설치", "Standard installation"),
        ("기본 프로필", "Default profile"),
        ("사용 안 함", "No cookies"),
        ("브라우저 자동 감지", "Browser auto-detection"),
        ("사진 다운로드 일부 실패", "Some photo downloads failed"),
        ("영상 형식 없음", "No video format"),
        ("사진 엔진으로 계속합니다", "Continuing with the photo engine"),
        ("Firefox 쿠키 프로필", "Firefox cookie profile"),
        ("Firefox, Brave, Opera 또는 Vivaldi의 사용자 프로필", "a user profile for Firefox, Brave, Opera, or Vivaldi"),
        ("게시물에서", "In the post,"),
        ("제목:", "Title:"),
        ("사용 가능 ·", "Available ·"),
        ("클립보드를 읽지 못했습니다:", "Could not read the clipboard:"),
        ("브라우저 쿠키로 사진 분석됨", "Photos analyzed using browser cookies"),
        ("브라우저 쿠키로 분석됨", "Analyzed using browser cookies"),
        ("영상 ·", "Video ·"),
        ("완료 ·", "Completed ·"),
        ("선택한 Chromium 브라우저의 쿠키가 Windows App-Bound Encryption으로 보호되어 yt-dlp가 DPAPI로 복호화하지 못했습니다.", "The selected Chromium browser cookies are protected by Windows App-Bound Encryption and yt-dlp could not decrypt them with DPAPI."),
        ("브라우저를 종료해도 해결되지 않을 수 있습니다.", "Closing the browser may not resolve this issue."),
        ("설정에서 'cookies.txt 파일 직접 선택'을 사용하거나 Firefox 쿠키를 선택해 주세요.", "Select a cookies.txt file in settings or use Firefox cookies."),
        ("브라우저 쿠키 자동 감지가 이미 진행 중입니다.", "Browser cookie auto-detection is already running."),
        ("설치된 브라우저 프로필을 찾았지만 Instagram 게시물에 접근 가능한 쿠키를 확인하지 못했습니다.", "Browser profiles were found, but no cookies could access the Instagram post."),
        ("브라우저에서 Instagram에 로그인한 뒤 다시 시도하거나 Firefox 또는 cookies.txt 파일을 사용해 주세요.", "Log in to Instagram in the browser and try again, or use Firefox or a cookies.txt file."),
        ("사용 가능한 브라우저 쿠키를 자동으로 찾지 못했습니다.", "No usable browser cookies were found automatically."),
        ("브라우저에서 Instagram에 로그인한 뒤 다시 감지하거나, Firefox·수동 프로필·cookies.txt 방식을 사용해 주세요.", "Log in to Instagram in a browser and detect again, or use Firefox, a manually selected profile, or cookies.txt."),
        ("Firefox, Brave, Opera 또는 Vivaldi의 사용자 프로필을 찾지 못했습니다.", "No user profile was found for Firefox, Brave, Opera, or Vivaldi."),
        ("브라우저를 한 번 실행한 뒤 다시 감지하거나 cookies.txt 파일을 선택해 주세요.", "Run the browser once and detect again, or select a cookies.txt file."),
        ("브라우저 쿠키를 자동으로 선택했습니다.", "Browser cookies were selected automatically."),
        ("설치된 프로필 정보를 기준으로 선택했습니다. '선택한 쿠키 검사'를 눌러 실제 접근을 확인할 수 있습니다.", "The profile was selected from installed profile data. Select Test selected cookies to verify actual access."),
        ("Instagram 접근 확인 완료", "Instagram access verified"),
        ("게시자:", "Author:"),
        ("프로필:", "Profile:"),
        ("브라우저:", "Browser:"),
        ("검사 결과:", "Test results:"),
        ("DPAPI 복호화 실패", "DPAPI decryption failed"),
        ("쿠키 DB 사용 중", "cookie database in use"),
        ("쿠키 자동 검사 중:", "Automatically testing cookies:"),
        ("브라우저 자동 감지 완료:", "Browser auto-detection complete:"),
        ("프로필을 새로 검색할 브라우저를 먼저 선택해 주세요.", "Select a browser before refreshing its profiles."),
        ("프로필을 찾지 못했습니다.", "No profile was found."),
        ("프로필을 찾았습니다.", "profile(s) found."),
        ("Firefox 쿠키 프로필을 찾지 못했습니다.", "No Firefox cookie profile was found."),
        ("Firefox에서 Instagram에 로그인한 뒤 프로필 새로고침을 눌러 주세요.", "Log in to Instagram in Firefox, then refresh the profiles."),
        ("일반 설치와 Microsoft Store 설치 경로를 모두 검사합니다.", "Both standard and Microsoft Store installation paths are checked."),
        ("감지된 프로필", "Detected profiles:"),
        ("기본 프로필 이름을 표시합니다.", "Default profile names are also shown."),
        ("기본 프로필 이름을 선택하거나 실제 프로필 이름을 직접 입력할 수 있습니다.", "Select a default profile name or enter the actual profile name."),
        ("미리 볼 Instagram 게시물 URL을 입력해 주세요.", "Enter an Instagram post URL to preview."),
        ("쿠키 접근을 검사할 Instagram 게시물 URL을 먼저 입력해 주세요.", "Enter an Instagram post URL before testing cookie access."),
        ("검사할 쿠키 사용 방식을 먼저 선택해 주세요.", "Select a cookie source before testing."),
        ("쿠키 읽기와 게시물 접근에 성공했습니다.", "Cookies were read and the post was accessed successfully."),
        ("쿠키 소스:", "Cookie source:"),
        ("종료할 브라우저를 먼저 선택해 주세요.", "Select a browser to close first."),
        ("현재 실행 중인", "No running"),
        ("프로세스를 찾지 못했습니다.", "process was found."),
        ("쿠키 데이터베이스 잠금을 해제하기 위해", "To release the cookie database lock,"),
        ("의 모든 창과 백그라운드 프로세스를 종료합니다.", "all windows and background processes will be closed."),
        ("열려 있던 탭은 보통 다음 실행 시 복원되지만, 작성 중인 내용은 먼저 저장하는 것이 안전합니다. 계속할까요?", "Open tabs are usually restored the next time the browser starts, but save any unsaved work first. Continue?"),
        ("프로세스를 종료하지 못했습니다.", "process(es) could not be closed."),
        ("InstaSave를 관리자 권한으로 실행하거나 작업 관리자에서 직접 종료해 주세요.", "Run InstaSave as administrator or close the processes manually in Task Manager."),
        ("프로세스를 종료했습니다. 이제 쿠키 검사를 다시 실행해 주세요.", "process(es) were closed. Run the cookie test again."),
        ("실행 중인", "There are no running"),
        ("프로세스가 없습니다.", "processes."),
        ("종료 완료", "closed"),
        ("올바른 Instagram URL을 찾지 못했습니다.", "No valid Instagram URL was found."),
        ("사진, 캐러셀, 릴스 또는 동영상 게시물 주소를 확인해 주세요.", "Check the photo, carousel, reel, or video post URL."),
        ("이미 다운로드한 기록이 있는 게시물입니다.", "This post has already been downloaded."),
        ("기존 파일을 덮어쓰고 다시 다운로드하시겠습니까?", "Overwrite the existing file and download it again?"),
        ("개 URL을 목록에 추가했습니다.", " URL(s) were added to the queue."),
        ("클립보드에서 Instagram URL", "Automatically added"),
        ("개를 자동 추가했습니다.", " Instagram URL(s) from the clipboard."),
        ("현재 다운로드가 끝난 뒤 전체 시작을 눌러 주세요.", "Wait for the current download to finish, then select Start all."),
        ("재시도", "Retry"),
        ("게시물 분석 중", "Analyzing post"),
        ("다운로드 준비 중", "Preparing download"),
        ("영상 다운로드 중:", "Downloading video:"),
        ("사진 다운로드 중:", "Downloading photos:"),
        ("사진을 내려받는 중", "Downloading photos"),
        ("다운로드할 사진이나 영상을 찾지 못했습니다.", "No downloadable photos or videos were found."),
        ("로그인 쿠키와 게시물 접근 권한을 확인해 주세요.", "Check the login cookies and post access permissions."),
        ("이미 다운로드됨:", "Already downloaded:"),
        ("완료:", "Completed:"),
        ("오류 발생", "Error"),
        ("초 후 자동 재시도", "s until automatic retry"),
        ("초 후 재시도:", "s until retry:"),
        ("실패:", "Failed:"),
        ("완료 항목", "Completed items:"),
        ("개를 지웠습니다.", " item(s) cleared."),
        ("사진 일부 실패", "Some photos failed"),
        ("다운로드 완료", "Download complete"),
        ("남은 시간", "Time left"),
        ("선택한 파일이 Netscape 형식 cookies.txt로 보이지 않습니다.", "The selected file does not appear to be a Netscape-format cookies.txt file."),
        ("브라우저 확장 프로그램에서 Instagram 쿠키를 Netscape 형식으로 내보낸 파일을 선택해 주세요.", "Select a file exported in Netscape format by a browser extension."),
        ("cookies.txt 파일을 읽지 못했습니다.", "Could not read the cookies.txt file."),
        ("사용할 cookies.txt 파일을 먼저 선택해 주세요.", "Select a cookies.txt file first."),
        ("Firefox의 실제 쿠키 프로필을 찾지 못했습니다.", "An actual Firefox cookie profile could not be found."),
        ("Firefox에서 Instagram에 로그인한 뒤 '프로필 새로고침' 또는 '브라우저 자동 감지'를 눌러 주세요.", "Log in to Instagram in Firefox, then select Refresh profiles or Auto-detect browser."),
        ("Firefox 프로필은 Default가 아니라 임의문자.default-release 형태입니다.", "Firefox profiles use a random-name.default-release folder rather than Default."),
        ("브라우저 쿠키를 사용하려면 프로필을 직접 선택하거나 입력해 주세요.", "Select or enter a profile to use browser cookies."),
        ("예상하지 못한 오류가 발생했습니다.", "An unexpected error occurred."),
        ("미디어 엔진을 설치하지 못했습니다.", "The media engines could not be installed."),
        ("인터넷 연결 또는 보안 프로그램 차단 여부를 확인한 뒤 엔진 관리에서 재설치해 주세요.", "Check the internet connection or security software, then reinstall the engines from Engine manager."),
        ("다운로드 엔진에서 알 수 없는 오류가 발생했습니다.", "An unknown download-engine error occurred."),
        ("사진 다운로드 엔진에서 알 수 없는 오류가 발생했습니다.", "An unknown photo-download engine error occurred."),
        ("Instagram 로그인 또는 접근 권한이 필요합니다.", "Instagram login or access permission is required."),
        ("올바른 브라우저와 프로필을 선택하고 Instagram에 로그인한 뒤 다시 시도해 주세요.", "Select the correct browser and profile, log in to Instagram, and try again."),
        ("게시물을 찾을 수 없거나 비공개 콘텐츠입니다.", "The post could not be found or is private."),
        ("URL과 계정 접근 권한을 확인해 주세요.", "Check the URL and account access permissions."),
        ("Instagram 요청이 일시적으로 제한되었거나 네트워크 연결이 불안정합니다.", "Instagram requests are temporarily limited or the network connection is unstable."),
        ("Instagram 요청 제한에 도달했습니다.", "The Instagram request limit was reached."),
        ("잠시 후 다시 시도해 주세요.", "Try again later."),
        ("게시물이 비공개이거나 삭제되었거나 현재 계정에 접근 권한이 없습니다.", "The post is private, deleted, or inaccessible to the current account."),
        ("선택한 Chromium 브라우저 쿠키가", "The selected Chromium browser cookies are"),
        ("Windows App-Bound Encryption으로 보호되어 복호화하지 못했습니다.", "protected by Windows App-Bound Encryption and could not be decrypted."),
        ("cookies.txt 파일 또는 Firefox 쿠키를 사용해 주세요.", "Use a cookies.txt file or Firefox cookies."),
        ("선택한 Firefox 프로필에서 cookies.sqlite를 찾지 못했습니다.", "cookies.sqlite was not found in the selected Firefox profile."),
        ("실제 Firefox 프로필을 다시 검색합니다.", "Searching again for an actual Firefox profile."),
        ("선택한 브라우저가 쿠키 데이터베이스를 사용 중입니다.", "The selected browser is using its cookie database."),
        ("InstaSave에서 브라우저를 안전하게 종료한 뒤 자동으로 다시 시도할 수 있습니다.", "InstaSave can safely close the browser and retry automatically."),
        ("설치되지 않았거나 경로를 찾지 못했습니다.", "Not installed or the path could not be found."),
        ("영상·음성 병합이 필요한 형식에서는 FFmpeg가 필요할 수 있습니다.", "FFmpeg may be required for formats that need video/audio merging."),
        ("선택한 폴더에서 ffmpeg.exe를 찾지 못했습니다.", "ffmpeg.exe was not found in the selected folder."),
        ("영상 엔진 yt-dlp를 업데이트하는 중입니다.", "Updating the yt-dlp video engine."),
        ("영상 엔진 업데이트가 완료되었습니다.", "Video engine update completed."),
        ("영상 엔진 yt-dlp를 새로 내려받는 중입니다.", "Downloading a fresh copy of the yt-dlp video engine."),
        ("영상 엔진 재설치가 완료되었습니다.", "Video engine reinstallation completed."),
        ("이전 영상 엔진을 복원하는 중입니다.", "Restoring the previous video engine."),
        ("이전 영상 엔진 복원이 완료되었습니다.", "Previous video engine restored."),
        ("영상 엔진 캐시를 초기화하는 중입니다.", "Clearing the video engine cache."),
        ("영상 엔진 캐시 초기화가 완료되었습니다.", "Video engine cache cleared."),
        ("사진 엔진 gallery-dl을 업데이트하는 중입니다.", "Updating the gallery-dl photo engine."),
        ("사진 엔진 업데이트가 완료되었습니다.", "Photo engine update completed."),
        ("사진 엔진 gallery-dl을 새로 내려받는 중입니다.", "Downloading a fresh copy of the gallery-dl photo engine."),
        ("사진 엔진 재설치가 완료되었습니다.", "Photo engine reinstallation completed."),
        ("이전 사진 엔진을 복원하는 중입니다.", "Restoring the previous photo engine."),
        ("이전 사진 엔진 복원이 완료되었습니다.", "Previous photo engine restored."),
        ("사진 엔진 캐시를 초기화하는 중입니다.", "Clearing the photo engine cache."),
        ("사진 엔진 캐시 초기화가 완료되었습니다.", "Photo engine cache cleared."),
        ("ffmpeg.exe가 들어 있는 폴더를 선택하세요.", "Select the folder containing ffmpeg.exe."),
        ("복원할 이전", "No previous"),
        ("엔진이 없습니다.", "engine is available to restore.")
    ];

    public static string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || IsKorean)
            return text ?? string.Empty;

        if (ExactEnglish.TryGetValue(text, out var exact))
            return TranslateFromEnglish(exact);

        var protectedValues = new List<string>();
        var result = ProtectUserContent(text, protectedValues);

        result = Regex.Replace(result, @"Firefox 실제 프로필 (?<count>\d+)개를 찾았습니다\.", "Found ${count} actual Firefox profile(s).");
        result = Regex.Replace(result, @"감지된 프로필 (?<count>\d+)개와 기본 프로필 이름을 표시합니다\.", "Showing ${count} detected profile(s) and default profile names.");
        result = Regex.Replace(result, @"(?<name>.+?) 프로세스 (?<count>\d+)개를 종료했습니다\. 이제 쿠키 검사를 다시 실행해 주세요\.", "Closed ${count} ${name} process(es). Run the cookie test again.");
        result = Regex.Replace(result, @"(?<name>.+?) 프로세스 (?<count>\d+)개를 종료하지 못했습니다\.", "Could not close ${count} ${name} process(es).");
        result = Regex.Replace(result, @"(?<name>.+?)을 종료하는 중입니다\.", "Closing ${name}.");

        result = Regex.Replace(result, @"(?<count>\d+)개 URL을 목록에 추가했습니다\.", "Added ${count} URL(s) to the queue.");
        result = Regex.Replace(result, @"클립보드에서 Instagram URL (?<count>\d+)개를 자동 추가했습니다\.", "Automatically added ${count} Instagram URL(s) from the clipboard.");
        result = Regex.Replace(result, @"완료 항목 (?<count>\d+)개를 지웠습니다\.", "Cleared ${count} completed item(s).");
        result = Regex.Replace(result, @"(?<name>.+?) 프로필 (?<count>\d+)개를 찾았습니다\.", "Found ${count} ${name} profile(s).");
        result = Regex.Replace(result, @"(?<name>.+?) 프로필을 찾지 못했습니다\.", "No ${name} profile was found.");
        result = Regex.Replace(result, @"사진 (?<count>\d+)장", match =>
            match.Groups["count"].Value == "1" ? "1 photo" : $"{match.Groups["count"].Value} photos");
        result = Regex.Replace(result, @"영상 (?<count>\d+)개", match =>
            match.Groups["count"].Value == "1" ? "1 video" : $"{match.Groups["count"].Value} videos");
        result = Regex.Replace(result, @"(?<count>\d+)개 미디어", match =>
            match.Groups["count"].Value == "1" ? "1 media item" : $"{match.Groups["count"].Value} media items");
        result = Regex.Replace(result, @"(?<kind>사진|영상) 1개", match =>
            match.Groups["kind"].Value == "사진" ? "1 photo" : "1 video");
        result = Regex.Replace(result, @"(?<count>\d+)초 후 재시도:\s*", "Retrying in ${count}s: ");
        result = Regex.Replace(result, @"오류 발생 · (?<count>\d+)초 후 자동 재시도 \((?<a>\d+)/(?<b>\d+)\)", "Error · Auto retry in ${count}s (${a}/${b})");
        result = Regex.Replace(result, @"재시도 (?<a>\d+)/(?<b>\d+) · 게시물 분석 중", "Retry ${a}/${b} · Analyzing post");
        result = Regex.Replace(result, @"재시도 (?<a>\d+) · 다운로드 준비 중", "Retry ${a} · Preparing download");
        result = Regex.Replace(result, @"영상 엔진 설치 중 (?<value>[\d.]+)%", "Installing video engine ${value}%");
        result = Regex.Replace(result, @"사진 엔진 설치 중 (?<value>[\d.]+)%", "Installing photo engine ${value}%");
        result = Regex.Replace(result, @"사진을 내려받는 중 · (?<count>\d+)장", "Downloading photos · ${count} downloaded");
        result = Regex.Replace(result, @"(?<count>\d+)개$", "${count} items");

        foreach (var (korean, english) in PhraseReplacements.OrderByDescending(pair => pair.Korean.Length))
            result = result.Replace(korean, english, StringComparison.Ordinal);

        result = Regex.Replace(result, @"\s+([.,:;!?])", "$1");
        result = Regex.Replace(result, @" {2,}", " ");

        if (ContainsKorean(result) && string.Equals(result, text, StringComparison.Ordinal))
            result = BuildSafeFallback(text, result);

        result = RestoreProtectedValues(result, protectedValues);
        return TranslateFromEnglish(result);
    }

    private static string TranslateFromEnglish(string english)
    {
        if (string.Equals(LanguageCode, "en", StringComparison.OrdinalIgnoreCase))
            return english;

        return AdditionalLanguageTranslations.Translate(LanguageCode, english);
    }

    private static string ProtectUserContent(string text, List<string> protectedValues)
    {
        string Protect(string value)
        {
            var token = $"__INSTASAVE_RAW_{protectedValues.Count}__";
            protectedValues.Add(value);
            return token;
        }

        var result = Regex.Replace(
            text,
            @"(?m)(게시자:|제목:)\s*(?<value>[^\r\n]*)",
            match => match.Groups[1].Value + " " + Protect(match.Groups["value"].Value));

        foreach (var prefix in new[]
                 {
                     "완료: ",
                     "실패: ",
                     "이미 다운로드됨: ",
                     "영상 다운로드 중: ",
                     "사진 다운로드 중: "
                 })
        {
            if (result.StartsWith(prefix, StringComparison.Ordinal))
                result = prefix + Protect(result[prefix.Length..]);
        }

        return result;
    }

    private static string RestoreProtectedValues(string text, IReadOnlyList<string> protectedValues)
    {
        var result = text;
        for (var index = 0; index < protectedValues.Count; index++)
            result = result.Replace($"__INSTASAVE_RAW_{index}__", protectedValues[index], StringComparison.Ordinal);
        return result;
    }

    public static void LocalizeWindow(Window window)
    {
        if (IsKorean)
            return;

        window.Title = Translate(window.Title);
        var visited = new HashSet<DependencyObject>();
        LocalizeNode(window, visited);
    }

    private static void LocalizeNode(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
            return;

        var skipElementText = node is FrameworkElement namedElement &&
                              !string.IsNullOrWhiteSpace(namedElement.Name) &&
                              NonLocalizedElementNames.Contains(namedElement.Name);

        if (node is FrameworkElement element && element.ToolTip is string toolTip && ShouldTranslateText(toolTip))
            element.ToolTip = Translate(toolTip);

        if (!skipElementText && node is TextBlock textBlock &&
            !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty) &&
            ShouldTranslateText(textBlock.Text))
        {
            textBlock.Text = Translate(textBlock.Text);
        }

        if (!skipElementText && node is ContentControl contentControl &&
            !BindingOperations.IsDataBound(contentControl, ContentControl.ContentProperty) &&
            contentControl.Content is string content && ShouldTranslateText(content))
        {
            contentControl.Content = Translate(content);
        }

        if (!skipElementText && node is HeaderedContentControl headeredContentControl &&
            !BindingOperations.IsDataBound(headeredContentControl, HeaderedContentControl.HeaderProperty) &&
            headeredContentControl.Header is string header && ShouldTranslateText(header))
        {
            headeredContentControl.Header = Translate(header);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            LocalizeNode(child, visited);

        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < childCount; index++)
                LocalizeNode(VisualTreeHelper.GetChild(node, index), visited);
        }
    }

    private static bool ShouldTranslateText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !ContainsKorean(value))
            return false;

        if (Path.IsPathRooted(value) || Uri.TryCreate(value, UriKind.Absolute, out _))
            return false;

        return true;
    }

    private static bool ContainsKorean(string value) => KoreanRegex().IsMatch(value);

    private static string BuildSafeFallback(string original, string partiallyTranslated)
    {
        if (original.Contains("쿠키", StringComparison.Ordinal))
            return "Cookie access could not be completed. Check the selected browser profile or cookies.txt file and try again.";
        if (original.Contains("브라우저", StringComparison.Ordinal))
            return "The browser operation could not be completed. Check the browser profile and try again.";
        if (original.Contains("사진", StringComparison.Ordinal) || original.Contains("영상", StringComparison.Ordinal) || original.Contains("다운로드", StringComparison.Ordinal))
            return "The download operation could not be completed. Check the item details and try again.";
        if (original.Contains("엔진", StringComparison.Ordinal) || original.Contains("FFmpeg", StringComparison.Ordinal))
            return "The media engine operation could not be completed. Open Engine manager for details.";
        if (original.Contains("실패", StringComparison.Ordinal) || original.Contains("오류", StringComparison.Ordinal))
            return "The operation failed. Check the details and try again.";

        return Regex.Replace(partiallyTranslated, @"[가-힣]+", string.Empty).Trim();
    }

    [GeneratedRegex("[가-힣]")]
    private static partial Regex KoreanRegex();
}

public static class LocalizedMessageBox
{
    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon);

    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult) =>
        MessageBox.Show(LocalizationService.Translate(messageBoxText), LocalizationService.Translate(caption), button, icon, defaultResult);
}
