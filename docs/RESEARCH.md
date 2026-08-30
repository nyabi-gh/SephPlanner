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

### 아직 반영하지 않은 것

- **각인**(`GridInventory.engravings`)은 읽지 않는다. `SyncList<StoneTablet>`이라 클라이언트에서
  볼 수는 있지만, 석판과 달리 옮길 수 있는 물건이 아니어서 지금의 배치 모델에 그대로 넣을 수 없다.
  "고정되어 움직이지 않는 석판"이라는 개념이 필요하다.
- **고정 각인**(`fixedEngravingsOnServer`)은 이름 그대로 서버에만 있어 클라이언트로 접속한
  세션에서는 읽을 수 없다.
- **배치 보너스**는 곱셈 뒤에 더해져서 `(석판 + 인챈트) × 배수`라는 우리 모델에 자리가 없다.

셋 다 있는 상황에서는 우리가 계산한 레벨이 게임이 보고한 레벨과 어긋난다. 플러그인의
`SimulationVerifier`가 그 차이를 잡아 BepInEx 로그에 남긴다.

## 배치 최적화

문제를 두 조각으로 나눈다.

**석판 배치**는 조합 탐색이다. 석판을 하나씩 놓으며 빔 서치로 후보를 좁힌다.

**아티팩트 배치**는 석판이 정해지면 칸마다 레벨이 확정되므로 배정 문제가 된다.
헝가리안 알고리즘으로 항상 최적해를 얻는다. 아티팩트 수가 빈 칸보다 많으면 행렬을 뒤집어
풀어서 어느 아티팩트를 빼는 것이 최선인지까지 함께 결정한다.

석판 조건과 아티팩트 조건이 서로의 배치에 의존하므로 몇 번 되풀이해 수렴시키고,
**최종 점수는 수렴한 배치로 다시 계산한다.** 그래서 보고되는 점수는 추정치가 아니라 실제 값이다.

## 선택지 감지

집거나 살 수 있는 아이템을 찾는 데 화면별 UI 클래스를 다룰 필요가 없다.
상자(`ItemChest.inventory`), 바닥에 떨어진 꾸러미(`DroppedInventory.Inventory`), 상점이
**모두 각자의 `GridInventory`를 들고 있기 때문이다.**

그래서 플러그인은 씬에 있는 `GridInventory` 중 플레이어의 것이 아니고 일정 거리 안에 있는 것을
훑는다. 새로운 종류의 상자나 상점이 추가되어도 같은 구조를 따르는 한 그대로 잡힌다.

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

## 제안이 흔들리지 않게 하는 것

석판 배치 탐색은 빔 서치라, 후보를 살펴본 순서와 어디까지 남겼는지에 따라 **점수가 같은 다른 배치**를
내놓는다. 그대로 두면 아무것도 달라지지 않았는데도 제안이 계속 바뀐다. 세 가지로 막는다.

1. 스냅샷에서 받은 석판과 아이템의 순서를 식별자 기준으로 한 번 고정한다. 게임이 넘겨주는 순서는
   물건을 옮기면 달라진다.
2. 현재 배치를 탐색 결과와 별개로 후보 목록에 넣는다. 빔이 현재 배치를 떨어뜨리면 이미 최적인
   배치를 두고도 옮기라고 하게 된다.
3. 점수가 같을 때 지금 자리에 그대로 있는 것마다 아주 작은 값을 더한다. 실제 점수 차이를 뒤집지
   못할 크기다.

## 레벨 상한

아티팩트마다 `maxLevel`이 다르고, 그 위로 올라간 레벨은 효과에 반영되지 않는다. 실제 분포는
2에서 4가 대부분이다. 점수 계산은 `min(maxLevel, 레벨)`을 쓰므로 상한을 넘겨도 이득이 없지만,
넘긴다고 손해도 아니어서 탐색이 굳이 피하지도 않는다.

그래서 두 가지를 둔다. 오버레이는 칸의 레벨이 아니라 **그 아티팩트가 실제로 받는 레벨**을 보여주고,
남는 레벨이 있으면 색과 도움말로 알린다. 그리고 점수가 같은 배치 중에서는 덜 흘리는 쪽을 고르도록
아주 작은 차이를 준다.
