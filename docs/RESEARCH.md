# 게임 내부 구조 조사

세피리아 1.0(App 2436940) 기준. 게임 업데이트로 내부 구조가 바뀔 수 있으니 플러그인이 깨지면
여기부터 다시 확인한다.

## 런타임

- **Unity Mono 빌드** (`MonoBleedingEdge/`, `Sephiria_Data/Managed/Assembly-CSharp.dll`).
  IL2CPP가 아니라서 BepInEx 5 + HarmonyX로 타입 참조가 그대로 된다.
- 네트워킹은 **Mirror**, 스팀 연동은 Heathen Steamworks, 직렬화용 **Newtonsoft.Json이 이미 게임에 포함**돼
  있어 플러그인이 따로 들고 갈 필요가 없다.
- `Assembly-CSharp.dll`에 타입 약 4,000개.

## 핵심 타입

### `GridInventory`

인벤토리 격자 전체를 들고 있는 클래스. 우리가 필요한 것의 대부분이 여기 있다.

```
public const int MaxWidth  = 6;
public const int MaxHeight = 7;
public byte Width = 6;

public readonly SyncDictionary<ItemPosition, int>                levelMatrix;
public readonly SyncDictionary<ItemPosition, int>                maxLevelMatrix;
public readonly SyncDictionary<ItemPosition, int>                disableMatrix;
public readonly SyncDictionary<ItemPosition, int>                ignoreCriteriaMatrix;
public readonly SyncDictionary<ItemPosition, NewItemOwnInstance> inventoryMatrix;
public readonly SyncDictionary<ItemPosition, Charm_Basic>        charms;
public readonly SyncDictionary<ItemPosition, StoneTablet>        stoneTablets;
```

**격자가 최대 6x7 = 42칸**이라는 점이 중요하다. 탐색 공간이 작아서 배치 최적화를 근사가 아니라
완전 탐색으로 풀 수 있다.

`levelMatrix`가 Mirror `SyncDictionary`라는 것도 중요하다. 게임이 이미 계산해 둔 최종 레벨이
클라이언트에 동기화돼 있으므로, **현재 상태를 읽을 때는 레벨 계산을 재구현할 필요가 없다.**
재구현이 필요한 것은 "이 아이템을 저기 놓으면 어떻게 되는가"라는 가상 배치를 평가할 때뿐이고,
그때도 `levelMatrix`를 정답지로 삼아 우리 시뮬레이터를 검증할 수 있다.

### `StoneTablet`

석판의 효과 모델이 완전히 드러나 있다. 셀별로 두 종류의 데이터가 붙는다.

**효과 (`AdditionEffectData`)** — 원문 문자열을 파싱해 결정된다:

| 원문 값 | `EffectType` | 의미 |
|---|---|---|
| 정수 (예: `2`, `-1`) | `IncreaseConstLevel` | 그 칸 아이템의 고정 레벨을 가감 |
| `X` | `Disable` | 그 칸을 사용 불가로 |
| `IGNORECRITERIA` | `IgnoreCriteria` | 그 칸 아이템의 배치 조건 무시 |
| `MUL/n` | `MultiplyConstLevel` | 그 칸 아이템의 레벨에 배수 |

**조건 (`AdditionCriteriaData`)**:

| 원문 값 | `CriteriaType` | 의미 |
|---|---|---|
| `ITEM` | `AnyItem` | 아무 아이템이나 있어야 함 |
| `CHARM` | `OnlyCharm` | 아티팩트만 허용 |
| `PLACED` | `Placed` | 배치된 상태여야 함 |

두 데이터 모두 위치 지정에 다음 플래그를 함께 갖는다:

- `isXWorldPosition` / `isYWorldPosition` — 해당 축을 석판 기준 상대 좌표가 아니라
  **인벤토리 절대 좌표**로 해석한다.
- `borderTop` / `borderRight` / `borderBottom` / `borderLeft` — 인벤토리 가장자리에 붙어야 한다는 제약.
  나무위키에서 말하는 "레벨 감소 칸을 가방 바깥으로 밀어내는" 테크닉이 이 플래그와 직결된다.

그 밖의 필드: `isRotatable`, `rotation`, `xIdx`, `yIdx`, `entityID`, `instanceID`, `IsApplied`.

### `NewItemOwnInstance` / `ItemEntity`

```
NewItemOwnInstance: InstanceID, Quantity, XIdx, YIdx, EntityID, Charm, StoneTablet
ItemEntity:         id, aName, aFlavorText, categories, cost, rarity, icon, type, ...
```

여러 칸을 차지하는 아이템은 `inventoryMatrix`의 여러 칸에 같은 인스턴스가 들어 있으므로
`InstanceID`로 중복을 제거해야 한다.

### 로컬 플레이어 접근 경로

```
Mirror.NetworkClient.localPlayer -> GetComponent<PlayerAvatar>()
                                 -> UnitAvatar.Inventory  (public GridInventory)
```

초기화 중에는 `localPlayer`가 비어 있을 수 있어 `FindObjectsOfType<PlayerAvatar>()` +
`isLocalPlayer` 폴백을 둔다.

## 텍스트 데이터

`Sephiria_Data/StreamingAssets/Localization/*.json`이 **평문 JSON**이고 15개 언어가 모두 들어 있다.
평평한 key-value 구조이며 총 6,590개 키.

- 석판: `Item_StoneTablet_{Id}_Name`
- 아티팩트: `Charm_{Id}_Effect`, `Charm_{Id}_Effect2`, `Item_{Id}_Name`, `Item_{Id}_FlavorText`

`SephPlanner.DataTool`이 여기서 **석판 68종, 아티팩트 257종**을 뽑는다. 게임 코드에서 아티팩트는
`Charm`으로 불린다.

주의: `Item_{Id}_Name` 만으로 아티팩트를 거르면 소모품까지 딸려온다. `Charm_` 접두 키로 id 집합을
먼저 확정한 뒤 교차하는 방식을 쓴다.

## 조사에 쓴 도구

```
dotnet tool install --global ilspycmd
ilspycmd -t GridInventory "<게임경로>/Sephiria_Data/Managed/Assembly-CSharp.dll"
```

## 석판 질의 DSL

석판의 효과 범위는 프리팹의 `query`(효과)와 `conditionQuery`(조건) 문자열이 결정하고,
`StoneTablet.ParseQuery`가 이를 칸 목록으로 푼다. 한 줄이 규칙 하나이며 형식은
`<토큰> <값>`, `IDX`/`RIDX`만 `<토큰> <인덱스> <값>`이다.

토큰은 47종이고 두 부류로 나뉜다.

### 오프셋 토큰 (28종)

`LEFT`, `LEFTLEFT` … `DOWNDOWNDOWNDOWN`, 대각선 `DIA*` 4종, 나이트 이동 `KNIGHT*` 8종.
석판 위치에서 고정된 만큼 떨어진 칸 하나를 가리킨다.

디컴파일된 소스에서 28종 × 4회전 = 112개 오프셋을 기계 추출해 대조한 결과,
**전부 회전 1스텝마다 `(x, y) -> (y, -x)`** 규칙에 일치했다. 따라서 회전 0의 기본 오프셋 하나만
표에 두고 회전은 계산으로 처리한다.

### 격자 토큰 (19종)

`O`, `IDX`, `RIDX`, `HORIZONTAL`, `VERTICAL`, `X_PLUS`/`X_MINUS`/`Y_PLUS`/`Y_MINUS`,
`TOP`/`BOTTOM`/`LEFTEND`/`RIGHTEND`, 대각 광선 `RIGHT_RISING`/`RIGHT_FALLING`/`LEFT_RISING`/`LEFT_FALLING`,
`CHECKERBOARD`, `CHECKERBOARD2`.

여러 칸을 한 번에 지정하고 테두리 플래그와 월드 좌표 플래그를 붙인다. 이 부류는 오프셋이 아니라
**토큰 이름 자체가 회전한다.** 게임의 `GetRotatedQuery`가 그 매핑을 갖고 있는데, 매핑대로 치환한
결과가 실제 구현과 같은지 12개 토큰 × 4회전 전부 대조해 일치를 확인했다.
`HORIZONTAL`/`VERTICAL`은 회전의 홀짝만 따진다.

### 주의할 점

- `Y_PLUS`는 `originPos.y`부터 순회해 **자기 칸을 포함한다.** `X_PLUS`가 `originPos.x + 1`부터
  시작하는 것과 비대칭이지만 게임 구현이 그렇다.
- `BOTTOM`, `RIGHTEND`, `CHECKERBOARD`는 `storage`(열려 있는 칸 수)에 의존한다. 가방이 커지면
  결과가 달라지므로 스냅샷에 `Storage`를 실어 보낸다.
- 대각 광선은 진행 방향 쪽 테두리 플래그만 세운다.

### 포팅 검증

`SephPlanner.Core`의 `TabletQuery`가 이 DSL의 포팅이다. 오버레이는 게임 없이 돌아야 하므로
포팅이 불가피하고, 한 칸이라도 어긋나면 솔버 전체가 틀어진다.

`SephPlanner.Plugin`의 `QueryVerifier`가 게임 안에서 원본 `StoneTablet.ParseQuery`와 전수 대조한다.
석판 68종 × 질의 2종 × 회전 4 × storage 7단계 × 모든 원점 조합을 돌려 위치·값·플래그를 비교하고,
결과를 `%LOCALAPPDATA%\SephPlanner\query-verification.txt`에 남긴다.

## 아티팩트 레벨과 활성 조건

`Charm_Basic.RefreshCharm`이 판정 전부를 담고 있다.

- 아티팩트의 레벨은 곧 `levelMatrix`의 자기 칸 값이다 (`DisplayedLevel`). 기본값은 0이고
  석판 효과와 인챈트가 더해진 뒤 배수 행렬이 곱해진다.
- 효과에 실제로 반영되는 레벨은 `min(maxLevel, 레벨)`이다. 상한을 넘겨 올리는 것은 낭비다.
- 다음 중 하나라도 걸리면 효과가 꺼진다.
  - `disableMatrix > 0`
  - **레벨이 0 미만** (0은 살아 있다)
  - 자체 조건 불만족. 단 `ignoreCriteriaMatrix > 0`이면 조건을 건너뛴다
  - 무기 연동 아티팩트인데 해당 무기를 들고 있지 않음

자체 조건은 `CharmActivateCriteria` 파생 10종이다. `TopInInventory`, `BottomInInventory`,
`SideEnd`, `Inside`, `Outlined`는 위치만 보고, `BothSideCharm`, `BothSidesAreEmpty`,
`NeighborsAreFull`, `Near8MagicBook`은 이웃 칸의 내용을 본다. `FullHP`만 배치와 무관한
전투 중 상태라 배치 탐색에서는 만족한 것으로 둔다.

## 배치 최적화

문제를 두 조각으로 나눈다.

**석판 배치**는 조합 탐색이다. 석판을 하나씩 놓으며 빔 서치로 후보를 좁힌다.

**아티팩트 배치**는 석판이 정해지면 칸마다 레벨이 확정되므로 배정 문제가 된다.
헝가리안 알고리즘으로 항상 최적해를 얻는다. 아티팩트 수가 빈 칸보다 많으면 행렬을 뒤집어
풀어서 어느 아티팩트를 빼는 것이 최선인지까지 함께 결정한다.

석판 조건과 아티팩트 조건이 서로의 배치에 의존하므로 몇 번 되풀이해 수렴시키고,
**최종 점수는 수렴한 배치로 다시 계산한다.** 그래서 보고되는 점수는 추정치가 아니라 실제 값이다.
