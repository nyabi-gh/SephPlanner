# SephPlanner

[![누적 다운로드](https://img.shields.io/github/downloads/nyabi-gh/SephPlanner/total?style=for-the-badge&logo=github&logoColor=white&label=%EB%8B%A4%EC%9A%B4%EB%A1%9C%EB%93%9C&labelColor=1e1b2e&color=77d8b0)](https://github.com/nyabi-gh/SephPlanner/releases) [![최신 릴리스](https://img.shields.io/github/v/release/nyabi-gh/SephPlanner?style=for-the-badge&logo=github&logoColor=white&label=%EC%B5%9C%EC%8B%A0&labelColor=1e1b2e&color=b9a7f5)](https://github.com/nyabi-gh/SephPlanner/releases/latest) [![지원 플랫폼](https://img.shields.io/badge/%ED%94%8C%EB%9E%AB%ED%8F%BC-Windows%20%7C%20macOS-8bbcf2?style=for-the-badge&labelColor=1e1b2e)](#설치)

**세피 플래너**는 세피리아(Sephiria)의 석판·아티팩트 배치를 계산해 게임 화면에 보여 주는 무료 팬 제작 모드입니다.

- 지금 가방보다 나은 배치를 찾아 보여 주고, `F8` 한 번으로 적용합니다.
- 상자·상점·세피라이트에서 무엇을 고를지 추천합니다.
- 석판 합성과 인챈트 대상을 추천합니다.

**[⬇ 다운로드](https://github.com/nyabi-gh/SephPlanner/releases/latest)** · [변경 사항](https://github.com/nyabi-gh/SephPlanner/releases) · [자세한 사용법](docs/USAGE.md) · [오류 제보](https://github.com/nyabi-gh/SephPlanner/issues)

> TEAM HORAY와 관계없는 비공식 도구입니다. 모드 사용은 본인 책임이며, 중요한 세이브는 미리 백업해 두세요.

## 설치

Steam판 세피리아가 필요합니다. 설치 ZIP에 BepInEx가 들어 있어 따로 받을 것은 없습니다.

### Windows

1. 게임을 끄고 [최신 릴리스](https://github.com/nyabi-gh/SephPlanner/releases/latest)에서 `SephPlanner-v버전.zip`을 받습니다. (`Source code`가 아닙니다.)
2. Steam에서 세피리아 우클릭 → **관리 → 로컬 파일 보기**로 게임 폴더를 엽니다.
3. 압축을 푼 뒤 `게임 폴더에 복사` 폴더 **안의 내용물**을 게임 폴더에 복사합니다.
4. 게임을 실행하고 탐험에 들어가면 화면에 세피 플래너가 나타납니다.

### macOS

1. 게임을 끄고 `SephPlanner-macos-v버전.zip`을 받아 `게임 폴더에 복사` 안의 내용물을 게임 폴더에 복사합니다. (`Sephiria.app` 안이 아니라 옆에 둡니다.)
2. 터미널에서 다음을 실행합니다. `<게임 폴더>`는 실제 경로로 바꿉니다.
   ```sh
   xattr -c "<게임 폴더>/libdoorstop.dylib" "<게임 폴더>/run_bepinex.sh"
   chmod +x "<게임 폴더>/run_bepinex.sh"
   ```
3. Steam에서 세피리아 **속성 → 일반 → 시작 옵션**에 `"<게임 폴더>/run_bepinex.sh" %command%`를 입력합니다.
4. 게임을 실행합니다. 자세한 내용은 [macOS 설치 안내](docs/INSTALL-macos.txt)에 있습니다.

### 업데이트

새 버전이 나오면 게임을 켤 때 알림이 뜹니다. `업데이트`를 누르고 게임을 다시 시작하면 끝입니다.

직접 하려면 ZIP의 `BepInEx/plugins` 안 DLL 두 개(`SephPlanner.Plugin.dll`, `SephPlanner.Core.dll`)를 게임 폴더의 같은 위치에 덮어씁니다. 이미 다른 모드로 BepInEx를 쓰고 있다면 이 두 파일만 넣으면 됩니다.

## 사용법

탐험 중에는 현재 가방 점수와 제안 배치 점수가 표시되고, 바뀌는 자리는 금색 테두리로 표시됩니다. 칸에 커서를 올리면 효과와 추천 이유를 볼 수 있습니다.

- **자동 배치:** `F8`을 누르면 제안 배치를 적용합니다.
- **수동 배치:** 이동 안내를 위에서부터 따라 옮깁니다.
- **후보 미리보기:** `F1`로 아직 얻지 않은 후보를 넣었을 때의 배치를 미리 봅니다.

점수는 배치 비교용 자체 평가값이며 게임의 공식 수치가 아닙니다. 고유 효과나 원하는 플레이 방식도 함께 고려해 주세요.

### 단축키

| 키 | 기능 |
|---|---|
| `F1` | 후보 미리보기 |
| `F2` | 빌드 설정 |
| `F3` | 설정 |
| `F4` | 패널 숨기기 |
| `F5` | 패널 불투명도 |
| `F6` | 패널 이동 |
| `F7` | 패널 접기·펼치기 |
| `F8` | 제안 배치 적용 |
| `F9` | 아이템 데이터 다시 읽기 |
| `F10` | 오류 제보용 상태 저장 |

키는 `F3`에서 바꿀 수 있습니다. 게임패드는 `F3`에서 `패드 View 버튼으로 창 열기`를 켜면 사용할 수 있습니다.

### 멀티플레이

멀티플레이에서 자동 배치는 기본적으로 꺼져 있습니다. 함께하는 사람들의 동의를 받은 뒤 `F3`의 `멀티 자동 배치`에서 켜세요. 서버 반영 경고가 뜨면 방에 다시 접속한 뒤 사용합니다.

빌드 지정, 합성·인챈트 추천, 아이템별 규칙 등은 **[자세한 사용법](docs/USAGE.md)**에서 볼 수 있습니다.

## 문제가 생겼을 때

| 증상 | 해결 |
|---|---|
| 패널이 안 보임 | 탐험에 들어갔는지, `F4`로 숨기지 않았는지, DLL 두 개가 `BepInEx/plugins`에 있는지 확인합니다. |
| 게임 패치 후 인식이 이상함 | `F9`로 아이템 데이터를 다시 읽습니다. |
| 추천이 안 보임 | `F3`의 `획득·합성·인챈트·제거 추천`이 켜져 있는지 확인합니다. |
| 단축키가 안 먹힘 | `F3` 단축키 탭에서 충돌 여부를 확인합니다. |

그래도 해결되지 않으면 문제가 생긴 상태에서 `F10`을 누르고, 화면에 나온 **제보 번호**와 함께 [Issues](https://github.com/nyabi-gh/SephPlanner/issues)나 [nyabi@tb.pro](mailto:nyabi@tb.pro)로 알려 주세요. 저장된 진단 파일은 게임 데이터가 들어 있으니 공개 Issues에 올리지 마세요. ([제보 안내](docs/USAGE.md#제보에-필요한-정보))

## 제거

게임을 끄고 `BepInEx/plugins`에서 `SephPlanner.Plugin.dll`과 `SephPlanner.Core.dll`을 지웁니다. 설정까지 지우려면 `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`와 `%LOCALAPPDATA%\SephPlanner` 폴더도 삭제합니다. 다른 모드를 쓰고 있다면 BepInEx는 그대로 두세요.

## 라이선스

[MIT 라이선스](LICENSE)로 자유롭게 쓰고 고치고 배포할 수 있습니다. 개발사의 모드 정책에 따라 광고·후원·구독 없이 비영리로 운영합니다. 공유할 때는 파일 대신 [릴리스 링크](https://github.com/nyabi-gh/SephPlanner/releases/latest)를 전해 주세요.

개발에 참여하려면 [개발 문서](docs/DEVELOPMENT.md)를 참고하세요.
