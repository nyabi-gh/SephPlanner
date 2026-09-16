# SephPlanner 개발 문서

사용자용 문서는 저장소 대문의 [README](../README.md)와 [자세한 사용법](USAGE.md)에 있다.
여기에는 저장소를 받아 고치고 배포하는 데 필요한 것과, 화면을 그렇게 만든 이유를 둔다.

## 구조

```
SephPlanner.Core      게임 상태 모델 + 솔버와 배치 계산 (netstandard2.1)
SephPlanner.Plugin    BepInEx 플러그인. 게임 상태를 읽어 배치를 풀고 게임 HUD 안에 직접
                      그린다. 멀티 세션의 자동 배치는 설정으로 연다(실험)
SephPlanner.DataTool  게임 없이 도는 CLI. 텍스트 추출, 스냅샷 대조, 콤보·아티팩트 값어치 측정
SephPlanner.Tests     Core 단위 테스트. 게임 없이 돈다
```

## 빌드

게임이 설치돼 있어야 한다. 기본 경로가 아니면 환경변수로 알려준다.

```powershell
$env:SEPHIRIA_DIR = "D:\SteamLibrary\steamapps\common\Sephiria"
dotnet build
```

일반 빌드는 게임 폴더를 변경하지 않는다. 빌드 결과를 설치된 게임에 복사하려면 명시적으로 배치한다.

```powershell
scripts/deploy-plugin.ps1
```

테스트는 게임 없이 돈다.

```powershell
dotnet test
```

밀기 전에는 이것 하나면 된다. 포맷·테스트에 더해 **플러그인 빌드까지** 세운다.

```powershell
scripts/check.ps1
```

마지막 항목이 따로 필요한 이유가 있다. CI 는 `SephPlanner.Core`, 진단 도구와 테스트를 검사한다. 플러그인은
게임 어셈블리를 참조하는데 그것을 저장소나 CI 에 둘 수 없기 때문이다(docs/LEGAL.md). 그래서
**플러그인만 깨지는 변경은 CI 를 초록으로 통과한다.** 릴리스 스크립트가 결국 잡아내지만, 그때는
이미 배포를 자르는 중이다.

게임 안에서 F10을 누르면 그 순간의 상태가 `%LOCALAPPDATA%\SephPlanner\inventory-snapshot.json`
으로 남는다. 그 스냅샷으로 우리 레벨 계산이 게임 값과 맞는지 게임 없이 다시 볼 수 있다. 어긋나는
칸이 있으면 아직 읽지 않는 효과(각인, 세트 효과, 배치 보너스)가 걸려 있다는 뜻이다. 제보를 받을
때 이 파일로 레벨을 대조할 수 있다. 당시 설정과 카탈로그까지 필요한 계획 재현에는 아래 재현 자료를 쓴다.

F10은 마지막으로 게시된 계획의 입력·빌드 설정·직전 목표·당시 카탈로그를
같은 데이터 폴더의 `reproductions/<시각>-<식별자>.replay`에 함께 저장한다.
계산 중이라면 마지막 게시 계획이므로 F10 순간의 새 스냅샷과 다를 수 있다.
아직 게시된 계획이 없으면 재현 자료는 만들지 않고 안내한다.

**진단 전송을 켰으면 F10이 메모 창을 먼저 연다.** 자료만으로는 무엇이 이상했는지 알 수 없기
때문이다. 증상을 골라 한 번 누르고 끝내도 되고 직접 적어도 된다. **`Enter` 가 보내기, `ESC` 가
취소다** - 취소해도 수집한 자료는 이 PC에 그대로 남고 서버로만 가지 않는다. 비워 둔 채 보내는
것이 곧 메모 없이 보내기다. 적은 그대로 가며 우리가 가리지 않으므로 아이디나 비밀번호 같은
것은 적지 않는다. 아직 전송에 동의하지 않았다면 동의를 먼저 묻고 그다음에 메모를 받으며,
로컬에만 저장하는 상태에서는 아예 묻지 않는다.

```powershell
dotnet run --project src/SephPlanner.DataTool -c Release -- --reproduce "<파일.replay>"
# 계산 코드를 바꾼 뒤 같은 입력으로 비교할 때
dotnet run --project src/SephPlanner.DataTool -c Release -- --reproduce "<파일.replay>" --allow-model-change
```

재현은 파일에 포함된 카탈로그만 사용하며 파일 손상·지원하지 않는 형식·빠진 설정은 실패로 알린다.
계산 빌드가 다르면 명시적인 비교 옵션이 필요하다. 종료 코드는 일치 0, 입력·실행 실패 1, 결과 차이 2다.
게임 데이터가 포함된 로컬 진단 자료이므로 저장소·릴리스·공개 제보에 첨부하지 않는다.
자세한 비교 범위는 [계획 재현](REPRODUCTION.md), 현재 개발 상태는 [현재 상태](STATUS.md)에 있다.

```powershell
dotnet run --project src/SephPlanner.DataTool -- --check %LOCALAPPDATA%\SephPlanner\inventory-snapshot.json
```

같은 스냅샷으로 `F8` 을 연달아 누르는 것을 흉내 낸다. 계획대로 옮기고, 게임이 다시 셀 레벨과
콤보 개수를 우리 셈으로 채운 뒤 다시 푼다. 적용한 뒤에도 계획이 또 바뀌면 그 이유가 여기서
드러난다 - 실제로 침 둘이 두 콤보 사이를 오가며 `F8` 마다 6~7수를 내던 판을 이것으로 잡았다.

```powershell
dotnet run --project src/SephPlanner.DataTool -- --churn %LOCALAPPDATA%\SephPlanner\inventory-snapshot.json
```

콤보 한 단계가 아티팩트 레벨 몇 개 값어치인지 잰다. `Worth`의 상수를 짐작으로 두지 않기 위한
것이며, 게임을 한 번 켜서 카탈로그를 다시 덤프한 뒤에 돌아간다(방법은 docs/RESEARCH.md 참고).

```powershell
dotnet run --project src/SephPlanner.DataTool -- --measure
```

아티팩트 하나하나의 값어치가 어디까지 측정됐는지 보고, 아직 레어도 어림값에 기대고 있는 것들을
손으로 채울 초안으로 뽑는다. 채우는 법은 [data/values/README.md](../data/values/README.md) 참고.

**레벨이 올라도 값어치가 내려가는 아티팩트도 함께 적는다.** 그런 구간이 있으면 낮은 레벨 칸이
정답이 되어 "왜 이걸 낮은 자리에 두느냐"는 제보가 온다. 원인이 된 능력치와 그 수치 변화까지
적으므로 환산율을 어디서 다시 재야 하는지가 바로 나온다.

```powershell
dotnet run --project src/SephPlanner.DataTool -- --values
```

데이터 추출:

```powershell
dotnet run --project src/SephPlanner.DataTool
# -> data/generated/text.json  (석판 68종, 아티팩트 257종, 15개 언어)
```

`data/generated/`는 각자 PC에서 생성되며 저장소에 커밋하지 않는다. 이유는 [LEGAL.md](LEGAL.md) 참고.

## 배포

```powershell
scripts/make-release.ps1
# -> artifacts/SephPlanner-v{버전}.zip
```

배포물에는 플러그인 DLL 두 개와 BepInEx 5.4.23.5(win_x64, Mono), 설치 안내와 라이선스가
들어간다. 릴리스 전에 포맷, 테스트, 빌드를 검증하고 커밋과 파일 해시가 적힌 `manifest.json`을
만든다. 게임 파일은 넣지 않는다.

zip은 이 저장소의 Releases 에 올린다. 변경 기록은 CHANGELOG.md 가 정본이고, 릴리스 본문은
`make-release.ps1`이 그 절을 그대로 뽑아 `artifacts/release-notes-v{버전}.md`로 내준다 - 손으로
옮겨 적지 않는다. 두 벌이 되면 한쪽만 고친 채 나가기 때문이다. 커뮤니티 글에는 Releases 링크를
걸고 zip을 직접 첨부하지 않는다 - 첨부하면 옛 버전이 돌아다닌다.

**릴리스 제목은 `SephPlanner {버전}`, 태그는 `v{버전}`이다.** 드래프트도 같은 제목을 쓴다.
예를 들어 제목은 `SephPlanner 0.4.5`, 태그는 `v0.4.5`다. 만들기 전에 최근 릴리스의 제목과
본문 형식을 확인하고, 사용자가 수정한 드래프트는 현재 내용을 읽은 뒤 필요한 부분만 갱신한다.

**소스와 배포본이 한 저장소에 있다**(`nyabi-gh/SephPlanner`, 공개). 2026-09-13 에 합쳤고 옛
소스 저장소는 `SephPlanner-archive` 로 이름을 바꿔 잠갔다. 경위와, 그때 일부러 남긴 것들은
[현재 상태](STATUS.md)의 "저장소 공개와 합치기" 에 있다 - **옛 태그가 로컬과 원격에서 갈라져
있으므로 `git fetch --tags` 의 clobber 경고는 정상이고 `--force` 로 맞추지 않는다.**

**zip 의 이름과 `manifest.json` 은 게임 안 업데이트가 읽는다.** 플러그인은 `releases/latest` 가
가리키는 태그 `v{버전}` 에서 `SephPlanner-v{버전}.zip` 을 받고, 안의 `manifest.json` 으로 버전과
DLL 둘의 SHA-256 을 대조한다(`UpdateClient`·`UpdatePackage`). 자산 이름·zip 안 경로·manifest
의 `version`/`files` 모양을 바꾸면 이미 깔린 판의 업데이트가 끊긴다. 시험 버전은 `Pre-release`
로 올려야 안정판 사용자에게 안내되지 않는다.

**태그를 먼저 달고 zip을 만든다.** manifest에 적히는 커밋이 곧 태그가 가리켜야 할 커밋이라,
나중에 달면 zip을 만든 커밋과 태그가 갈라져 어느 소스에서 나온 zip인지 되짚을 수 없다.
`make-release.ps1`이 HEAD에 `v{버전}` 태그가 없으면 멈추고, `[BepInPlugin]`의 버전과
CHANGELOG의 해당 절도 함께 확인한다.

## 화면 설계

**인게임 HUD 가 본체다.** 플러그인이 게임 HUD 캔버스 안에 직접 그린다. 별도 창이 아니라 게임 UI 의
일부라 전용 전체화면에서도 보이고, 글꼴과 아이콘을 게임에서 빌려 쓴다. 크기는 게임 HUD 글자
크기에 대한 비율이라 해상도와 UI 배율을 따라간다. 게임만 켜면 저절로 올라온다.

**마우스와 키보드를 하나도 가져가지 않는다.** 그리는 것마다 `raycastTarget` 이 꺼져 있고
컨트롤 스택에도 들어가지 않는다. 그래서 누를 것이 없고 조작은 전부 단축키다. 커서 위치는
읽지만(이동 모드와 쪽지) 읽기만 하는 것이라 게임에서 가져가는 입력은 없다.

**기본 단축키는 게임이 쓰지 않는 F 키로 고른다.** 게임은 수정키를 보지 않아서 `Ctrl+Alt+P` 같은
조합도 글자 키가 게임 조작을 함께 발동시킨다. F 키는 게임이 하나도 쓰지 않는다
([RESEARCH.md](RESEARCH.md) 의 "게임 단축키"). 키 목록은 [README](../README.md)에 있다.

### 접힘과 펼침

읽는 방식이 달라서 둘로 나눴다.

- **접힘.** 플레이 중에는 화면을 거의 가리지 않는다. 현재 점수와 탐색한 배치 점수, 그리고 지금
  옮길 것 하나만 보여준다.
- **펼침.** 상자나 상점이 열리거나 세피라이트를 여는 순간 저절로 펼쳐지고, 닫히면 되돌아간다.
  격자에 칸마다 놓일 아이템과 레벨이 나오고, 무엇을 어디로 옮길지와 지금 집을 수 있는 후보가
  줄 세워진다. 직접 접거나 펼친 상태는 상자·상점이 다시 열리거나 닫힐 때까지 유지된다.

**접으면 정말 한 줄만 남는다.** 점수와 지금 옮길 것 하나뿐이고, 폭도 제한되며,
단축키 안내도 사라진다. 안내는 화면이 처음 뜰 때와 키를 누른 뒤 잠깐만 보인다. 게임 화면을
가리지 않는 것이 접는 이유이므로 접힌 상태에 무엇을 더 남기지 않는다.

예외는 경고다. 점수를 믿을 수 없거나 멀티 세션이라는 것처럼 **모르고 넘어가면 안 되는 것**은
접혀 있어도 뜨고, 접힌 폭에 맞춰 여러 줄로 접힌다. 자동 배치가 도는 동안 뜨는 진행 표시도
그렇다.

### 쪽지 (커서를 올리면)

아티팩트가 무슨 일을 하는지, 값어치를 어떻게 정했는지, 왜 꺼졌는지, 집으면 무엇이 빠지는지가
거기 있다. 격자 칸과 후보·합성 줄에 뜬다. **여기서도 입력은 가져가지 않는다** — 커서 좌표를
읽어 우리가 그린 사각형과 겹치는지 직접 세므로 `raycastTarget` 을 켜지 않는다. 펼쳤을 때만
뜬다. 접힌 화면은 곁눈질용 한 줄이라 그때 쪽지가 뜨면 플레이를 가린다.

### 창은 입력을 받는다

빌드 창과 설정 창은 **입력을 받는다.** HUD 의 무입력 원칙은 플레이 중 조작을 방해하지 않기
위한 것인데, 이 창들은 플레이어가 일부러 연 것이라 그 이유가 걸리지 않는다. 게임의 `UIBase`를
상속해 컨트롤 스택에 올라가므로 **여는 동안 캐릭터 조작이 멈추고 ESC 로도 닫힌다** — 게임의 다른
창과 똑같이 행동한다. 닫으면 그 자리에 아무것도 남지 않아 HUD 의 보장은 그대로다.

**여는 동안 시간도 멈춘다**(싱글 한정, 게임의 `GameTimeManager`가 멀티에서는 스스로 거부한다).
게임에서 설정을 만지는 길은 ESC 일시정지 창을 거치는 것이라 그때는 시간이 이미 멈춰 있다.
우리 창만 시간이 흐르면 만지는 사이에 얻어맞는다.

게임 자체의 설정 창에 탭으로 붙이는 길은 만들어 보고 접었다. 그 창이 탭 다섯 개에 딱 맞게 짜여
있어 우리 탭이 들어갈 자리가 없고, 선택 표시가 탭 그림마다 박혀 있어 버튼을 옮기면 게임 탭들이
어긋난다. 잰 값은 [RESEARCH.md](RESEARCH.md) 에 남겼다.

## 원칙

- **쓰기는 자동 배치 하나뿐이다.** 그 밖에는 게임 상태를 읽기만 한다. 멀티에서는 기본으로
  잠기고, 설정에서 여는 실험 옵션이 있다(켜면 방을 연 쪽과 참가한 쪽 모두에서 동작한다).
  싱글 자동 배치는 개발사에게 허락받았다. 근거와 경위는
  [LEGAL.md](LEGAL.md) 참고.
- **게임 저작물을 배포하지 않는다.** 데이터는 각자 PC의 게임 설치본에서 생성한다.
- **비영리.**

## 문서

- [STATUS.md](STATUS.md) — 현재 구현·추정·실기 미검증 범위와 다음 작업. 설계·감사·제보 문서는 여기서 잇는다
- [REPRODUCTION.md](REPRODUCTION.md) — F10 계획 재현 자료와 진단 도구 사용법
- [DIAGNOSTIC-UPLOAD.md](DIAGNOSTIC-UPLOAD.md) — F10 비공개 진단 전송과 서버 배포 인계

- [INSTALL.txt](INSTALL.txt) — 사용자용 설치 안내. 메모장에서 그대로 읽히도록 마크다운 없이 평문으로 쓰고, 배포 zip에 `설치안내.txt`로 들어간다
- [RESEARCH.md](RESEARCH.md) — 게임 내부 구조 조사 결과
- [LEGAL.md](LEGAL.md) — 약관·저작권 검토와 그에 따른 설계 제약
- [ROADMAP.md](ROADMAP.md) — 백로그, 측정 기준선, 고치지 않기로 한 것
- [PERFORMANCE.md](PERFORMANCE.md) — 성능과 응답성. 지금 값, 남은 일감, 지켜야 할 규약
- [CHANGELOG.md](CHANGELOG.md) — 버전별 사용자 영향 변경 사항
- [PLACEMENT-OBJECTIVE.md](PLACEMENT-OBJECTIVE.md) — 배치 평가 순서, 활성 보호, 선호도 가중치, 사용 유지
- [HANDOVER.md](HANDOVER.md) — 직전 세션이 남긴 길잡이. 세션마다 덮어쓰며, STATUS 와 어긋나면 STATUS 가 정본이다
- [notes/](notes) — 지난 검토·감사·제보 분석과 측정 기록. 그때의 사실이며 현재 상태가 아니다
- [LICENSE](../LICENSE) — MIT. 우리가 비영리인 것은 운영 방침이지 라이선스 조건이 아니다
