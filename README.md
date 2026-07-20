# InstaSave 1.0.0

Windows에서 Instagram 사진, 여러 장짜리 캐러셀, 릴스와 동영상 게시물을 다운로드하는 C# WPF 프로그램입니다. 라이트 모드 전용이며 .NET 8 기반으로 제작되었습니다.

## 언어 자동 감지

InstaSave는 Windows의 현재 표시 언어를 시작 시 자동으로 확인합니다.

- Windows 표시 언어가 한국어이면 한국어 UI를 사용합니다.
- 한국어 이외의 모든 언어 환경에서는 영어 UI를 사용합니다.
- 버튼, 설정, 상태, 오류 안내와 엔진 관리 화면에 동일하게 적용됩니다.
- Instagram 게시물 제목과 설명 같은 원본 콘텐츠는 번역하지 않습니다.
- 언어를 별도로 선택하거나 설정할 필요가 없습니다.
- 설치 프로그램의 바로가기 및 실행 안내도 한국어 또는 영어로 자동 표시됩니다.

## 주요 기능

- Instagram 사진 게시물, 사진 캐러셀, 릴스 및 동영상 게시물 다운로드
- 사진과 영상이 섞인 게시물의 미디어를 함께 저장
- `사진도 다운로드` 옵션으로 사진 저장 기능 켜기·끄기
- 여러 URL을 순차 처리하는 다운로드 대기열
- 다운로드 전 제목, 게시자, 썸네일, 게시물 ID, 날짜, 길이, 미디어 개수 미리보기
- 클립보드에서 Instagram URL 자동 감지
- 앱 기록, yt-dlp 아카이브, gallery-dl 아카이브를 이용한 중복 다운로드 방지
- 중복 게시물 강제 재다운로드 선택
- Firefox, Brave, Opera, Vivaldi 쿠키 연동
- 설치된 브라우저와 프로필 자동 감지 및 실제 Instagram 접근 검사
- Firefox 일반 설치판과 Microsoft Store 설치판의 실제 프로필 경로 자동 검색
- 잘못 저장된 Firefox `Default` 프로필을 실제 `.default-release` 프로필로 자동 교체
- DPAPI 복호화 실패 또는 잠금 후보를 건너뛰고 다음 브라우저 자동 검사
- 브라우저 프로필 직접 지정 및 게시물 접근 검사
- 쿠키 DB 잠금 감지, 선택 브라우저 종료 및 자동 재시도
- 실패 시 자동 재시도, 대기 시간 설정, 영상 `.part` 파일 이어받기
- 실패 및 취소 항목 일괄 재시도
- 최고 화질, 최대 1080p, 720p, 480p 선택
- 게시물에 포함된 모든 미디어 또는 첫 번째 미디어만 다운로드
- 썸네일 별도 저장
- 파일 이름 프리셋 및 사용자 지정 형식
- yt-dlp와 gallery-dl 업데이트, 재설치, 이전 버전 복원, 캐시 초기화
- FFmpeg 자동 검색 및 사용자 경로 지정
- 진행률, 속도, 남은 시간 표시
- 다운로드 기록과 설정 자동 저장
- Windows x64 self-contained 단일 EXE 빌드

## 실행 방법

Windows 10 또는 Windows 11에 .NET 8 SDK를 설치한 뒤 다음 파일을 실행합니다.


## 기본 사용 방법

1. Instagram 사진, 캐러셀, 릴스 또는 동영상 게시물 URL을 입력합니다.
2. 필요하면 `미리보기`를 눌러 게시물 정보를 확인합니다.
3. 사진을 저장하려면 `사진도 다운로드`를 켭니다.
4. 저장 폴더, 화질, 파일 이름 형식을 선택합니다.
5. 공개 게시물은 `쿠키 사용 방식: 사용 안 함`으로 먼저 시도합니다.
6. 로그인이 필요한 게시물은 `브라우저 자동 감지`를 누르거나 브라우저와 프로필을 직접 선택합니다.
7. 자동 감지는 URL이 입력되어 있으면 각 후보의 Instagram 접근을 실제로 검사하고 성공한 브라우저를 선택합니다.
8. `목록에 추가` 후 `전체 시작`을 누릅니다.

영상은 yt-dlp, 사진은 gallery-dl로 처리합니다. 두 엔진은 최초 필요 시 공식 배포처에서 자동 설치되며 소스 ZIP에는 실행 파일이 포함되지 않습니다.

## 사용자 지정 파일 이름

`파일 이름 형식`에서 `사용자 지정`을 선택하면 다음 값을 사용할 수 있습니다.

```text
{uploader}   게시자
{date}       업로드 날짜
{id}         게시물 또는 미디어 ID
{title}      제목 또는 설명
{resolution} 해상도
{index}      여러 미디어의 순번
```

예시:

```text
{date}_{uploader}_{title}_{id}
```

Windows에서 사용할 수 없는 문자는 자동으로 밑줄로 변경됩니다. ID 또는 순번이 빠진 형식에는 파일 충돌 방지를 위해 ID가 자동으로 추가됩니다.

## 클립보드 자동 감지

`클립보드 Instagram URL 자동 감지`를 켜면 새로 복사한 Instagram 주소가 대기열에 자동으로 추가됩니다.

- Instagram 주소만 감지합니다.
- 같은 주소가 이미 목록에 있으면 다시 추가하지 않습니다.
- 이전 다운로드 기록에 있는 주소는 자동 추가하지 않습니다.
- 설정은 기본적으로 꺼져 있습니다.

## 중복 다운로드 방지

중복 방지 기능은 다음 파일을 함께 사용합니다.

```text
%LOCALAPPDATA%\InstaSave\download-records.json
%LOCALAPPDATA%\InstaSave\yt-dlp-archive.txt
%LOCALAPPDATA%\InstaSave\gallery-dl-archive.sqlite3
```

이미 다운로드한 URL을 직접 추가하면 재다운로드 여부를 묻습니다. 재다운로드를 선택한 경우에만 기존 미디어를 다시 저장합니다.

## 자동 재시도 및 이어받기

네트워크 오류나 일시적인 요청 제한이 발생하면 설정한 횟수만큼 자동으로 재시도합니다. 영상 다운로드 중 취소하거나 앱이 종료되어도 `.part` 임시 파일은 유지되며, 다시 시작하면 가능한 경우 이어서 다운로드합니다.

로그인 필요, 비공개 콘텐츠, 잘못된 URL처럼 재시도로 해결되지 않는 오류는 자동 재시도하지 않습니다.

## 쿠키 사용 주의사항

이 프로그램은 Instagram 아이디와 비밀번호를 직접 입력받거나 저장하지 않습니다. yt-dlp와 gallery-dl이 선택한 브라우저의 기존 로그인 세션을 직접 읽습니다.

- `브라우저 자동 감지`는 Firefox, Brave, Vivaldi, Opera의 프로필을 순서대로 찾습니다.
- Instagram URL이 입력되어 있으면 실제 쿠키 접근까지 검사하고, DPAPI 복호화 실패 또는 DB 잠금이 발생한 후보는 건너뜁니다.
- 자동 감지에 성공하면 브라우저와 프로필 선택란이 해당 항목으로 자동 변경되어 이후 영상·사진 다운로드에 함께 적용됩니다.
- 선택한 브라우저의 쿠키 DB 잠금이 감지되면 해당 브라우저를 종료하고 자동 재시도할지 확인합니다.
- 설정 화면의 `선택 브라우저 종료` 버튼으로 창과 백그라운드 프로세스를 직접 정리할 수도 있습니다.
- 브라우저 종료 전 작성 중인 내용은 저장하세요. 열려 있던 탭은 보통 브라우저 재실행 시 복원됩니다.
- 브라우저 프로필은 `Default`, `Profile 1`, `default-release`처럼 직접 선택하거나 입력합니다.
- 비공개 게시물은 선택한 계정이 실제 접근 권한을 가지고 있어야 합니다.
- 앱은 쿠키 파일을 별도로 내보내거나 저장하지 않습니다.

## 엔진 관리

`엔진 관리` 화면에서 다음 작업을 할 수 있습니다.

- 영상 엔진 yt-dlp 버전 확인, 업데이트, 재설치, 이전 버전 복원, 캐시 초기화
- 사진 엔진 gallery-dl 버전 확인, 업데이트, 재설치, 이전 버전 복원, 캐시 초기화
- 도구 폴더와 로그 열기
- FFmpeg 설치 여부 확인
- FFmpeg 폴더 직접 지정

업데이트 직전 엔진은 다음 위치에 백업됩니다.

```text
%LOCALAPPDATA%\InstaSave\tools\yt-dlp.previous.exe
%LOCALAPPDATA%\InstaSave\tools\gallery-dl.previous.exe
```

## 저장 위치

```text
%LOCALAPPDATA%\InstaSave
```

주요 파일:

- `settings.json`: 사용자 설정
- `history.json`: 최근 대기열 및 다운로드 기록
- `download-records.json`: 중복 검사 기록
- `yt-dlp-archive.txt`: 영상 다운로드 아카이브
- `gallery-dl-archive.sqlite3`: 사진 다운로드 아카이브
- `InstaSave.log`: 문제 확인용 로그
- `tools\yt-dlp.exe`: 영상 다운로드 엔진
- `tools\yt-dlp.previous.exe`: 이전 영상 엔진 백업
- `tools\gallery-dl.exe`: 사진 다운로드 엔진
- `tools\gallery-dl.previous.exe`: 이전 사진 엔진 백업

## 합법적 사용

본인이 제작했거나 저장 권한을 받은 콘텐츠만 다운로드하세요. 저작권, 개인정보, Instagram 이용약관 및 현지 법률을 준수해야 합니다. 이 프로그램은 비공개 계정의 접근 제어를 우회하지 않습니다.

브라우저 쿠키 사용 방법
- 브라우저 자동 감지 또는 수동 브라우저 선택을 사용할 수 있습니다.
- Firefox는 감지된 실제 `임의문자.default-release` 프로필을 선택합니다.
- Brave, Opera, Vivaldi는 프로필 이름 또는 실제 프로필 경로를 선택할 수 있습니다.
- 선택한 쿠키는 영상과 사진 다운로드에 동일하게 사용됩니다.


## Chromium 계열 DPAPI 오류

Brave 또는 Vivaldi에서 Windows App-Bound Encryption으로 쿠키 직접 읽기가 실패하면 `cookies.txt 파일 직접 선택`을 사용하거나 Firefox 프로필 쿠키를 선택하세요.

쿠키 파일에는 로그인 세션 정보가 포함되므로 다른 사람에게 공유하지 말고, 사용하지 않을 때는 안전하게 보관하거나 삭제하세요.


## 언어 설정

상단 언어 메뉴에서 자동, 한국어, English를 선택할 수 있습니다. 자동은 Windows 표시 언어가 한국어일 때만 한국어를 사용하고, 나머지 환경에서는 영어를 사용합니다.


# InstaSave 1.0.0

A light-mode C# WPF application for downloading Instagram photos, carousels, Reels, and video posts on Windows.

## Automatic language detection

InstaSave detects the current Windows UI language at startup.

- Korean Windows environments use the Korean interface.
- Every other Windows language uses the English interface.
- Buttons, settings, status messages, errors, and the engine manager follow the detected language.
- Instagram titles, uploader names, and descriptions remain in their original language.
- No manual language setting is required.
- Custom installer text also follows Korean or English automatically.

## Features

- Download photo-only posts, multi-photo carousels, Reels, and video posts
- Download photos and videos from mixed-media posts
- Optional `Download photos` setting
- Preview title, uploader, thumbnail, media ID, upload date, duration, and media count
- Automatic Instagram URL detection from the clipboard
- Persistent duplicate prevention using local records, the yt-dlp archive, and the gallery-dl archive
- Automatic detection of Firefox, Brave, Vivaldi, and Opera profiles
- Live Instagram access checks with fallback after DPAPI decryption or database-lock failures
- Browser cookie access with optional manual profile selection
- Cookie and post-access diagnostics
- Automatic retry with configurable retry count and delay
- Resume interrupted video `.part` downloads
- Download every media item or only the first item
- Multiple filename presets and custom filename tokens
- Manage yt-dlp and gallery-dl updates, reinstallations, backups, restores, and caches
- FFmpeg detection and custom directory selection
- Queue progress, speed, ETA, history, and settings persistence
- Windows x64 self-contained single-file publishing

## Build

Install the .NET 8 SDK on Windows and run:


Video media is handled by yt-dlp and photo media by gallery-dl. The official standalone executables are downloaded at runtime when required and are not included in this source archive.

## Custom filename tokens

```text
{uploader} {date} {id} {title} {resolution} {index}
```

## Data directory

```text
%LOCALAPPDATA%\InstaSave
```

The app does not request or store Instagram usernames and passwords. The selected browser session is read directly by the media engines. Only download content you own or are authorized to save.

## Browser cookie lock recovery

When a selected browser keeps its cookie database locked, InstaSave can ask for permission to close that browser, stop remaining background processes, and retry the failed operation once. A manual **Close selected browser** button is also available. Save any text being edited in the browser before allowing it to close.


## Automatic browser detection

Use **Detect browser automatically** to scan Firefox, Brave, Vivaldi, and Opera profiles. When an Instagram URL is available, InstaSave tests candidates in sequence and selects the first profile that can access the post. Candidates that fail with Windows DPAPI/App-Bound Encryption or a locked cookie database are skipped. Manual browser selection and Netscape `cookies.txt` remain available.


## Language settings

Use the language menu in the header to select Automatic, Korean, or English. Automatic uses Korean only when the Windows display language is Korean; all other environments use English.


