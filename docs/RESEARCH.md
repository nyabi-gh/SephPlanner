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

## 게임 상태 쓰기 경로 (자동 배치)

자동 배치가 쓰는 경로다. 원칙은 **게임이 스스로 쓰는 경로만 탄다**는 것이다.

### 자리 이동: `GridInventory.Swap`

```
public void Swap(sbyte xLeft, sbyte yLeft, sbyte xRight, sbyte yRight)
```

수동 드래그가 타는 그 경로다. 서버(=싱글 호스트)면 `LocalSwap`을 바로 부르고, 클라이언트면
`CmdSwap`(Mirror Command, `requiresAuthority: true`)으로 서버에 보낸다. `LocalSwap`은
`inventoryMatrix`/`charms`/`stoneTablets` 세 딕셔너리를 함께 갱신하고, 빈 칸과의 맞바꿈도
그대로 처리하므로 "이동"과 "맞바꿈"을 구분할 필요가 없다. 임시 저장(손에 들기)을 거치는
`CmdMoveToTempStorage`/`CmdTempStorageToInventory` 경로도 있지만, 중간에 끊기면 아이템이 손에
남으므로 쓰지 않는다.

목표 배치를 적용할 때는 대상마다 "지금 자리 ↔ 목표 자리"를 Swap 하면 된다. 맞바꿈이라 밀려난
물건의 자리를 따로 관리하면 대피 걸음 없이 어떤 순열이든 만들어진다. 단 **여러 칸을 차지하는
아이템은 한 칸짜리 Swap 으로 옮기면 망가지므로**, 하나라도 보이면 적용 전체를 중단한다.

### 회전: `Permission` 스코프 안에서 `Networkrotation`

배치된 석판의 회전을 바꾸는 전용 Cmd 는 없다. 게임 내장 자동 정리(아래)가 하는 방식을 그대로
따른다: `using (new GridInventory.Permission(inv))` 안에서 `StoneTablet.Networkrotation`(SyncVar)을
설정한다. `Permission`은 공개 중첩 클래스로, 생성 시 쓰기 권한을 얻고 Dispose 때
`ReleasePermission`이 레벨 행렬 전체를 다시 계산한다. `LocalSwap`도 내부에서 같은 스코프를
여므로, **우리 Permission 스코프 안에서 Swap 을 부르면 권한 중복으로 터진다.** 이동과 회전을
분리한 이유다.

회전은 인스턴스 단위로 잠길 수 있다(`DungeonManager.IsTabletRotatable(instanceID, isRotatable)`).
솔버는 카탈로그의 `IsRotatable`만 보므로 잠긴 인스턴스에 회전을 제안할 수 있고, 적용기는 그런
회전을 건너뛰고 로그에 남긴다. 스냅샷에 인스턴스별 회전 가능 여부를 실어 솔버에 알리는 것이
다음 과제다.

### 게임 내장 자동 정리

`GridInventory.RequestAutoArrangeInventoryForBestCharmLevels(maxIterations = 4, allowTabletRotation = true)`
가 이미 게임에 있다(클라이언트용 Cmd 포함). 회전 후보와 쌍별 맞바꿈을 그리디로 4회 반복하는
지역 탐색이라 우리 솔버(빔 서치 + 헝가리안)보다 약하고, 목적 함수도 다르다:

```
켜진 아티팩트의 유효 레벨 합 × 10000 + 켜진 수 × 1000 + 전체 레벨 합 × 10
+ 상한 초과분 − 꺼진 수 × 750 − 음수 레벨 합 × 250
```

우리가 이 함수를 부르지 않는 이유는 우리 배치를 적용하기 위해서다. 다만 이 함수의 존재는
"자동 배치"가 게임 설계에 이미 있는 동작이라는 근거가 된다. 전체 상태를 갈아끼우는 내부 구현
(`ApplyAutoArrangeState`)은 여러 칸 아이템 처리가 우리 모델과 달라 흉내 내지 않는다.

### 공식 모드 API

`Assembly-CSharp`에 `HorayModAPI` 정적 클래스가 있다. 개발사가 직접 넣은 모드 훅으로,
"The MOD System is still under development" 안내와 https://teamhoray.com/mod-api 링크,
데이터베이스 로드·세션 시작·`GridInventoryStartPermission`/`EndPermission` 등의 이벤트를 제공한다.
아직 초기 단계라 쓰지는 않지만, 게임이 모드를 공식적으로 상정하고 있다는 근거다.

### 멀티플레이는 검증 전 잠금

싱글은 호스트 모드라 위 경로가 전부 서버 로컬에서 끝난다. 멀티 클라이언트에서는 `CmdSwap` 등
Cmd 경유가 필요한데, 커뮤니티의 다른 자동배치 모드가 클라이언트 회전 미동작·호스트 인벤토리
오염을 겪은 전례가 있다. 동기화가 안전하다고 확인될 때까지 플러그인은 멀티 세션에서 적용을
거부한다(`docs/LEGAL.md`).

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

## 레벨이 정해지는 순서

`GridInventory.ReleasePermission`이 `levelMatrix`를 만드는 순서다. 순서가 중요한 이유는 배수가
중간에 한 번 걸리기 때문이다.

1. 아티팩트 칸마다 **인챈트**를 더한다. 값은 `DungeonManager`의 `globalItemStatTable`에
   `"{instanceID}/Enchant"` 키로 인스턴스마다 따로 들어 있다.
2. **고정 각인**(`fixedEngravingsOnServer`)의 고정 레벨·비활성·조건무시·배수를 더한다.
3. **석판과 각인**(`stoneTablets`, `engravings`)의 `ApplyEffect`.
4. **배수 행렬을 곱한다.** 여기까지 더해진 값 전체에 곱한다.
5. **배치 보너스**(`SearchArrangementBonusInInventory`). 곱셈 뒤에 붙는다.

인챈트는 그대로 읽어 스냅샷에 싣는다. `globalItemStatTable`이 `SyncDictionary`라 클라이언트에도
값이 있다. 예전에는 게임이 보고한 레벨에서 석판 몫을 빼서 역산했는데, 4단계의 배수가 걸린 칸에서는
나눗셈이 떨어지지 않아 근사값이 됐다.

### 각인

`GridInventory.engravings`(`SyncList<StoneTablet>`)는 **격자 위 고정된 자리에서 석판과 똑같이
효과를 내지만 칸은 차지하지 않는다.** `AddEngravingOnServer(entityID, instanceID, rotation, x, y)`가
위치와 회전을 정하고, `ApplyEffect`는 일반 석판과 같은 코드를 탄다(조건 질의 포함).

`inventoryMatrix`에는 들어가지 않으므로 **각인이 있는 칸에도 아이템을 놓을 수 있다.** 추가와
제거 API만 있고 이동 API가 없어 **플레이어가 옮길 수 없다.**

그래서 각인은 배치 탐색의 대상이 아니라 주어진 조건이다. `PlacementProblem.FixedTablets`에 담아
효과 계산에는 언제나 함께 넣되, 배치 후보에서 자리를 빼앗지는 않는다.

### 고정 각인은 호스트에서 읽는다

**고정 각인**(`fixedEngravingsOnServer`)은 서버에만 있는 `List<FixedEngraving>`이다. 각 원소는
질의가 아니라 **절대 좌표로 이미 풀린 효과 딕셔너리**(fixedLevel/fixedDisable/fixedIgnoreCriteria/
fixedMultiplyLevel)를 들고 있고, 배수는 석판과 같은 `multiplyLevelMatrix`에 쌓인다.

정체의 대표가 **신비(MYSTIC) 콤보**다. `ComboEffect_Mystic`이 임계값마다 석판 12002를 고정
각인으로 심는다. 커뮤니티의 다른 자동배치 모드가 "신비 콤보 ×2 미반영" 버그를 겪은 원인이
바로 이것이다.

싱글은 호스트 모드라 이 목록을 그대로 읽을 수 있다. `GameReader.ReadFixedEffects`가 칸 효과로
합쳐 스냅샷(`FixedEffects`)에 싣고, `TabletSimulator`가 행렬에 먼저 깔고 시작한다.
`SimulationVerifier`도 같은 기준으로 대조한다. **클라이언트로 접속한 세션에서만 여전히 못 읽고**,
그때는 예전처럼 레벨 불일치 경고로 드러난다.

### 아직 반영하지 않은 것

- **배치 보너스**(`SearchArrangementBonusInInventory`). 곱셈 뒤에 더해져서
  `(석판 + 인챈트) × 배수`라는 우리 모델에 자리가 없다.
- **이웃 의존 아티팩트.** 효과의 세기가 자기 레벨이 아니라 다른 칸의 내용에 달린 아티팩트들이다.
  디컴파일 전수 검색으로 `Inventory.FindItem`을 부르는 아티팩트 클래스를 세어 보니 12종쯤 된다:
  `Charm_NearLevelDamage`(조화의 수정 - 이웃 8칸 유효 레벨 합에 비례), `Charm_UpCharmDamage`,
  `Charm_AutoMagic`, `Charm_ReduceMPCost`, `Charm_RightSpellCooldownHelper`(북향의 금빛침),
  `Charm_NearMagicBullet`, `Charm_PlanetModule`, `Charm_WhitePaper`, `Charm_WoodenBox`,
  `Charm_MagicCoolDownBonusByTag`, `Charm_BoltMagicMultiShot`, `Charm_CompanionChaos`.
  각자 로직이 달라 일괄 모델이 없고, 레벨 행렬에는 영향이 없어 불일치 경고로도 안 잡힌다
  (전투 스탯으로만 새므로). 배치 점수가 이들의 자리 가치를 과소평가하는 문제이며, 당장은
  사용자가 강화 우선 지정(우클릭)으로 보정한다. 제대로 하려면 아티팩트별 가치 함수가 필요하다.

레벨에 관해서는 어긋나는지를 **항상 확인한다.** 게임이 계산해 둔 `levelMatrix`가 정답지다.

- 플러그인의 `SimulationVerifier`가 아티팩트 칸마다 대조해 어긋나면 BepInEx 로그에 남긴다.
  (석판의 `IsApplied`와 질의 해석 범위만 보던 것에서 최종 레벨까지 넓혔다)
- 오버레이는 어긋난 칸이 있을 때만 "점수가 실제와 다를 수 있다"고 알린다. 무엇이 원인인지는
  알 수 없으므로 추측해서 미리 경고하지 않는다.
- `dotnet run --project src/SephPlanner.DataTool -- --check <스냅샷.json>` 으로 게임 없이도 본다.

실제 런에서 한 번 재어 보니(1.0.30, 석판 6개·아이템 13개) 석판 적용은 6개 모두 맞고 칸별 레벨도
한 칸을 빼고 전부 일치했다. 그 한 칸은 인챈트를 싣기 전 플러그인이 만든 스냅샷이라 설명이 된다.
표본이 한 런이라 일반화할 수는 없지만, 적어도 각인과 세트 효과가 늘 걸려 있는 것은 아니다.

## 점수를 어떻게 매기는가

점수는 **켜져 있는 아티팩트마다 1점, 거기에 그 아티팩트가 실제로 받는 레벨을 더한 값**의 합이다.

레벨만 세면 안 된다. 게임에서 레벨 0인 아티팩트는 멀쩡히 작동하고 레벨은 효과의 세기를 더할
뿐이기 때문이다. 레벨만 세면 **꺼지는 칸(레벨 음수)과 레벨 0 칸이 똑같이 0점**이 되어, 아티팩트를
꺼진 채로 두고도 최적이라고 말하게 된다. 실제로 그런 화면이 나온 적이 있다.

아직 모든 아티팩트의 1점을 같게 친다. 레벨 3짜리 사소한 아티팩트가 레벨 1짜리 핵심 아티팩트보다
높게 나오는 문제가 남아 있다. 레어도나 카테고리로 가중치를 주는 것이 다음 과제다.

## 배치 최적화

문제를 두 조각으로 나눈다.

**석판 배치**는 조합 탐색이다. 석판을 하나씩 놓으며 빔 서치로 후보를 좁힌다.

**아티팩트 배치**는 석판이 정해지면 칸마다 레벨이 확정되므로 배정 문제가 된다.
헝가리안 알고리즘으로 항상 최적해를 얻는다. 아티팩트 수가 빈 칸보다 많으면 행렬을 뒤집어
풀어서 어느 아티팩트를 빼는 것이 최선인지까지 함께 결정한다.

석판 조건과 아티팩트 조건이 서로의 배치에 의존하므로 몇 번 되풀이해 수렴시키고,
**최종 점수는 수렴한 배치로 다시 계산한다.** 그래서 보고되는 점수는 추정치가 아니라 실제 값이다.

## 선택지 감지

두 갈래로 나뉜다. 하나로 될 줄 알았는데 아니었다.

### 인벤토리를 가진 것

상자(`ItemChest.inventory`), 바닥에 떨어진 꾸러미(`DroppedInventory.Inventory`), 상점은
**각자 `GridInventory`를 들고 있다.** 그래서 화면별 UI 클래스를 다룰 필요 없이, 씬에 있는
`GridInventory` 중 플레이어의 것이 아니고 일정 거리 안에 있는 것을 훑으면 된다.

### 세피라이트

**석판과 아티팩트는 대개 세피라이트(`Sephirite`)로 나오는데 여기에는 `GridInventory`가 없다.**
대신 `SyncList<SephiriteRewardMetadata> rewards`에 `{ instanceID, entityID }`를 담고 있고,
`isGenerated`가 참이 되면 채워진다. `isAcquired`면 이미 가져간 것이다. 전부 동기화되는 값이라
클라이언트에서 읽을 수 있다.

`Sephirite.Type`에 `TABLET`, `TABLET_BOSS`, `CHARM`이 있는 데서 보이듯 이쪽이 석판의 주 경로다.
`GridInventory`만 훑던 동안에는 석판 추천이 아예 되지 않았다.

**세피라이트에는 거리를 쓰지 않는다.** 좌표가 플레이어와 같은 기준이 아니어서, 바로 앞에 있는데도
거리가 1800이 넘게 나온다. 그래서 거리로 거르면 언제나 걸러진다. 애초에 거리는 "지금 닿을 수 있는가"의
근사일 뿐이고, 세피라이트는 **`isGenerated`가 참이라는 것 자체가 "플레이어가 열어서 지금 고르는 중"**
이라는 더 정확한 신호다. 비활성 오브젝트도 함께 찾는다. 여는 동안 본체가 꺼져 있을 수 있다.

이 문제는 플러그인이 스스로 남기는 로그로 잡았다. 상태가 바뀔 때만 한 줄씩 남는다.

```
세피라이트 [NORMAL d=1818.5 gen=True acq=False n=5 active=True] -> 후보 0개
```

단축키로 덤프를 받는 방식은 키 입력이 게임 창에 닿아야만 해서 정작 필요할 때 쓰지 못했다.
F9는 먹는데 F10은 먹지 않는 일이 반복됐다.

### 석판 제단에서는 미리 알 수 없다

`AltarOfTablet`의 선택지 세 개(`AltarOfTabletInteractable`)에는 어떤 석판인지에 대한 정보가 없다.
고르는 순간 서버가 `CmdSpawnReward`에서 세피라이트를 스폰하고 `Sephirite.Initialize(RandomID +
selectionSeedOffset)`으로 내용을 정한다. **고르기 전에 무엇이 나올지는 클라이언트가 알 수 없고,
이는 게임의 설계라 우회할 수 없다.** 추천은 세피라이트가 생긴 뒤부터 가능하다.

거리 기준은 BepInEx 설정의 `OfferRadius`로 조정한다.

## 특수 경로로 얻은 아이템

기적 보상으로 얻은 아이템이 오버레이에서 인식되지 않는다는 보고가 있었다. 실제 덤프로 확인한
결과 **재현되지 않았다.**

덤프에는 이름 키가 `Item_`이 아니라 `Skill_`로 시작하고(`Skill_0006`, `Skill_0020`) `activeType`이
`Locked`인, 일반 아이템 풀과 다른 경로로 들어온 아티팩트가 있었다. 이들은 전 구간에서 제대로
다뤄진다.

- `inventoryMatrix`에 `Charm_Basic`을 단 인스턴스로 그대로 들어 있다. `GameReader`는 석판만
  걸러내므로 스냅샷에 담긴다.
- 카탈로그에도 있다. `ItemCatalog`는 `Resources`를 직접 훑고 `activeType`이 `Disabled`인 것만
  제외한다. 엔티티 3002/3012가 `maxLevel`과 `categories`까지 갖춰 들어 있었다.
- 그래서 오버레이에서도 자리만 차지하는 아이템이 아니라 아티팩트로 채점된다.

`activeType`을 아이템의 인게임 제약으로 오해하기 쉬운데, 실제로는 **도감과 커스텀 로드아웃에
보일지**를 정하는 값이다. `Locked`은 아직 해금하지 않아 목록에 안 보인다는 뜻이며
(`playerSpawner.unlockedCharms`로 판정) 런 중의 이동이나 효과 계산에는 관여하지 않는다.
읽을 때 걸러야 하는 값은 `Disabled`와 `Hidden`뿐이다.

같은 증상이 다시 나오면 그 시점에 F10으로 받은 덤프가 있어야 한다. 덤프에는 칸별 좌표, 엔티티
번호, 아이템 종류, `activeType`, 이름 키, 컴포넌트 유무가 그대로 남는다.

## 콤보 (세트 효과)

화면 오른쪽 "콤보 효과" 패널(잉걸불 6/8 등)의 정체다. 자료 구조는 이렇게 이어진다.

- 아티팩트의 `ItemEntity.categories`가 카테고리 문자열 목록을 갖는다 (`EMBER`, `FLAMESWORD` 등).
  이미 카탈로그 덤프에 `Categories`로 들어 있다.
- 카테고리 정의는 `Resources.LoadAll<ItemCategoryEntity>("ItemCategory")`. `id`, 로컬라이즈된
  `categoryName`, 그리고 발동 효과가 있다. 효과는 두 세대가 공존한다: 신형은 `comboEffectPrefab`의
  `ComboEffectBase.addStatByCombo[]`(원소마다 `comboCount` 임계값), 구형은 `setStatus[]`
  (`itemCount` 임계값). 게임의 `SearchSetEffectInInventory`가 양쪽을 다 쓰므로 임계값은 둘을
  합쳐 모은다. `CatalogDump`가 `combos.json`으로 덤프한다.
- 개수 판정은 `GridInventory.SearchSetEffectInInventory`(서버). **배치 위치·레벨·활성 여부와
  무관하게 격자에 있는 아티팩트 전체로 카테고리를 센다.** 그래서 콤보는 배치 최적화가 아니라
  **후보 추천**에만 영향을 준다.
- 센 결과는 `currentSetEffectCount`(SyncDictionary)로 클라이언트에 동기화된다. 유니크 페어 변환
  (`allowUniquePairIncreaseCombo`)이나 하드모드 중복 금지(`OVERLAPITEMCOMBO`) 같은 보정이 서버
  계산에 섞여 있어, **우리가 다시 세지 않고 이 값을 스냅샷(`ComboCounts`)에 그대로 싣는다.**

추천 반영은 `OfferAdvisor`가 한다. 후보 아티팩트의 카테고리마다 "하나 더 모으면" 임계값에
닿는지 보고, 닿으면 레벨 2에 해당하는 보너스, 다가가기만 하면 소액을 줄 세우기에 더한다.
배치 점수(증가분)와는 섞지 않고 별도 열("잉걸불 7/8")로 보여준다. 가중치(2.0 / 0.25)는 실측
근거가 없는 설계값이므로, 추천이 이상하게 기울면 여기부터 의심한다.

## 무기 연동 아티팩트

`isWeaponRelatedCharm`이 참인 아티팩트는 `relatedWeapon`과 지금 든 무기의 종류가 같아야 효과가
켜진다. 판정은 `Charm_Basic.RefreshCharm`에 있고, 무기는 아바타의 `WeaponControllerSimple`이
들고 있다.

```
PlayerAvatar -> GetComponent<WeaponControllerSimple>() -> currentWeapon.weaponType  (EWeaponType)
```

`EWeaponType`은 `SwordAndShield`, `GreatSword`, `Dagger`, `Crossbow`, `StaffMagic`, `Katana`,
`Golem`, `Staff`, `Random` 9종이다. 카탈로그가 `relatedWeapon`을 이름 그대로 싣고, 스냅샷의
`RunState.WeaponId`가 장착 무기를 실어 보낸다.

판정할 근거가 없을 때는 켜져 있는 것으로 둔다. 무기를 모르거나(런 밖) 카탈로그가 연동 무기를
기록하기 전 버전이면, 비교했다가는 무기 연동 아티팩트를 전부 0점으로 보게 되기 때문이다.

## 가격과 소지금

상점 가격은 **원가가 아니다.** `ItemDatabase.GetItemBuyPrice`가 파는 쪽과 사는 쪽의
`ECustomStat.Negotiation` 차이로 원가를 조정한다. 차이가 0 이상이면 1배에서 3배까지,
음수면 1배에서 0.66배까지 선형으로 움직인다. 그래서 `ItemEntity.cost`를 그대로 쓰면 최대 3배까지
어긋난다.

값을 치러야 하는지 아닌지는 인벤토리의 주인으로 가른다. 상자(`ItemChest`)와 바닥에 떨어진
꾸러미(`DroppedInventory`)는 `GridInventory.UnitAvatar`가 없고, 상인은 있다. 주인이 없으면 값은 0이다.

소지금은 `UnitAvatar.Money`다.

## 옮기는 순서

목표 배치만 알려주면 그대로 따라 할 수 없다. 두 물건이 자리를 맞바꾸는 경우 목표 칸이 이미 차
있어서 첫 걸음부터 막힌다. 그래서 `MoveOrder`가 실행 가능한 순서로 세운다.

1. 목표 칸이 비어 있는 것부터 옮긴다. 하나 옮기면 그 자리가 비므로 연쇄적으로 풀린다.
2. 더 옮길 수 있는 것이 없는데 남아 있으면 서로 물고 물린 고리다. 하나를 빈 칸으로 대피시켜
   고리를 끊는다. 대피처는 다른 물건이 가야 할 자리를 피해서 고른다.
3. 대피할 칸조차 없으면(격자가 꽉 찬 경우) 남은 것을 그대로 알린다. 조용히 빠뜨리지 않는다.

## 제안이 흔들리지 않게 하는 것

석판 배치 탐색은 빔 서치라, 후보를 살펴본 순서와 어디까지 남겼는지에 따라 **점수가 같은 다른 배치**를
내놓는다. 그대로 두면 아무것도 달라지지 않았는데도 제안이 계속 바뀐다. 세 가지로 막는다.

1. 스냅샷에서 받은 석판과 아이템의 순서를 식별자 기준으로 한 번 고정한다. 게임이 넘겨주는 순서는
   물건을 옮기면 달라진다.
2. 현재 배치를 탐색 결과와 별개로 후보 목록에 넣는다. 빔이 현재 배치를 떨어뜨리면 이미 최적인
   배치를 두고도 옮기라고 하게 된다.
3. 점수가 같을 때 지금 자리에 그대로 있는 것마다 아주 작은 값을 더한다. 실제 점수 차이를 뒤집지
   못할 크기다.

세 번째는 **아티팩트를 칸에 배정하는 단계에서 더해야 한다.** 채점할 때만 더하면 헝가리안이 이미
자리를 정한 뒤라 늦다. 값이 같은 두 아티팩트를 배정기가 임의로 골라서, 이득이 0인데도 둘을
맞바꾸라는 제안이 나온다. 실제로 그런 화면이 나온 적이 있다 — 점수는 `4 → 4`로 같은데
"봉황의 날개깃 (3,3) → (2,3), 파이어 볼트 (2,3) → (3,3)"을 제안했다.

## 레벨 상한

아티팩트마다 `maxLevel`이 다르고, 그 위로 올라간 레벨은 효과에 반영되지 않는다. 실제 분포는
2에서 4가 대부분이다. 점수 계산은 `min(maxLevel, 레벨)`을 쓰므로 상한을 넘겨도 이득이 없지만,
넘긴다고 손해도 아니어서 탐색이 굳이 피하지도 않는다.

그래서 두 가지를 둔다. 오버레이는 칸의 레벨이 아니라 **그 아티팩트가 실제로 받는 레벨**을 보여주고,
남는 레벨이 있으면 색과 도움말로 알린다. 그리고 점수가 같은 배치 중에서는 덜 흘리는 쪽을 고르도록
아주 작은 차이를 준다.
