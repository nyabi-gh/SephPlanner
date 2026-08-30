# SephPlanner

세피리아(Sephiria)용 비공식 컴패니언 오버레이. 현재 인벤토리를 읽어 석판 배치를 최적화하고,
지금 상황에서 어떤 아티팩트를 고르는 게 좋은지 알려준다.

TEAM HORAY와 무관한 팬 제작 도구이며, 비영리로 배포한다.

## 구조

```
SephPlanner.Core      도메인 모델 + IPC 계약 + 솔버와 배치 계산 (netstandard2.1)
SephPlanner.Plugin    BepInEx 플러그인. 게임 상태를 읽어 명명 파이프로 내보낸다 (읽기 전용)
SephPlanner.Overlay   WPF 오버레이. 파이프에 붙어 화면에 표시한다 (net10.0-windows)
SephPlanner.DataTool  게임 로컬라이제이션에서 이름/설명 텍스트를 추출하는 CLI
SephPlanner.Tests     Core 단위 테스트. 게임 없이 돈다
```

게임 ↔ 오버레이 통신에 localhost HTTP 대신 **명명 파이프**를 쓴다. 방화벽 팝업이 뜨지 않고,
포트가 충돌하지 않으며, 같은 사용자 세션 밖에서는 열 수 없다.

## 빌드

게임이 설치돼 있어야 한다. 기본 경로가 아니면 환경변수로 알려준다.

```powershell
$env:SEPHIRIA_DIR = "D:\SteamLibrary\steamapps\common\Sephiria"
dotnet build
```

테스트는 게임 없이 돈다.

```powershell
dotnet test
```

저장해 둔 스냅샷으로 우리 레벨 계산이 게임 값과 맞는지 볼 수 있다. 어긋나는 칸이 있으면 아직
읽지 않는 효과(각인, 세트 효과, 배치 보너스)가 걸려 있다는 뜻이다.

```powershell
dotnet run --project src/SephPlanner.DataTool -- --check <스냅샷.json>
```

데이터 추출:

```powershell
dotnet run --project src/SephPlanner.DataTool
# -> data/generated/text.json  (석판 68종, 아티팩트 257종, 15개 언어)
```

`data/generated/`는 각자 PC에서 생성되며 저장소에 커밋하지 않는다. 이유는 [docs/LEGAL.md](docs/LEGAL.md) 참고.

## 문서

- [docs/RESEARCH.md](docs/RESEARCH.md) — 게임 내부 구조 조사 결과
- [docs/LEGAL.md](docs/LEGAL.md) — 약관·저작권 검토와 그에 따른 설계 제약

## 원칙

- **읽기 전용.** 게임에 값을 쓰지 않는다.
- **게임 저작물을 배포하지 않는다.** 데이터는 각자 PC의 게임 설치본에서 생성한다.
- **비영리.**

## 플러그인 설치

1. [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases) (win_x64, Mono)를 게임 폴더에 설치하고 한 번 실행한다.
2. `SephPlanner.Plugin.dll`과 `SephPlanner.Core.dll`을 `BepInEx/plugins/`에 넣는다.
3. 게임을 실행하면 석판/아티팩트 데이터가 `%LOCALAPPDATA%\SephPlanner\`에 덤프된다. F9로 다시 덤프할 수 있다.

인식이 이상한 아이템이 있으면 게임 안에서 F10을 누른다. 인벤토리 내용과 주변 선택지의 상태가
그대로 `%LOCALAPPDATA%\SephPlanner\inventory-dump.txt`에 기록된다.

단축키는 게임 창에 포커스가 있어야 먹는다. 그래서 **왜 어떤 선택지가 추천에 안 들어왔는지는
BepInEx 로그에 저절로 남는다.** 상태가 바뀔 때만 한 줄씩이다.

```
세피라이트 [NORMAL d=1818.5 gen=True acq=False n=5 active=True] -> 후보 5개
```

같은 폴더의 `query-verification.txt`에 질의 파서 검증 결과가 남는다. 불일치가 0이 아니면
솔버 결과를 믿을 수 없으므로 먼저 확인한다.

## 오버레이 실행

```powershell
dotnet run --project src/SephPlanner.Overlay -c Release
```

게임이 실행 중이면 자동으로 연결된다. 창은 끌어서 옮기고 Esc 또는 오른쪽 위 X로 닫는다.

오버레이는 **접힌 상태와 펼친 상태** 두 가지로 쓴다. 읽는 방식이 달라서 나눴다.

- **접힘.** 플레이 중에는 화면을 거의 가리지 않는다. 현재 점수와 최적 배치 점수, 그리고 지금
  옮길 것 하나만 보여준다.
- **펼침.** 상자나 상점이 열리거나 세피라이트를 여는 순간 저절로 펼쳐지고, 닫히면 되돌아간다.
  격자에 칸마다 놓일 아이템 이름과 레벨이 나오고, 무엇을 어디로 옮길지와 지금 집을 수 있는
  후보가 줄 세워진다.

**이동 목록은 위에서부터 그대로 따라 하면 된다.** 두 물건이 자리를 맞바꿔야 하면 목표 칸이 이미
차 있어 그냥은 옮길 수 없으므로, 한쪽을 빈 칸으로 "잠시 비켜두기" 하는 걸음을 끼워 넣는다.
하나 옮길 때마다 목록이 다시 계산되어 남은 걸음만 줄어든다.

**후보에는 점수 증가분과 함께 그 석판이 미치는 범위를 적는다.** `6칸 +6` 처럼 읽으면 된다.
아티팩트가 적을 때는 서로 다른 석판이 똑같은 증가분으로 보이는데, 그때 무엇이 다른지는 범위가
알려준다. 증가분이 같으면 여력이 큰 쪽을 위로 올린다. 살 수 없는 것은 지우지 않고 아래로 내린다.

`Ctrl+Alt+P`로 직접 접고 펼 수 있다. 게임에 포커스가 있어도 동작한다. 직접 접거나 펼친 상태는
상자·상점이 다시 열리거나 닫힐 때까지 유지된다.

옮겨야 하는 칸은 노란 테두리로 강조된다. 효과가 꺼진 아티팩트는 `꺼짐`으로 표시하고, 칸에
마우스를 올리면 왜 꺼졌는지(무기 불일치, 석판이 막은 칸, 음수 레벨, 배치 조건) 알려준다.
