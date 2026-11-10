<h1 align="center">SephPlanner</h1>

<p align="center">
  세피리아의 석판·아티팩트 배치를 계산해 게임 화면에 보여 주는 모드
</p>

<p align="center">
  <a href="https://github.com/nyabi-gh/SephPlanner/releases/latest"><img src="https://img.shields.io/github/v/release/nyabi-gh/SephPlanner?style=flat-square&label=%EC%B5%9C%EC%8B%A0&color=b9a7f5" alt="최신 릴리스"></a>
  <a href="https://github.com/nyabi-gh/SephPlanner/releases"><img src="https://img.shields.io/github/downloads/nyabi-gh/SephPlanner/total?style=flat-square&label=%EB%8B%A4%EC%9A%B4%EB%A1%9C%EB%93%9C&color=77d8b0" alt="다운로드"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/%EB%9D%BC%EC%9D%B4%EC%84%A0%EC%8A%A4-MIT-b9a7f5?style=flat-square" alt="라이선스"></a>
</p>

<p align="center"><b>한국어</b> · <a href="README.en.md">English</a> · <a href="README.ja.md">日本語</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.zh-TW.md">繁體中文</a></p>

## 다운로드

게임 브랜치에 따라 받는 곳이 다릅니다.

| 게임 | 다운로드 |
| --- | --- |
| **1.1 베타** (`workshop-beta` 브랜치) | [Steam 창작마당](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)에서 구독 |
| **기본 브랜치**, Windows | [최신 릴리스](https://github.com/nyabi-gh/SephPlanner/releases/latest)의 `SephPlanner-v버전.zip` |
| **기본 브랜치**, macOS | [최신 릴리스](https://github.com/nyabi-gh/SephPlanner/releases/latest)의 `SephPlanner-macos-v버전.zip` |

ZIP은 BepInEx가 들어 있는 판으로, 1.1 베타에서도 돌아갑니다. `Source code`가 아니라 위 ZIP을 받습니다.

> [!NOTE]
> TEAM HORAY와 관계없는 개인이 만든 모드입니다. 모드 사용은 본인 책임이며, 중요한 세이브는 미리 백업해 두세요.

## 설치

### 창작마당 (게임 1.1 베타)

1. Steam에서 세피리아 우클릭 → **속성 → 게임 버전 및 베타**에서 `workshop-beta`를 고릅니다.
2. [창작마당 글](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875)에서 **구독**을 누릅니다.
3. 게임을 켜고 타이틀의 창작마당 창에서 SephPlanner가 켜져 있는지 확인합니다.
4. 탐험에 들어가면 화면에 SephPlanner가 나타납니다.

업데이트는 Steam이 알아서 받습니다.

**BepInEx 판(ZIP)을 쓰던 분은** 게임 폴더의 `BepInEx/plugins`에서 `SephPlanner.Plugin.dll`과 `SephPlanner.Core.dll`을 지워야 창작마당 판이 켜집니다. 그대로 두면 BepInEx 판만 실행됩니다. 쓰던 설정은 창작마당 판을 처음 켤 때 가져옵니다.

### ZIP (BepInEx 판)

**Windows**

1. 게임을 끄고 ZIP의 압축을 풉니다.
2. Steam에서 세피리아 우클릭 → **관리 → 로컬 파일 보기**로 게임 폴더를 엽니다.
3. `게임 폴더에 복사` 폴더 **안의 내용물**을 게임 폴더에 복사합니다.
4. 게임을 실행하고 탐험에 들어가면 화면에 SephPlanner가 나타납니다.

**macOS**

1. 게임을 끄고 `게임 폴더에 복사` 안의 내용물을 게임 폴더에 복사합니다. (`Sephiria.app` 안이 아니라 옆에 둡니다.)
2. 터미널에서 다음을 실행합니다. `<게임 폴더>`는 실제 경로로 바꿉니다.
   ```sh
   xattr -c "<게임 폴더>/libdoorstop.dylib" "<게임 폴더>/run_bepinex.sh"
   chmod +x "<게임 폴더>/run_bepinex.sh"
   ```
3. Steam에서 세피리아 **속성 → 일반 → 시작 옵션**에 `"<게임 폴더>/run_bepinex.sh" %command%`를 입력합니다.
4. 게임을 실행합니다. 자세한 내용은 [macOS 설치 안내](docs/INSTALL-macos.txt)에 있습니다.

ZIP 판은 스스로 업데이트합니다. 새 버전이 나오면 게임을 켤 때 알려 주고, `업데이트`를 누른 뒤 게임을 다시 시작하면 적용됩니다.

이미 다른 모드로 BepInEx를 쓰고 있다면 ZIP의 `BepInEx/plugins` 안 DLL 두 개(`SephPlanner.Plugin.dll`, `SephPlanner.Core.dll`)만 게임 폴더의 같은 위치에 넣으면 됩니다. 직접 업데이트할 때도 이 두 파일을 덮어씁니다.

## 사용 방법

1. **탐험에 들어갑니다.** 화면 구석에 현재 가방 점수와 제안 배치 점수가 뜹니다.
2. **`F7`로 펼칩니다.** 바뀌는 자리가 금색 테두리로 표시되고, 칸에 커서를 올리면 효과와 추천 이유가 나옵니다. 상자·상점·세피라이트·합성기·인챈트 창을 열면 저절로 펼쳐집니다.
3. **`F8`을 누릅니다.** 제안 배치가 적용됩니다. 직접 옮기고 싶다면 이동 안내를 위에서부터 따라 하면 됩니다.

상자·상점·세피라이트에서 무엇을 고를지, 어떤 석판을 합성하고 어디에 인챈트할지도 추천합니다. `F1`로 아직 얻지 않은 후보를 넣었을 때의 배치를 미리 볼 수 있습니다.

> [!IMPORTANT]
> 점수는 배치 비교용 자체 평가값이며 게임의 공식 수치가 아닙니다. 고유 효과나 원하는 플레이 방식도 함께 고려해 주세요.

빌드 지정, 아이템별 규칙 등은 [자세한 사용법](docs/USAGE.md)에 있습니다.

## 단축키

| 키 | 기능 |
| --- | --- |
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

키는 `F3`에서 바꿀 수 있습니다. 게임패드는 가방·상자 같은 게임 창이 떠 있을 때 `View` 버튼으로 창을 엽니다.

## 멀티플레이

SephPlanner는 내 화면에만 적용되므로 함께하는 사람은 설치하지 않아도 됩니다.

멀티플레이에서 자동 배치는 기본적으로 꺼져 있습니다. 함께하는 사람들의 동의를 받은 뒤 `F3`의 `멀티 자동 배치`에서 켜세요. 서버 반영 경고가 뜨면 방에 다시 접속한 뒤 사용합니다.

## 자주 묻는 질문

**패널이 안 보여요.** 탐험에 들어갔는지, `F4`로 숨기지 않았는지, `F3`의 `플래너 화면 표시`가 켜져 있는지 확인합니다. 창작마당 판이라면 타이틀의 창작마당 창에서 켜져 있는지, `BepInEx/plugins`에 SephPlanner DLL이 남아 있지 않은지 봅니다. ZIP 판이라면 DLL 두 개가 `BepInEx/plugins`에 있는지 봅니다.

**게임 패치 후 인식이 이상해요.** `F9`로 아이템 데이터를 다시 읽습니다.

**계산이 오래 걸려요.** `F3`의 `고사양 모드(여러 코어로 계산)`를 켜면 CPU 코어를 더 써서 빨리 계산합니다. 결과는 같습니다. 게임이 끊기면 다시 끄세요.

**추천이 안 보여요.** `F3`의 `획득·합성·인챈트·제거 추천`이 켜져 있는지 확인합니다.

**단축키가 안 먹혀요.** 단축키에 없는 Ctrl·Alt·Shift·Cmd 를 누르고 있으면 단축키가 듣지 않습니다. 그 키에서 손을 뗀 뒤 누르세요. 이동 키는 누른 채여도 됩니다. `F3` 단축키 탭에서 다른 키와 겹치지 않는지도 확인합니다. `플래너 화면 표시`를 끄면 `F1`·`F4`~`F8`도 멈춥니다.

**그래도 해결되지 않아요.** 문제가 생긴 상태에서 `F10`을 누르고, 화면에 나온 **제보 번호**와 함께 [Issues](https://github.com/nyabi-gh/SephPlanner/issues)나 창작마당 글의 댓글, [nyabi@tb.pro](mailto:nyabi@tb.pro)로 알려 주세요. 저장된 진단 파일에는 게임 데이터가 들어 있으니 공개 Issues에 올리지 마세요. ([제보 안내](docs/USAGE.md#제보에-필요한-정보))

**지우고 싶어요.** 창작마당 판은 창작마당 글에서 구독을 취소합니다. ZIP 판은 게임을 끄고 `BepInEx/plugins`에서 `SephPlanner.Plugin.dll`과 `SephPlanner.Core.dll`을 지웁니다. 설정까지 지우려면 `%LOCALAPPDATA%\SephPlanner` 폴더와 `BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`도 삭제합니다. 다른 모드를 쓰고 있다면 BepInEx는 그대로 두세요.

## 라이선스

SephPlanner는 [MIT 라이선스](LICENSE)로 자유롭게 쓰고 고치고 배포할 수 있습니다.

개발사의 모드 정책에 따라 광고·후원·구독 없이 비영리로 운영합니다. 공유할 때는 파일 대신 [릴리스](https://github.com/nyabi-gh/SephPlanner/releases/latest)나 [창작마당 글](https://steamcommunity.com/sharedfiles/filedetails/?id=3832790875) 링크를 전해 주세요.

## 개발 참여

오류 제보와 풀 리퀘스트를 환영합니다. 빌드와 테스트 방법은 [개발 문서](docs/DEVELOPMENT.md)에 있습니다.
