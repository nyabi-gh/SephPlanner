# SephPlanner 개발 문서

사용자용 문서는 저장소 대문의 [README](../README.md)와 [자세한 사용법](USAGE.md)에 있다.
여기에는 저장소를 받아 고치고 배포하는 데 필요한 것과, 화면을 그렇게 만든 이유를 둔다.
어느 문서가 무엇을 맡는지는 [현재 상태](STATUS.md)의 "문서 지도" 에 있다. 문서를 새로 만들기 전에 거기 들어갈 자리가
있는지 먼저 본다.

## 구조

```
SephPlanner.Core      게임 상태 모델 + 솔버와 배치 계산 (netstandard2.1)
SephPlanner.Plugin    플러그인 본체. 게임 상태를 읽어 배치를 풀고 게임 HUD 안에 직접 그린다.
                      멀티 세션의 자동 배치는 설정으로 연다. 이 프로젝트가 BepInEx 판을 짓는다.
                      Host/        두 판이 함께 쓰는 설정·단축키·로그 층
                      Net/         진단 전송(두 판). 화면은 Plugin.Diagnostics.cs 와 Ui/ 의 동의·메모 창
                      BepInExHost/ BepInEx 판에만 드는 것 - 진입점, 설정 파일, 게임 안 업데이트(Net/ 포함)
SephPlanner.Workshop  창작마당 판. Plugin 소스를 BepInExHost 없이 링크하고 게임 모드 로더의 진입점과
                      모드 옵션 저장소를 더한다. 게임 1.1.x 로만 빌드된다
SephPlanner.DataTool  게임 없이 도는 CLI. 텍스트 추출, 스냅샷 대조, 콤보·아티팩트 값어치 측정
SephPlanner.Diagnostics  F10 진단을 받는 수집 서버(ASP.NET Core). docs/DIAGNOSTIC-UPLOAD.md
SephPlanner.Tests     (tests/) 단위 테스트. Core·진단 서버를 참조하고 플러그인·DataTool 의 일부 파일을 링크한다.
                      게임 없이 돈다
SephPlanner.Plugin.Net  (tests/) Net/ 과 BepInExHost/Net 을 배포와 같은 netstandard2.1 로 지어 시험에 넘긴다
```

**진단 전송은 두 판에, 게임 안 업데이트는 BepInEx 판에만 있다.** 창작마당 규칙 5 는 무엇을 보내는지 이용자에게 알리면 게임
관련 정보의 전송을 허용하고, 실행 중 DLL 을 바꾸는 모드는 로드되지 않는다(docs/LEGAL.md 의 "창작마당 모드 규칙").

- 네트워크 코드를 따로 어셈블리로 빼지 않는다. 0.5.1 까지의 게임 안 업데이트는 ZIP 에서 DLL 둘(`SephPlanner.Plugin.dll`·
  `SephPlanner.Core.dll`)만 꺼내므로, BepInEx 판에 셋째 DLL 이 생기면 옛 판에서 업데이트한 사람의 플러그인이 뜨지 않는다.
- 플러그인 본체는 `partial` 클래스다. 업데이트 자리는 몸체 없는 `partial void` 라 창작마당 판에서는 부르는 자리째 빠진다.

## 빌드

게임 어셈블리를 참조하므로 게임이 설치돼 있어야 한다. 경로는 `Directory.Build.props` 가 정한다. 기본값은 Windows 가
`D:\SteamLibrary\steamapps\common\Sephiria`, macOS 가 `~/Library/Application Support/Steam/steamapps/common/Sephiria` 다.
다른 곳에 설치했으면 `SEPHIRIA_DIR` 에 게임 폴더를 준다(macOS 는 `Sephiria.app` 의 상위 폴더).

```powershell
$env:SEPHIRIA_DIR = "C:\Program Files (x86)\Steam\steamapps\common\Sephiria"
dotnet build
```

일반 빌드는 게임 폴더를 바꾸지 않는다. 설치된 게임의 `BepInEx/plugins` 에 넣으려면 따로 배치한다. 배치한 DLL 이 방금
빌드한 것과 다르면(게임이 켜져 있어 덮어쓰지 못했으면) 멈춘다.

```powershell
scripts/deploy-plugin.ps1
```

밀기 전에는 이것 하나면 된다. 판 번호 대조·포맷·테스트에 더해 **플러그인 빌드까지** 세운다. 테스트만 돌릴 때는
`dotnet test` 다.

```powershell
scripts/check.ps1
```

**플러그인만 깨지는 변경은 CI 를 초록으로 통과한다.** CI(`.github/workflows/ci.yml`)는 Core·진단 서버·DataTool·테스트의
포맷과 테스트, DataTool 빌드만 본다. 플러그인은 게임 어셈블리를 참조하는데 그것을 저장소나 CI 에 둘 수 없다(docs/LEGAL.md).
릴리스 스크립트가 결국 잡아내지만 그때는 이미 배포를 자르는 중이다.

게임이 1.1.x 면 `check.ps1` 이 창작마당 판도 세운다. 1.0.33 으로 돌리면 모드 API 가 없어 건너뛰고 그렇다고 출력한다.
창작마당 판을 솔루션에 넣지 않은 것도 같은 까닭이다 - 넣으면 1.0.33 에서 솔루션 포맷 검사가 깨진다.

**1.0.33 과 1.1.x 를 함께 지원하는 동안** 플러그인은 두 판 어셈블리 모두로 빌드돼야 한다. 설치본 밖에 다른 판의
`Sephiria_Data/Managed` 사본을 두고 `SEPHIRIA_DIR` 만 바꿔 `scripts/check.ps1` 을 두 번 돈다. 한 판에만 있는 클래스는
컴파일 시점에 참조하지 않는다([게임 내부 조사](RESEARCH.md)의 "침은 1.1.0 에서 나침반이 됐다").

macOS용 BepInEx와 Doorstop 로더는 `scripts/build-macos-loader.sh`로 빌드하며, 결과는 `artifacts/macos-loader/`에 놓인다.
Xcode 명령줄 도구와 .NET SDK가 필요하다.

### 창작마당 판

패키지 폴더(`artifacts/workshop/sephplanner/`)를 짓고, 로컬에서 시험할 때는 게임의 `WorkshopLocal/sephplanner/` 로 복사한다.

```powershell
dotnet build src/SephPlanner.Workshop -c Release -p:PackWorkshop=true -p:DeployToWorkshopLocal=true
```

- 패키지는 `metadata.json`, `bin/SephPlanner.Plugin.dll`·`bin/SephPlanner.Core.dll`, `thumbnail.png` 다. 썸네일이 없거나
  1,000,000바이트 이상이면 게임이 올리기를 거절한다(`WorkshopUploadPreparer`). 원본은
  `src/SephPlanner.Workshop/package/` 에 있고 `metadata.json` 의 `version` 은 `check.ps1` 이 `Directory.Build.props` 와 대조한다.
  게임은 이 값을 창작마당 글의 변경 사항(`v` + 버전)으로 올리므로 내용을 바꾸면 반드시 올린다. 우리 모드는 `local_only` 라
  멀티 로비의 모드 판 비교에서는 빠진다([게임 내부 조사](RESEARCH.md)의 "공식 모드 API").
- `modId` 는 `nyabi.sephplanner` 다. 공개한 뒤에는 바꾸지 않는다 - 다른 모드가 되고 모드 옵션에 저장된 설정도 끊긴다.
- 어셈블리 이름은 BepInEx 판과 같은 `SephPlanner.Plugin` 이다. BepInEx 판이 깔린 게임에서는 BepInEx 가 먼저 싣고, 게임의
  모드 로더는 같은 이름이 이미 있으면 창작마당 판을 보류한다. 그래서 **로컬 시험은 BepInEx 를 끄고 한다** - 게임 폴더의
  `winhttp.dll` 이름을 잠시 바꾸면 BepInEx 가 뜨지 않는다.
- DLL 모드는 타이틀이 아니라 **월드에 들어갈 때** 켜진다. 로그는 BepInEx 로그가 아니라 게임의
  `%USERPROFILE%\AppData\LocalLow\TEAMHORAY\Sephiria\Player.log` 에 `[SephPlanner]` 머리로 남는다. 게임 쪽 줄은
  `[WorkshopModManager]`(계획)·`[WorkshopModRuntime]`(활성화) 다.
- `WorkshopLocal` 의 모드가 로드된 게임은 멀티가 막힌다. 멀티는 창작마당에 올려 구독한 사본으로 본다.
- 설정은 게임의 `DeviceOptions.xml` 에 `ModOptions:nyabi.sephplanner:섹션.열쇠` 로 들어간다. 아직 값이 없는 설정은 첫 실행에
  BepInEx 설정 파일(`BepInEx/config/dev.nyabi.sephplanner.bridge.cfg`)에서 가져온다(`BepInExConfigImport`). 진단 전송
  동의만은 가져오지 않는다 - 이 판에서 보낼 항목을 보고 다시 고른다.

**올리기는 게임 안에서만 된다.**

- 타이틀의 창작마당 창이 `WorkshopLocal/` 의 모드를 "내 모드" 로 보여 준다. 이 모드를 고르고 "창작마당에 올리기" 를 누르면
  올라가고, DLL 이 들었으므로 검수에 저절로 접수된다.
- 창작마당 창은 열 때와 게임 창으로 돌아올 때 `WorkshopLocal` 을 다시 읽고 올릴 파일은 누르는 순간 읽는다. 다시 지은 뒤
  게임을 껐다 켤 필요는 없다.
- 그 뒤 게임이 `WorkshopLocal/sephplanner/` 에 만드는 `uploadticket.json` 이 창작마당 글 번호를 들고 있다. 지우면 다음에
  올릴 때 새 글이 생긴다(위 배치 명령은 이 파일을 지우지 않는다).
- 처음에는 "목록에 없음" 으로 올라가므로 직접 구독해 돌려 본 뒤 공개로 바꾼다. 공개 여부는 갱신해도 그대로다.
- **올릴 때마다 게임이 글의 제목·설명·태그를 `metadata.json` 으로 다시 쓴다.** Steam 글 페이지에서 고친 설명은 다음에
  올리면 사라진다. 설명은 `info.description` 에서 `default` → `en-US` → 첫 항목 순으로 하나만 들어간다([게임 내부
  조사](RESEARCH.md)의 "공식 모드 API").
- 게임은 미리보기 한 장만 올린다(`SetItemPreview`). 스크린샷 같은 추가 이미지는 Steam 글 페이지에서 넣는다.

## 진단 도구

`SephPlanner.DataTool` 은 게임 없이 돈다. 한 번에 명령 하나만 받고, 전체 목록은 `--help` 가 보여 준다.

```powershell
dotnet run --project src/SephPlanner.DataTool -c Release -- --help
```

| 명령 | 하는 것 |
|---|---|
| 인자 없음(또는 게임 폴더) | 게임의 `StreamingAssets/Localization` 에서 석판·아티팩트 이름과 설명을 뽑아 `data/generated/text.json` 에 둔다 |
| `--check <스냅샷.json>` | F10 스냅샷으로 우리 레벨 계산이 게임 값과 맞는지 본다 |
| `--churn <스냅샷.json> [횟수]` | 같은 스냅샷으로 `F8` 을 연달아 누르는 것을 흉내 낸다 |
| `--reproduce <파일.replay>` | F10 재현 자료로 그때의 계획을 다시 푼다. [계획 재현](REPRODUCTION.md) |
| `--measure` | 콤보 한 단계가 아티팩트 레벨 몇 개 값어치인지 잰다 |
| `--values` | 아티팩트마다 값어치가 어디까지 측정됐는지 보고, 손으로 채울 초안을 뽑는다 |

- **F10 자료.** F10 은 누른 순간의 관측값을 `%LOCALAPPDATA%\SephPlanner\inventory-snapshot.json` 에, 마지막으로 게시된
  계획의 입력·설정·카탈로그를 `reproductions\` 의 `.replay` 에 남긴다. 둘은 같은 순간의 자료가 아니다([계획
  재현](REPRODUCTION.md)). 진단 전송을 켰으면 메모 창을 거쳐 서버로도 보낸다([진단 전송](DIAGNOSTIC-UPLOAD.md)).
  게임 데이터가 든 로컬 진단 자료이므로 저장소·릴리스·공개 제보에 첨부하지 않는다.
- **`--check`.** 어긋나는 칸이 있으면 아직 모델 밖인 효과가 걸려 있다는 뜻이다. 무엇이 모델에 들었는지는 [현재
  상태](STATUS.md)의 "구현 범위".
- **`--churn`.** 계획대로 옮기고, 게임이 다시 셀 레벨과 콤보 개수를 우리 셈으로 채운 뒤 다시 푼다. 적용한 뒤에도 계획이
  또 바뀌면 그 이유가 여기서 드러난다(침 둘이 두 콤보 사이를 오가며 `F8` 마다 6~7수를 내던 판을 이것으로 잡았다).
- **`--measure`.** `Worth` 의 상수를 짐작으로 두지 않기 위한 것이다. 게임을 한 번 켜서 카탈로그를 다시 덤프한 뒤에
  돈다(방법은 docs/RESEARCH.md).
- **`--values`.** 아직 레어도 어림값에 기대는 아티팩트를 초안으로 뽑는다. 채우는 법은
  [data/values/README.md](../data/values/README.md). **레벨이 올라도 값어치가 내려가는 아티팩트도 함께 적는다.** 그런
  구간이 있으면 낮은 레벨 칸이 정답이 되어 "왜 이걸 낮은 자리에 두느냐" 는 제보가 온다. 원인이 된 능력치와 그 수치
  변화까지 적으므로 환산율을 어디서 다시 재야 하는지가 바로 나온다.
- **텍스트 추출.** `data/generated/` 는 각자 PC에서 생성되며 저장소에 커밋하지 않는다. 이유는 [LEGAL.md](LEGAL.md).

## 배포

GitHub 릴리스(BepInEx 판 zip 둘)와 창작마당(위 "창작마당 판")은 따로 올린다. GitHub 릴리스는 이 순서다.

1. **판 번호를 다섯 곳에 적는다.** `Directory.Build.props` 의 `Version`, `BepInExHost/BepInExEntry.cs` 의 `[BepInPlugin]`,
   `src/SephPlanner.Workshop/package/metadata.json` 의 `version`, STATUS 의 `**이번 판: {버전}.**`, CHANGELOG 의
   `## {버전}` 절. 하나라도 어긋나면 `check.ps1` 이 멈춘다.
2. 커밋하고 **태그 `v{버전}` 을 먼저 단다.** manifest 에 적히는 커밋이 곧 태그가 가리켜야 할 커밋이라, 나중에 달면 zip 을
   만든 커밋과 태그가 갈라져 어느 소스에서 나온 zip 인지 되짚을 수 없다. `make-release.ps1` 은 작업 트리가 깨끗하지 않거나
   HEAD 에 태그가 없으면 멈춘다.
3. Windows 와 Mac 에서 각각 `make-release.ps1` 을 돌린다. `check.ps1` 을 먼저 돌고 zip 과 릴리스 본문을 짓는다.

   ```powershell
   scripts/make-release.ps1
   # -> artifacts/SephPlanner-v{버전}.zip (macOS 에서는 artifacts/SephPlanner-macos-v{버전}.zip)
   # -> artifacts/release-notes-v{버전}.md
   ```

4. **두 ZIP 을 한 드래프트에 올린다.** 먼저 도는 쪽이 `gh release create --draft`, 나중 쪽이 `gh release upload` 다
   (스크립트가 끝에 두 명령을 출력한다).
5. `scripts/publish-release.ps1` 로 공개한다. 드래프트에 두 ZIP 이 다 있을 때만 `--draft=false` 로 바꾸고 하나라도 빠지면
   거절한다. 업데이트 확인은 `releases/latest` 의 태그만 보고 창을 띄우므로, macOS ZIP 이 빠진 채 공개하면 Mac 사용자의
   `업데이트` 가 404 로 실패한다.

지킬 것.

- **배포물에는 게임 파일을 넣지 않는다.** 플러그인 DLL 두 개, 플랫폼에 맞는 BepInEx, 설치 안내, 라이선스와 커밋·파일
  해시가 적힌 `manifest.json` 이 들어간다. macOS 배포물은 `build-macos-loader.sh` 가 고정한 BepInEx·Doorstop·plthook
  소스와 `scripts/patches/macos-plthook.patch` 를 쓴다.
- **릴리스 본문은 손으로 옮겨 적지 않는다.** 변경 기록은 CHANGELOG.md 가 정본이고 `make-release.ps1` 이 그 절을 그대로
  뽑는다. 두 벌이 되면 한쪽만 고친 채 나가기 때문이다.
- **릴리스 제목은 `SephPlanner {버전}`, 태그는 `v{버전}`이다**(예: `SephPlanner 0.4.5`, `v0.4.5`). 드래프트도 같은 제목을
  쓴다. 만들기 전에 최근 릴리스의 제목과 본문 형식을 확인하고, 사용자가 수정한 드래프트는 현재 내용을 읽은 뒤 필요한
  부분만 갱신한다.
- **zip 의 이름과 `manifest.json` 은 게임 안 업데이트가 읽는다.** 플러그인은 `releases/latest` 가 가리키는 태그
  `v{버전}` 에서 운영체제에 맞는 ZIP 을 받고, 안의 `manifest.json` 으로 버전과 DLL 둘의 SHA-256 을 대조한다
  (`UpdateClient`·`UpdatePackage`). 자산 이름·zip 안 경로·manifest 의 `version`/`files` 모양을 바꾸면 이미 깔린 판의
  업데이트가 끊긴다. 시험 버전은 `Pre-release` 로 올려야 안정판 사용자에게 안내되지 않는다.
- 커뮤니티 글에는 Releases 링크를 걸고 zip 을 직접 첨부하지 않는다. 첨부하면 옛 버전이 돌아다닌다.

**소스와 배포본이 한 저장소에 있다**(`nyabi-gh/SephPlanner`, 공개). 2026-09-13 에 합쳤고 옛
소스 저장소는 `SephPlanner-archive` 로 이름을 바꿔 잠갔다(옛 주소 `SephPlanner-Release` 는 리다이렉트된다).

- **옛 태그가 로컬과 원격에서 갈라져 있다.** 원격의 `v0.1.0`~`v0.3.8` 은 옛 릴리스 저장소의 커밋을, 로컬의 같은
  이름은 소스 커밋을 가리킨다. `git fetch --tags` 의 "would clobber existing tag" 는 정상이고 **`--force` 로 맞추지
  않는다** - 릴리스 자산이 그 태그에 붙어 있다. `v0.3.9` 부터는 갈라지지 않는다.
- 합칠 때 커밋 메시지의 트레일러를 지우느라 히스토리를 한 번 다시 썼다. 그래서 공개된 옛 릴리스 노트와 zip
  manifest 의 커밋 번호는 이 저장소에서 찾을 수 없다.
- 진단 서버의 배포 파일은 저장소 밖에 있다([진단 전송](DIAGNOSTIC-UPLOAD.md)).

## 게임이 패치되면

카탈로그는 게임 버전·어셈블리가 바뀌면 스스로 다시 짓는다. 그 밖에 다시 보는 것이다. 어느 판까지 대조했는지는
[현재 상태](STATUS.md)의 "게임 버전" 에 있다.

1. 카탈로그의 질의 검증(`query-verification.txt`)이 불일치 0 인지, 플러그인이 새 어셈블리로 경고 없이 빌드되는지.
2. **쓰기 경로 셋이 그대로인지.** `GridInventory.Swap` → `LocalSwap`/`CmdSwap`, `DoClickAction` →
   `LocalDoClickAction`/`CmdDoClickAction`, `StoneTablet.Networkrotation`. 우리가 부르는 것은 이 셋뿐이다. 개발사가
   "동기화 구현이 최적화 중이라 바뀔 수 있다" 고 했으므로(LEGAL "받은 답변") 참가자 경로는 실기로도 다시 본다.
3. 아티팩트 번호가 밀리지 않았는지(양쪽에 있는 것의 식별자가 같은지).
4. 새 아티팩트·능력치 종류가 환산율을 받는지(`DataTool --values`), 새 조건 토큰이 없는지(모르는 조건은 `None` 으로
   읽혀 조용히 통과한다).
5. **배포된 DLL 도 새 어셈블리에 대 본다.** 소스가 빌드된다고 깔린 판이 도는 것은 아니다 - 선택 인자 추가나 필드↔속성
   변경은 소스로는 빌드되지만 옛 DLL 은 런타임에 `MissingMethodException` 을 낸다.
   - 마지막 릴리스 zip 의 `SephPlanner.Plugin.dll` 이 참조하는 게임 타입·멤버를 Mono.Cecil 로 새 `Managed` 에서
     `Resolve()` 해 깨진 것을 고른다(1.1.0 때 이렇게 했다). 두 판을 함께 지원하는 동안은 두 설치본으로 빌드한 DLL 을 두 판
     `Managed` 에 모두 대 본다.
   - 1.1.x `Managed` 의 Lib.Harmony 2.4.3 에는 BepInEx 판이 쓰는 `Harmony.Patch`(인자 여섯)가 없어 걸리지만 깨진 것이
     아니다. 실행 때는 BepInEx 의 HarmonyX 를 쓴다 - BepInEx 가 깔린 1.1.1 의 `Player.log` 는 `동봉 Harmony 2.9.0.0`
     (HarmonyX)을 찍는다(2026-11-02). 창작마당 판은 게임의 2.4.3 으로 짓고, 두 Harmony 에 다 있는 멤버만 쓴다([게임 내부
     조사](RESEARCH.md)의 "공식 모드 API").
   - `GameBinding`·진단·Harmony 대상처럼 문자열로 잡는 멤버는 이 검사에 안 걸리므로 디컴파일에서 이름을 따로 찾는다.

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

- **접힘.** 플레이 중에는 화면을 거의 가리지 않는다. **정말 한 줄만 남는다** - 현재 점수와 탐색한 배치 점수, 그리고
  지금 옮길 것 하나뿐이고 폭도 제한된다. 단축키 안내는 화면이 처음 뜰 때와 키를 누른 뒤 잠깐만 보인다. 게임 화면을
  가리지 않는 것이 접는 이유이므로 접힌 상태에 무엇을 더 남기지 않는다.
- **펼침.** 상자나 상점이 열리거나 세피라이트를 여는 순간 저절로 펼쳐지고, 닫히면 되돌아간다.
  격자에 칸마다 놓일 아이템과 레벨이 나오고, 무엇을 어디로 옮길지와 지금 집을 수 있는 후보가
  줄 세워진다. 직접 접거나 펼친 상태는 상자·상점이 다시 열리거나 닫힐 때까지 유지된다.

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
어긋난다. 잰 값은 [RESEARCH.md](RESEARCH.md) 의 "게임 설정 창에 탭으로 붙이는 길은 접었다".

## 번역

화면 문구는 한국어로 쓰고 `Tr("…")` 로 감싼다. 한국어 원문이 곧 열쇠이고, 영어·일본어·간체·번체는
`src/SephPlanner.Core/Text/Strings.영역.cs` 의 `Entry` 한 줄에 함께 둔다. 표에 없으면 영어, 그것도 없으면 원문이
나온다. `LocalizationTests` 가 소스의 `Tr("…")`·`Phrase("…")` 을 긁어 네 언어가 다 있는지, 안 쓰는 줄이 없는지, `{0}` 자리가
언어마다 같은지 본다. `Tr`·`Phrase` 의 첫 인자는 문자열 상수여야 한다.

- 한국어 조사는 `{0|을/를}` 로 적는다. 받침 있는 말 뒤에 앞엣것이 붙는다. 번역문은 `{0}` 만 쓴다.
- 로그·진단·`[perf]`·재현 사실·설정 파일 설명은 번역하지 않는다. 읽는 사람이 개발자다.
- 화면에도 로그·진단에도 가는 문장은 `Tr` 대신 `new Phrase("…")` 로 짓는다. 원문과 인자를 들고 있다가 화면에는
  `ToString()`(지금 언어), 로그·진단에는 `.Korean` 으로 쓴다. 인자로 든 문장은 같은 언어로 함께 옮긴다. 시뮬레이션
  불일치 사유, 자동 배치 결과와 그 진단 오류, 진단 전송 결과가 그렇다.
- 예외 메시지는 로그에 남으므로 한국어다. 화면에도 보일 예외는 `new Phrase("…").As(message => new …Exception(message))`
  로 던지고 화면에서는 `Phrase.Of(ex)` 로 꺼낸다. 예외의 형식은 그대로라 잡는 쪽과 시험이 형식으로 가르는 것은 바뀌지 않는다.
- 플러그인 클래스의 필드에는 번역 형식(`Phrase`, `Language`)을 두지 않는다. 필드 형식은 판 대조보다 먼저 클래스를 올릴
  때 풀려, 판이 섞였을 때 플러그인이 아예 뜨지 않는다(ROADMAP "판이 섞이면").
- 화면 언어는 F3 의 `언어` 가 정한다. 기본은 게임 언어를 따르고(`LocalizationManager.OnLanguageChanged`), 번역하지 않은
  게임 언어는 영어로 보인다. 바뀌면 HUD 와 닫힌 창을 버리고 다시 짓는다. 계획은 설명 문장까지 지어 두므로 언어가 계획 지문에
  들어가 다시 풀린다(한국어일 때는 줄이 없어 옛 재현 자료가 그대로 맞는다).
- 용어는 게임의 번역을 따른다. `Sephiria_Data/StreamingAssets/Localization/<언어>.json` 에서 같은 열쇠를 찾는다. 짧은
  용어만 옮기고 게임 문장은 가져오지 않는다.
- 아이템·석판·콤보 이름은 게임이 지금 언어로 주는 값을 그대로 쓴다(`names["current"]`). 우리가 번역하지 않는다.

## 원칙

- **쓰기는 자동 배치 하나뿐이다.** 그 밖에는 게임 상태를 읽기만 한다. 멀티에서는 기본으로 잠기고, 설정에서 켜면 방을 연
  쪽과 참가한 쪽 모두에서 동작한다. 싱글 자동 배치는 개발사에게 허락받았다. 근거는 [LEGAL.md](LEGAL.md).
- **게임 저작물을 배포하지 않는다.** 데이터는 각자 PC의 게임 설치본에서 생성한다.
- **비영리.** 운영 방침이지 라이선스 조건은 아니다. [LICENSE](../LICENSE) 는 MIT 다.
