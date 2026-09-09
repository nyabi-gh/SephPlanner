# 게임 내부 구조 조사

현재 구현·추정·실기 미검증 범위는 [현재 상태](STATUS.md)에 모았다. 이 문서의 날짜별 측정과
조사 경위는 당시 기록이며, 최신 빌드의 성능이나 전체 효과 지원을 보장하지 않는다.

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

**격자가 최대 6x7 = 42칸**이어도 아이템과 회전의 조합은 많다. 현재는 제한된 빔 탐색과
배정·교환 개선을 사용하며 전체 배치의 완전 탐색이나 전역 최적을 보장하지 않는다.

`levelMatrix`가 Mirror `SyncDictionary`라는 것도 중요하다. 게임이 이미 계산해 둔 최종 레벨이
클라이언트에 동기화돼 있으므로, **현재 상태를 읽을 때는 레벨 계산을 재구현할 필요가 없다.**
재구현이 필요한 것은 "이 아이템을 저기 놓으면 어떻게 되는가"라는 가상 배치를 평가할 때뿐이고,
그때도 `levelMatrix`를 정답지로 삼아 우리 시뮬레이터를 검증할 수 있다.

### 포션 벨트와 보조 가방은 격자가 아니다

**포션칸은 같은 `inventoryMatrix` 를 쓰면서 격자 밖 좌표에 산다.** 자리는 `ItemPosition(x, 100)`
이고 x 는 `0 .. numberOfPotionStorage - 1` 이다(`HasEmptyPotionSlot`, `CanAddPotionStorage`).
`GridInventory.Swap` 도 이 줄을 알고 있어서, 포션이 아닌 것을 y>=100 으로 넣으려 하면 거부한다
(반대로 포션을 격자로 꺼내는 것은 막지 않는다).

그래서 딕셔너리를 그냥 훑으면 **포션이 격자 아이템으로 섞여 든다.** 배치 쪽은 원래부터 격자
밖 좌표를 걸러 냈지만 스냅샷의 아이템 목록에는 그대로 들어 있어 "가방에 아이템 몇 개" 같은
셈이 조용히 어긋날 자리였다. 지금은 `GameReader`가 읽을 때 걸러 내고, 진단이 필요할 때는
F10 덤프가 딕셔너리를 있는 그대로 보여준다(덤프 머리에 `Potion=`, `SubBag=` 개수도 함께 적힌다).

이 판정은 `GridSpec.Contains` 하나다. 한동안 같은 뜻의 함수가 네 벌(계획·적용 전 검사·적용기·
읽기) 있었고 그중 읽기 쪽만 아직 잠긴 칸(`CurrentInventoryStorage` 밖)을 보지 않았다. 넷이
갈리면 솔버와 자동 배치가 서로 다른 격자를 보게 되므로 한 벌로 모았다.

**보조 가방**(`numberOfSubBagStorage`)은 아예 다른 딕셔너리(`subBagMatrix`, 키가 `sbyte`)라
`inventoryMatrix` 에 나타나지 않는다. 격자와 오가는 길은 `ServerSwapSubBagAndInventory` 뿐이다.

포션칸도 보조 가방도 석판 효과가 닿지 않으므로 배치·점수에는 관여하지 않는다. 다만 **선택지
쪽에서는 한 번 걸린다** - 상점에 놓인 포션은 후보로 읽히는데, 아티팩트도 석판도 아니라
평가 대상에서 빠진다. 그 탈락을 후보 상한보다 먼저 처리하지 않으면 "선택지가 많아 몇 개는
평가하지 못했습니다"가 포션 때문에 뜬다.

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

### 석판 합성 (`TabletMix`)

상점 층에 놓이는 석판 합성기다. 게임 안 이름은 "석판 합성기"이고 한 번 쓰면 그 층에서는 다시
못 쓴다. `GridInventory.ServerMixTablet`이 실제 처리를 한다.

```
결과 = entityID 2101 짜리 새 아이템 (새 instanceID)
  질의      = GetRotatedQuery(A, 회전A) + 줄바꿈 + GetRotatedQuery(B, 회전B)
  조건질의  = 양쪽이 다 있을 때만 같아야 한다 (다르면 TABLET_MIX_FAILED / DIFFERENT_CONDITION_QUERY)
              한쪽이 비어 있으면 있는 쪽이 그대로 결과의 조건이 된다
  회전가능  = A와 B가 둘 다 회전 가능할 때만
  이름      = 플레이어가 직접 입력
  비용      = TabletMix.mixCost (기본 200골드). UI_TabletMixPanel 이 HasMoney/SubMoney 로 받는다
  제약      = 재료의 엔티티가 2101 이면 안 된다 - 합성 석판은 다시 합성하지 못한다
  사용 제한 = 사람마다 층에 한 번 (TabletMix.LocalUsed, guid 로 판정)
```

값은 셋 다 인스턴스 단위로 `DungeonManager`에 저장된다 — `customTabletQuery`,
`overrideTabletRotatable`, `overrideItemName`. 엔티티 2101 자체의 정의는 껍데기다:
질의는 비어 있고, `isRotatable`은 거짓이며, **이름은 문자열 `"..."` 하나다**(자리표시자).

여기서 따라오는 것들:

- 질의가 **여러 줄**이다. 게임 `ParseQuery`는 `
`으로 잘라 줄마다 처리하고 우리
  `TabletQuery.Parse`도 같으므로 그대로 통한다.
- 합성 시점의 회전이 **토큰 이름으로 미리 구워진다**(`GetRotatedKeyword`). 나오는 토큰은 전부
  기존 28+19종 안이라 새 토큰이 생기지 않는다.
- `DungeonManager.IsTabletRotatable(instanceID, 기본값)`은 오버라이드가 있으면 그것을 그대로
  돌려준다. 즉 **인스턴스 값이 정의 기본값을 이미 흡수한 최종 답이다.** 정의값을 다시 AND 하면
  합성 석판(정의상 거짓)은 영원히 회전 후보에서 빠진다 — 실제로 그 버그가 있었고
  `PlacementSolver`에서 정의값을 떼어 고쳤다. 아직 집지 않은 후보 석판은 인스턴스가 없으므로
  그쪽만 정의값을 쓴다(`OfferAdvisor`).
- 이름이 없으면 화면에서 합성 석판끼리 구분되지 않는다. 아이콘도 2101 하나를 공유한다.
  그래서 스냅샷에 `PlacedTablet.Name`을 실어 `DungeonManager.GetItemName`의 값을 그대로 쓴다.
  게임 화면에 이미 보이는 이름이라 숨은 정보에 해당하지 않는다.

`QueryVerifier`는 카탈로그 정의 68종을 도는데 2101의 질의가 비어 있어, **합성으로 생긴 인스턴스
질의는 게임 원본과 대조된 적이 없다.** 분할 코드가 같아 위험은 낮지만 검증 공백으로 남아 있다.

### 합성 추천

어느 둘을 합치면 좋은지는 `TabletMixAdvisor`가 답한다. 후보 추천과 같은 방식이다 - 합친 상태로
배치를 다시 풀어 점수 증가분을 본다. 재료 둘이 사라지고 칸이 하나 비는 것까지 증가분에 들어간다.

**회전이 답의 일부다.** 합성이 재료의 회전을 결과 질의에 구워 넣으므로 "무엇과 무엇을"만으로는
결과가 정해지지 않는다. 그래서 `TabletQuery.Rotated`가 게임의 `GetRotatedQuery`와 같은 일을 한다 -
오프셋 토큰은 좌표를 돌린 뒤 이름을 되찾고, 격자 토큰은 이름표를 갈아 끼운다. 오프셋 토큰 28종이
좌표와 일대일이라 회전한 좌표에 늘 대응 토큰이 있다(빠짐없이 확인했다).

읽을 때 돌리는 것(`Parse`의 rotation 인자)과 결과가 같아야 한다. **a 만큼 구운 질의를 b 로 읽은
것이 원본을 a+b 로 읽은 것과 같다**는 성질을 `TabletMixAdvisorTests`가 회전 16조합으로 고정한다.

탐색 범위는 작다. 쌍이 n(n-1)/2 이고 회전 조합은 넷을 넘지 않는다 - 둘 다 돌릴 수 있으면 결과도
돌릴 수 있어 절대 회전이 아니라 사이 각도만 결과를 가르므로, 한쪽을 고정해도 나올 모양은 다 나온다.

합성기는 거리를 보지 않고 읽는다. 미니맵에 뜨는 고정물(`minimapElementName`)이라 층에 있다는
사실 자체가 이미 보이는 정보이고, 무엇을 합칠지는 합성기 앞에 서기 전에 정해 두는 편이 쓸모
있기 때문이다. 상자 속 내용물과 달리 숨은 정보가 아니다.

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

수동 드래그가 타는 그 경로다 - `UI_CharacterStatusPanel` 이 집은 칸과 누른 칸을 그대로 `Swap` 에
넘기며 **목표 칸이 비었는지 보지 않는다**(로그도 "Icon Swapped"라고 찍는다). 서버(=싱글 호스트)면
`LocalSwap`을 바로 부르고, 클라이언트면
`CmdSwap`(Mirror Command, `requiresAuthority: true`)으로 서버에 보낸다. `LocalSwap`은
`inventoryMatrix`/`charms`/`stoneTablets` 세 딕셔너리를 함께 갱신하고, 빈 칸과의 맞바꿈도
그대로 처리하므로 "이동"과 "맞바꿈"을 구분할 필요가 없다. 임시 저장(손에 들기)을 거치는
`CmdMoveToTempStorage`/`CmdTempStorageToInventory` 경로도 있지만, 중간에 끊기면 아이템이 손에
남으므로 쓰지 않는다.

목표 배치를 적용할 때는 대상마다 "지금 자리 ↔ 목표 자리"를 Swap 하면 된다. 맞바꿈이라 밀려난
물건의 자리를 따로 관리하면 대피 걸음 없이 어떤 순열이든 만들어진다. 단 **여러 칸을 차지하는
아이템은 한 칸짜리 Swap 으로 옮기면 망가지므로**, 하나라도 보이면 적용 전체를 중단한다.

### 회전: 호스트는 `Networkrotation`, 참가자는 `DoClickAction`

**호스트에서는** 게임 내장 자동 정리(아래)가 하는 방식을 그대로 따른다:
`using (new GridInventory.Permission(inv))` 안에서 `StoneTablet.Networkrotation`(SyncVar)을
설정한다. `Permission`은 공개 중첩 클래스로, 생성 시 쓰기 권한을 얻고 Dispose 때
`ReleasePermission`이 레벨 행렬 전체를 다시 계산한다. `LocalSwap`도 내부에서 같은 스코프를
여므로, **우리 Permission 스코프 안에서 Swap 을 부르면 권한 중복으로 터진다.** 이동과 회전을
분리한 이유다.

**참가자로 접속한 세션에서는 `Networkrotation` 을 쓸 수 없다.** SyncVar 라 클라이언트에서 써 봐야
서버 값이 덮는다. 대신 우클릭이 타는 길이 있다:

```
GridInventory.DoClickAction(ItemPosition)             // 서버면 Local, 아니면 Cmd
  → [Command, requiresAuthority: true] CmdDoClickAction(ItemPosition)
  → [Server] LocalDoClickAction → using (new Permission(this)) { item.DoClickAction() }
  → NewItemOwnInstance.DoClickAction() → StoneTablet.Rotate()      // 한 번에 90도
```

`Rotate()`는 `rotation`을 1 올리고 4에서 0으로 돌린다(잠긴 석판이면 아무것도 하지 않는다). 각도를
직접 줄 수 없으므로 필요한 횟수만큼 누른다. 세는 것은 `TabletRotation.PressesFrom` 이고 Core 에
두고 시험한다 - **방향을 뒤집어 세면 누를 때마다 어긋난 채로 굳는다**(같은 일을 하는 커뮤니티
도구가 겪은 드리프트). 그 칸에 아티팩트가 있으면 `DoClickAction`은 아무것도 하지 않는다
(`Charm != null` 이면 회전으로 가지 않는다).

회전은 인스턴스 단위로 잠길 수 있다(`DungeonManager.IsTabletRotatable(instanceID, isRotatable)`).
스냅샷의 `PlacedTablet.IsRotatable`이 인스턴스별 잠금을 실어 보내고 솔버가 이를 존중한다
(잠긴 석판은 지금 각도 그대로만 쓴다). 적용기는 계산 이후 잠겼을 가능성에 대비해 이동 전과
회전 직전에 다시 확인하고, 그 사이 잠겼다면 이미 옮긴 항목을 되돌린 뒤 적용을 중단한다.

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

### 멀티플레이는 기본으로 잠그고, 켜면 호스트와 참가자 양쪽에서 돈다

싱글은 호스트 모드라 위 경로가 전부 서버 로컬에서 끝난다. 멀티 세션은 기본으로 적용을 거부하고,
설정(`MultiplayerAutoPlace`, 기본 꺼짐)으로만 연다. 판정은 `AutoPlacePolicy`(화면 안내와 F8)와
`PlanApplier`(쓰기 직전, 적용 도중 접속이 늘어난 경우까지) 두 겹이다.

**참가자로 접속한 세션에서도 돈다.** 게임이 클라이언트에게 열어 둔 길이 이동과 회전 모두 있다.

- **이동**은 `Swap` 이 서버가 아니면 `CmdSwap`(Mirror Command, `requiresAuthority: true`)으로 서버에
  보내고, 서버 핸들러(`UserCode_CmdSwap`)는 `LocalSwap` 을 그대로 부른다. 인벤토리는 내 플레이어
  오브젝트에 붙어 있어 권한 조건을 만족한다.
- **회전**은 `DoClickAction` → `CmdDoClickAction` 이다(위 "회전").
- 둘 다 신뢰 채널 0 으로 나가므로 **서버가 보낸 순서대로 처리한다.** 이동을 다 보낸 뒤 회전을
  보내면 서버도 그 순서로 본다.

다만 **왕복이라 부른 즉시 반영되지 않는다.** `Swap` 을 부른 프레임에는 아직 아무것도 바뀌지 않았고,
서버가 처리해 SyncDictionary 로 돌려줄 때까지 옛 상태가 보인다. 그래서 적용기는 코루틴이고
걸음마다 반영을 기다린다(3초 한계, 밀려난 쪽까지 제자리에 와야 한 걸음이 끝난 것으로 본다).
호스트에서는 첫 확인이 곧바로 참이라 예전처럼 한 프레임에 끝난다.

걸음 순서·저널·되돌리기·시간 상한은 Core 의 `ApplyPlanRoutine` 에 있고, 게임 타입은
`IInventoryPort`(칸 읽기, 맞바꾸기, 석판 찾기, 우클릭, 호스트 회전, 레벨 읽기) 뒤에 있어
`ApplyPlanRoutineTests` 가 가짜 격자로 돌린다. Plugin 의 `PlanApplier` 에 남은 것은 쓰기 전
사전 검증과 그 포트의 구현이다. 한 국면(앞으로 가기·되돌리기)의 30초 예산은 **걸음을 보내기
전에** 본다 - 이미 보낸 걸음의 3초 창을 중간에 자르면 확인이 안 된 채로 저널에 못 올라간 걸음이
뒤늦게 반영되어 되돌리기가 그것을 놓친다(가짜 격자 테스트가 잡았다). 그래서 한 국면은 길어야
30초에 걸음 하나의 창을 더한 만큼이다.

2026-09-05에는 3초보다 늦게 도착하는 명령도 가짜 포트로 재현했다. 제한 시간을 넘긴 것은
서버의 거부 응답이 아니므로, 미확정 쓰기가 있으면 보상 이동·회전을 보내지 않고 F8을 잠근다.
Plugin은 코루틴 중단에도 그 인벤토리 참조를 유지하며, 재접속으로 기존 인벤토리가 파괴돼야
잠금이 해제된다. 확정된 이동의 되돌리기는 두 칸의 인스턴스가 아직 예상과 같은 경우에만 실행한다.
각 회전은 정확히 한 단계가 관측돼야 하고, 완료 전 대상 전체의 위치·회전을 다시 검사한다.

**기다리지 않고 쏘기만 하면 무엇이 남는지**는 같은 일을 하는 커뮤니티 도구가 보여 준다. 그쪽은
확인을 하지 않아 `LocalSwap` 이 조용히 거부한 걸음까지 "이동 N건 완료"로 보고하고, 도중에 막히면
솔버가 평가한 적 없는 반쯤 배치가 남는다. 확인과 되돌리기가 우리가 붙이는 값이다.

**남은 사각지대 하나.** 고정 각인(`fixedEngravingsOnServer`)은 순수 `List` 라 참가자 세션에서는
읽을 수 없다(아래 "고정 각인은 호스트에서 읽는다"). 다만 `levelMatrix` 는 SyncDictionary 라
클라이언트에도 오고 `SimulationVerifier` 가 그것을 정답지로 대조하므로, **각인이 걸린 참가자
세션은 실시간 검증이 스스로 실패해 자동 배치가 잠긴다.** 모르는 채로 어긋난 계획을 적용하는 일은
없다.

한때 이 토글을 두었다가 개발사 답변을 "잠가 두라"로 읽고 2026-09-03에 뺐고, 같은 날 그 답변이
금지가 아니라 안전상의 권고임을 다시 읽고 되살렸다(`docs/LEGAL.md` "받은 답변").

**정정(2026-09-04).** 그때 이 절에 "회전은 길 자체가 없다"고 적었는데 **틀렸다.** `DoClickAction` 을
놓쳤다. 커뮤니티 모드의 "클라이언트 회전 미동작"을 게임에 길이 없다는 증거로 읽은 것이 원인이고,
실제로는 그 모드가 아직 그 경로를 쓰지 않던 판이었다. 게임 어셈블리에서 확인하고 참가자 세션까지
열었다.

## 아티팩트 효과 설명

로컬라이제이션의 `Charm_{Id}_Effect` / `_Effect2`는 **완성된 문장이 아니다.** 자리표시자와 키워드
태그가 남아 있다 — 예: `블록 성공 시 {BASIC_ATTACK_DAMAGE} 물리 피해 {TIME}초`,
`<tag=CATEGORY:STURDY>`.

자리를 채우는 것은 `Charm_Basic.BuildEffectString(avatar, bullet, endl, level, virtualLevelOffset,
showAllLevel, ignoreAvatarStatus)`이다. 파생 클래스가 `BuildKeywords`로 값을 만들어 넣고,
마지막에 `KeywordDatabase.Convert`가 `<tag=…>`를 푼다.

우리 덤프가 이 함수를 그대로 부르는 이유와 인자 선택:

- 문장 조립을 우리가 흉내 내면 게임과 어긋난다. 값이 프리팹의 레벨별 배열에서 나오기 때문이다.
- `showAllLevel: true` — 덤프는 아티팩트 종류마다 한 번뿐인데 값은 레벨마다 다르다. 이 인자를
  켜면 `+3~+9` 꼴의 범위로 나와 어느 레벨에서도 거짓말이 아니다.
- `avatar: null`, `ignoreAvatarStatus: true` — 덤프 시점에는 플레이어 상태가 없다.
- 프리팹 컴포넌트라 `RequestCharmDamageBonusOnRoot`는 `netIdentity`가 없어 0을 돌려준다. 안전하다.
- 그래도 런타임 상태가 있어야 문장을 만드는 아티팩트가 있을 수 있어 종류마다 try/catch 로 감싸고,
  실패하면 설명 없이 둔다.

결과에는 `<color=…>`, `<sprite=…>`, `<indent=…>` 같은 TextMeshPro 서식이 남으므로
`SephPlanner.Core`의 `RichText.Strip`으로 걷어낸 뒤 저장한다. HUD 색은 `NativeSkin`이 정한다.

덤프에 항목이 늘어나는 변경이라 `PlannerData.CatalogVersion`을 두었다. 덤프는 첫 실행에 한 번만
만들어지므로, 번호가 없으면 예전 덤프를 가진 사람은 새 항목이 영영 빈 채로 남는다.

## 텍스트 데이터

`Sephiria_Data/StreamingAssets/Localization/*.json`이 **평문 JSON**이고 15개 언어가 모두 들어 있다.
평평한 key-value 구조이며 총 6,590개 키.

- 석판: `Item_StoneTablet_{Id}_Name`
- 아티팩트: `Charm_{Id}_Effect`, `Charm_{Id}_Effect2`, `Item_{Id}_Name`, `Item_{Id}_FlavorText`

`SephPlanner.DataTool`이 여기서 **석판 68종, 아티팩트 257종**을 뽑는다. 게임 코드에서 아티팩트는
`Charm`으로 불린다.

플러그인이 게임 안에서 뜨는 카탈로그는 **299종**이다. 여기 257종은 로컬라이제이션 키를 센 것이라
이름 키가 없는 것(특수 경로로만 나오는 것, 이름을 코드가 짓는 것)이 빠진다. 두 숫자가 다른 것은
모순이 아니라 세는 대상이 다른 것이다.

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

`SephPlanner.Core`의 `TabletQuery`가 이 DSL의 포팅이다. 솔버를 게임 객체와 분리해 테스트하려면
포팅이 필요하고, 한 칸이라도 어긋나면 결과 전체가 틀어진다.

`SephPlanner.Plugin`의 `QueryVerifier`가 게임 안에서 원본 `StoneTablet.ParseQuery`와 전수 대조한다.
석판 68종 × 질의 2종 × 회전 4 × storage 7단계 × 모든 원점 조합을 돌려 위치·값·플래그를 비교하고,
결과를 `%LOCALAPPDATA%\SephPlanner\catalog-generations\<generation>\query-verification.txt`에
남긴다. 데이터 루트의 `active-catalog.txt`가 현재 활성 generation을 가리키며, 같은 generation의
`manifest.txt`가 파일 크기·SHA-256·게임 버전·게임 어셈블리 MVID를 묶어 검증한다.

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

주의: 이 판정들의 게임 구현에는 격자 폭이 항상 6이라는 전제의 **하드코딩이 섞여 있다.**
`SideEnd`는 `x == 0 || x == 5`, `BottomInInventory`/`Outlined`는 `storage - 6`,
`Inside`는 `+ 7` 을 그대로 쓴다. Core 의 `CharmCriteria`는 이를 "고치지" 않고 그대로
옮겼다 — 정리하면 게임과 어긋난다. 각 클래스의 `GetCriteria` 디컴파일로 확인했다.

## 레벨이 정해지는 순서

`GridInventory.ReleasePermission`이 `levelMatrix`를 만드는 순서다. 순서가 중요한 이유는 배수가
중간에 한 번 걸리기 때문이다.

1. 아티팩트 칸마다 **인챈트**를 더한다. 값은 `DungeonManager`의 `globalItemStatTable`에
   `"{instanceID}/Enchant"` 키로 인스턴스마다 따로 들어 있다.
2. **고정 각인**(`fixedEngravingsOnServer`)의 고정 레벨·비활성·조건무시·배수를 더한다.
3. **석판과 각인**(`stoneTablets`, `engravings`)의 `ApplyEffect`.
4. **배수 행렬을 곱한다.** 여기까지 더해진 값 전체에 곱한다. 배수는 `multiplyLevelMatrix`에
   **덧셈으로 쌓인다** — `MUL/2`와 `MUL/3`이 겹치면 ×6이 아니라 ×5다. 쌓인 합이 0이면
   `ReleasePermission`이 곱셈 자체를 건너뛰므로 ×0이 아니라 ×1이 된다. 디컴파일로 확인했고
   시뮬레이터가 같은 규칙을 쓴다.

배치 보너스는 이 파이프라인 뒤에 돌지만 레벨을 바꾸지 않는다(아래 참고).

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

### 고정 각인과 참가자의 신비 복원

**고정 각인**(`fixedEngravingsOnServer`)은 서버에만 있는 `List<FixedEngraving>`이다. 각 원소는
질의가 아니라 **절대 좌표로 이미 풀린 효과 딕셔너리**(fixedLevel/fixedDisable/fixedIgnoreCriteria/
fixedMultiplyLevel)를 들고 있고, 배수는 석판과 같은 `multiplyLevelMatrix`에 쌓인다.

정체의 대표가 **신비(MYSTIC) 콤보**다. `ComboEffect_Mystic`이 임계값마다 석판 12002를 고정
각인으로 심는다. 커뮤니티의 다른 자동배치 모드가 "신비 콤보 ×2 미반영" 버그를 겪은 원인이
바로 이것이다.

싱글은 호스트 모드라 이 목록을 그대로 읽을 수 있다. `GameReader.ReadFixedEffects`가 칸 효과로
합쳐 스냅샷(`FixedEffects`)에 싣고, `TabletSimulator`가 행렬에 먼저 깔고 시작한다.
`SimulationVerifier`도 같은 기준으로 대조한다.

**참가자는 신비 각인만 공개된 상태로 복원한다**(2026-09-05 수정). 설치된 1.0.30의
`ComboEffect_Mystic.OnEnableEffect`, `FixedEngraving.ApplyEffect`, `GridInventory`를 다시
디컴파일해 확인했다. `mysticPositions`는 `SyncList<ItemPosition>`, `currentSetEffectCount`는
`SyncDictionary<string,int>`다. 신비 프리팹의 `first/firstEngravingCount`에 따라 앞쪽 좌표를,
`second/secondEngravingCount`에 따라 그 다음 좌표를 사용한다. 두 번째 단계는 첫 번째에 더해진다.

임계값·개수·각인의 석판 번호를 상수로 박지 않고 실제 콤보 프리팹에서 읽는다. 그 석판의
`query/conditionQuery`를 사용해 회전 0의 고정 효과를 만든다. 현재 로컬 카탈로그의 석판 12002는
`O MUL/2`, 조건 없음이다. 일반 석판의 `GetQuery/GetConditionQuery`는 이 필드를 그대로 돌려주며,
인스턴스별 질의가 필요한 커스텀 석판은 복원하지 않는다. 조건이 있는 신비 각인도 과거 생성 시점의
점유 상태를 알 수 없어 지원하지 않는다. 동기화되지 않은 좌표를 (0,0)으로 대체하지 않는다.

호스트에서는 원본 목록만 사용하므로 신비를 두 번 더하지 않는다. 최종 레벨의 차이를 역산해
보정하는 방식이 아니며, 다른 미지원 고정 각인의 불일치는 계속 검출된다. `multiplyLevelMatrix`와
`ignoreCriteriaMatrix`도 SyncDictionary이므로 실시간 검증에서 대조한다. 레벨 0인 칸에서도
누락된 배수·제한 해제를 잡아야 새 아이템을 옮긴 뒤의 계산을 믿을 수 있다.

사용자 스크린샷의 (3,1) 레벨 3/게임 6과 동일한 합성 테스트에서, 신비 복원 전에는 행렬과
아이템 레벨 검증이 각각 한 번 실패하고 복원 뒤 모두 통과한다. 스크린샷 시점의 실제 F10 자료는
아직 받지 못했으므로 그 판의 전체 재생 및 수정 DLL의 실기 확인과 구분한다.

### 배치 보너스는 레벨과 무관했다

전에는 배치 보너스(`SearchArrangementBonusInInventory`)가 곱셈 뒤에 레벨에 더해진다고 적었는데,
디컴파일을 끝까지 따라가 보니 **틀린 추정이었다.** 이 함수는 `ReleasePermission`에서 곱셈 뒤에
호출되긴 하지만 `levelMatrix`를 건드리지 않는다. 실체는:

- `ArrangementBonusEntity`(Resources "ArrangementBonus")가 "특정 아이템들을 특정 상대 배치로
  놓으면"이라는 레시피를 정의하고,
- 일치하면 해당 아티팩트의 `OnApplyArrangementBonus`(가상 메서드)가 불린다. 이를 구현한
  아티팩트는 **셋뿐이다**: `Charm_FrostiumRing`, `Charm_SummonGreenBat`, `Charm_TooCloseDamage`.
  전부 전투 효과라 레벨 행렬에는 흔적이 없다.

따라서 레벨 불일치의 원인 목록에서 배치 보너스는 빠진다. 이 셋의 자리 가치는 아래
이웃 의존 아티팩트와 같은 부류의 과제다.

### 아직 반영하지 않은 것
- **이웃 의존 아티팩트.** 효과의 세기가 자기 레벨이 아니라 다른 칸의 내용에 달린 아티팩트들이다.
  디컴파일 전수 검색으로 `Inventory.FindItem`을 부르는 아티팩트 클래스를 세어 보니 12종쯤 된다:
  `Charm_NearLevelDamage`(조화의 수정 - 이웃 8칸 유효 레벨 합에 비례), `Charm_UpCharmDamage`,
  `Charm_AutoMagic`, `Charm_ReduceMPCost`(레이에 별조각),
  `Charm_RightSpellCooldownHelper`(빛나는 모래시계),
  `Charm_NearMagicBullet`, `Charm_PlanetModule`, `Charm_WoodenBox`,
  `Charm_MagicCoolDownBonusByTag`, `Charm_BoltMagicMultiShot`, `Charm_CompanionChaos`.
  (`Charm_UpCharmDamage`가 북향의 금빛침이다 - 예전에 모래시계 쪽에 잘못 달아 두었던 이름을
  디컴파일로 바로잡았다.) 각자 로직이 달라 일괄 모델이 없고, 레벨 행렬에는 영향이 없어 불일치
  경고로도 안 잡힌다 (전투 스탯으로만 새므로). 배치 점수가 이들의 자리 가치를 과소평가하는
  문제이며, 아직 모델에 없는 것은 사용자가 강화 우선 지정(`F2` 빌드 창)으로 보정한다.
  빛나는 모래시계는 이후 방향 의존 마법 강화로 반영했다. `Charm_RightSpellCooldownHelper.SearchMagic`은
  바로 오른쪽의 `Charm_Magic`에 프리팹의 `cooldownRecoveryByLevel`을 더한다. `Charm_Magic.OnUpdate`는
  마법이 활성일 때만 재충전하며, 기본 쿨다운을 `(100 + 전체 회복 속도 + 추가 회복 속도)/100`으로 나눈다.
  카탈로그에는 프리팹의 표와 방향을 저장한다. 대상이 실제로 사용 가능할 때만 강화 가치를 평가하며,
  다른 버프·마나·시전 빈도가 없는 기본 속도를 환산의 추정 기준으로 삼는다. 사용 유지는 이 연결까지
  확인하되 게임의 자체 활성 여부와는 분리한다.
  **앞서 12종 중 다섯을 넣었고**(하얀 종이, 조화의 수정, 북향의 침, 거대한 망원경, 헌신의 휘장),
  목록에 없던 캘세더니 열쇠(`Charm_3Elemental_ByRow`)도 같은 부류라 함께 넣었다.

  이 부류의 첫 사례로 **하얀 종이(`Charm_WhitePaper`)는 모델에 넣었다.** 좌우 이웃의 아티팩트가
  공유하는 카테고리를 자기 것으로 물려받아 콤보 개수에 +1을 보태는 아이템이다(둘 다 가진
  카테고리만, `match=2`). 카탈로그가 컴포넌트 클래스 이름(`Behavior`)을 실어 주고, 솔버의
  배정 고정점 루프가 직전 반복의 배치를 이웃 지도로 넘겨 "같은 카테고리 쌍 사이에 낀
  하얀 종이"에 콤보 한 걸음 가치(`Worth.OfComboStep`)를 더한다. 최종 점수(Describe)에도 같은
  값이 들어간다. 근사가 하나 남는다: 이웃은 직전 반복 기준이라 쌍을 종이 주위로 재구성하는
  탐색까지는 못 하고(끼울 자리가 이미 있으면 찾아간다). 콤보 개수에 종이 자신의 기여가 섞이는
  것은 아래 "콤보를 세는 규칙" 의 방법으로 뺀다.

  **조화의 수정(`Charm_NearLevelDamage`)도 모델에 넣었다.** 게임 로직은 단순하다:

  ```
  보너스 = floor( allDamageBonusByLevel[내 레벨] × Σ 이웃 8칸 min(그 아티팩트 레벨, 그 상한) )
  ```

  이웃 여덟 칸(대각 포함)에 아티팩트가 있을 때만 세고, 상한만 씌울 뿐 아래로는 자르지 않아
  음수 레벨 이웃은 오히려 깎는다. 레벨별 배수 표(`allDamageBonusByLevel`, 예: `0,1,1,2`)는
  카탈로그 덤프가 `NeighborLevelBonus`로 실어 준다. 자기 레벨로 색인한다
  (`LevelToIdx` = clamp(level, 0, maxLevel)).

  결과는 전체 피해 보너스라 우리 점수(레벨) 단위가 아니다. `Worth.DamageBonus`가 그 환산값이고
  아래 "콤보 가중치" 절의 `--measure`로 **0.4 로 실측했다**(`Worth.cs`). 다만 **자리 선택은
  환산값과 무관하게 옳다.** 배수가 양수이기만 하면 이웃 레벨 합이 큰 칸을 고른다.

  하얀 종이와 같은 근사가 걸리되 한 가지가 다르다. 이웃 지도는 직전 반복의 배정이라, 이 아티팩트
  자신이 서 있던 칸이 이웃으로 잡힌다. 그대로 세면 자기를 세는 것이고 건너뛰면 빈 칸으로 치는데,
  배정은 자리 맞바꾸기이므로 **지금 목표 칸에 있는 아티팩트가 그 자리를 채우는 것으로 갈음한다.**
  이 처리가 없으면 이웃이 하나 모자라게 세어져 가운데 칸의 우위가 사라진다(고정 테스트
  `NeighborValueTests`가 그 경우를 잡는다). 하얀 종이 쪽은 좌우 둘 다 조건이라 같은 상황에서
  값이 0 이 되고 말아, 아직 같은 처리를 넣지 않았다.

  **넷을 더 넣었다**(`PositionalWorth`, 고정 테스트 `PositionalWorthTests`). 넷 다 공통점이
  있다 — 다른 플래너가 "대상 지정" 단추로 사람에게 묻는 것들인데, **게임은 자리만 보고 정한다.**
  그래서 물어볼 것이 없고 솔버가 알아서 좋은 자리를 찾으면 된다.

  | 아티팩트 | 게임 클래스 | 게임이 보는 것 |
  |---|---|---|
  | 북향의 금빛/파란 침 | `Charm_UpCharmDamage` | `(x+xOffset, y+yOffset)` 칸 하나. 기본은 바로 위 |
  | 거대한 망원경 | `Charm_PlanetModule` | 이웃 여덟 칸의 `PLANET` 카테고리 아티팩트 |
  | 헌신의 휘장 | `Charm_CompanionChaos` | 같은 행 전체(`0..Width-1`)의 `ICompanionCharm` |
  | 캘세더니 열쇠 | `Charm_3Elemental_ByRow` | 자기 행 하나. `lineCategory[YIdx % 개수]` |

  침이 특히 그렇다. `OnRequestCharmDamageBonus`는 대상을 못 찾으면 **0**을 돌려주므로, 대상 없이
  선 침은 자리만 차지하고 아무 일도 하지 않는다. 대상 자격은 `IsDependencyValid`가 정한다 —
  `IAttackableCharm`이거나 다른 침이다. 침 위에 침이 있으면 `SearchCategory`가 각 침의 오프셋을
  따라 계속 올라가 침이 아닌 아티팩트에서 멈추고, **사슬의 침 전부가 그 아티팩트를 인정할 때만**
  물려받는다. 우리도 같은 걸음을 걷는다. 크기는 두 표(`damageBonusByLevel`,
  `dependencyDamageBonusByLevel`)의 **비율**로만 쓰므로 단위를 옮길 필요가 없다 - 레어도 조건
  (`maxRarity`)까지 맞는 대상 위에 선 침이 그만큼 더 값어치가 있다.

  **콤보를 세는 규칙에 여기서 예외가 생긴다.** 위의 "개수 판정" 항목은 배치·레벨·활성 여부와
  무관하다고 적었고 평범한 아티팩트에서는 맞지만, 침과 캘세더니 열쇠는 `GetItemCategory()`를
  덮어써 **자리에 따라 다른 카테고리를 내보인다** — 침은 대상에게 물려받은 것을,
  열쇠는 `lineCategory[행 % 개수]`를. `SearchSetEffectInInventory`가 읽는 것이 그 값이므로,
  이 둘에 한해서는 콤보 개수가 배치에 달려 있다. 그래서 배치 점수에 콤보 한 걸음
  (`Worth.OfComboStep`)이 들어간다.

  **그 걸음의 출발 개수에서 자기 몫을 빼야 한다(0.2.1).** 스냅샷의 콤보 개수는 게임이 센 것이라
  침·열쇠·종이가 지금 자리에서 보태는 몫이 이미 들어 있다. 0.2.0 은 그 개수 그대로 "하나 더" 를
  쟀고, 실제 판에서 이렇게 돌았다 - FLAMESWORD 아티팩트 9개에 침 둘이 붙어 11(임계값 4/6/8/10 을
  다 넘김), EMBER 8. 침이 보기에 FLAMESWORD 는 더 모을 것이 없고 EMBER 는 진행이라 둘 다 EMBER
  밑으로 가고, 게임이 EMBER 10 / FLAMESWORD 9 로 다시 세면 다음 계산은 "FLAMESWORD 하나 더면 10"
  이라며 도로 부른다. `F8` 을 누를 때마다 6~7수가 나온 것이 이것이고, 열쇠도 같은 식으로 자기
  하나로 임계값 2 를 "채웠다" 고 보고 있었다. `ComboCounting` 이 게임 개수에서 자리 의존
  아티팩트들의 지금 자리 몫을 뺀 바탕 개수를 만들고, 평가하는 배치에서 **다른** 자리 의존
  아티팩트가 보태는 몫만 더한다. 같은 판을 `--churn` 으로 돌리면 0.2.0 은 두 배치 사이를 영원히
  오가고(점수 119.7 ↔ 120.56/126.5), 고친 뒤에는 한 번에 EMBER 10 / FLAMESWORD 10 으로 가서
  멈춘다. 우리가 다시 센 개수가 게임 개수와 같다는 것도 그 판에서 확인했다(11 = 9 + 2).

  망원경과 휘장은 크기를 재지 못했다. 강화(`SetEnhancement`)도 혼돈 모드(`SetChaoticMode`)도
  능력치가 아니라 소환물의 동작을 바꾸는 것이라 정적 데이터에 수치가 없다. 방향(모아 두는 쪽이
  낫다)만 확실하므로 `PositionalWorth.EnhanceStep`을 **그 아티팩트의 레벨 한 칸**으로 두었다 -
  콤보 한 단계(3.4)의 1/3 남짓이라 잰 값을 뒤집지 못하는 크기다. `ComboProgress`와 같은 성격의
  값이며, 재고 나면 그 상수 하나만 고치면 된다.

  이 넷을 넣으면서 **배정 뒤에 다듬는 단계**(`PlacementSolver.Polish`)가 필요해졌다. 헝가리안의
  비용이 직전 반복의 이웃을 보고 매겨지는 근사라, 두 아티팩트가 동시에 움직여야 좋아지는 수
  (침 둘을 한 아티팩트 아래로 쌓기 같은)를 배정기 혼자서는 못 넘는다. 이긴 배치 하나에만
  자리 맞바꾸기를 몇 번 돌린다 - 후보마다 걸면 풀이 시간이 두 자릿수 배로 뛴다. 실측으로
  42칸·석판 8·아티팩트 30 판에서 **점수 +2.5, 시간 차이는 측정 잡음 안**이었다.

## 콤보 가중치

`Worth.ComboThreshold`(2.0)와 `ComboProgress`(0.25)는 근거 없는 설계값이었다. 콤보 쪽으로 추천이
얼마나 기우는지를 정하는 값이라 짐작으로 둘 수 없어 재는 길을 만들었다.

**다리는 두 쪽이 같은 능력치 체계(`StatusDatabase`)를 쓴다는 점이다.**

- 아티팩트: `Charm_StatusInstance.stats[]`가 `{ statusID, valuesByLevel[] }`. 레벨 하나가 그
  능력치를 얼마 올려 주는지 그대로 나온다.
- 콤보: `ComboEffectBase.addStatByCombo[].status[]`가 `"ID/VALUE"` 문자열. 게임의
  `StatusDatabase.CreateStatusEntity`가 `/`로 쪼개는 것과 같게 읽는다.

그래서 능력치별로 "레벨 하나당 얼마"를 구하면 콤보가 주는 값을 레벨 단위로 옮길 수 있다. 같은
능력치를 주는 아티팩트가 여럿이라 **중앙값**을 쓴다 — 평균은 유별나게 센 아티팩트 하나에 끌려간다.
아티팩트가 아무도 주지 않는 능력치는 0으로 삼키지 않고 "못 옮김"으로 남겨 보이게 한다.

플러그인이 원자료를 `stat-measure.json`으로 떠 두고, 계산은 게임 밖에서 한다.

```
dotnet run --project src/SephPlanner.DataTool -- --measure
```

능력치별 레벨당 증가분, 콤보 임계값별 값어치, 그 중앙값을 찍는다.

**임계값 묶음은 누적이 아니라 증분이다.** `ApplyComboEffect`가 `comboCount` 이하인 묶음을 전부
적용하므로, 한 걸음 넘을 때 새로 붙는 것은 그 단계의 묶음뿐이다. 그래서 측정된 임계값별 값어치가
곧 `ComboThreshold`가 뜻하는 "발동할 때의 이득"과 같은 것이다.

### 측정 결과

아티팩트 능력치 표 262건, 콤보 능력치 94건. 임계값 74건 중 63건을 환산했고 **중앙값은 3.43
레벨**이었다. `Worth.ComboThreshold`를 2.0 에서 **3.4** 로 옮겼다 — 콤보를 그동안 낮게 보고 있었다.

폭이 넓다(FROST 2개 0.4 ~ LAKE 9개 14.17). 평균이 아니라 중앙값을 쓰는 이유가 여기 있다.

못 옮긴 11건은 아티팩트가 아무도 주지 않는 능력치를 주는 단계다 — `PLANET_DAMAGE`,
`POTION_SLOT`, `*_DAMAGE_AMP`, `BURN_EVO`, `FREEZE_THRESHOLD` 등. 이들은 0으로 삼키지 않고
중앙값 계산에서 빼는 방식이라 값이 낮게 왜곡되지는 않는다. 다만 **일부만 못 옮긴 단계
(SAVVY, DARKCLOUD 10개 등)는 그만큼 낮게 잡힌다.** 알고 있는 편향이다.

### 조화의 수정 환산값도 이 표에서 나왔다

`StatusInstance_FinalDamage`가 조화의 수정과 **똑같은 `ECustomStat.AllDamageBonus`** 를 더한다.
그러니 `FINAL_DAMAGE`의 측정된 레벨당 증가분 2.5 가 그대로 다리다 — 전체 피해 보너스 1점 =
레벨 0.4. `Worth.DamageBonus`를 0.05(짐작)에서 **0.4**로 고쳤다.

여덟 배 커진 값이라 조화의 수정의 자리 가치가 크게 오른다. 이웃 여덟 칸이 전부 레벨 3이고
자기도 만렙이면 `2 × 24 × 0.4 = 19.2` 점으로 격자에서 가장 값진 아티팩트가 된다. 조건이 그만큼
까다로운 아티팩트라 부당한 결과로 보이지는 않지만, 실제로 이상하게 기울면 상수가 아니라 **환산
방법**(FINAL_DAMAGE 를 다리로 삼은 것)을 먼저 의심한다.

레벨에 관해서는 어긋나는지를 **항상 확인한다.** 게임이 계산해 둔 `levelMatrix`가 정답지다.

- 플러그인의 `SimulationVerifier`가 아티팩트 칸마다 대조해 어긋나면 BepInEx 로그에 남긴다.
  (석판의 `IsApplied`와 질의 해석 범위만 보던 것에서 최종 레벨까지 넓혔다)
- HUD는 어긋난 칸이 있을 때만 "점수가 실제와 다를 수 있다"고 알린다. 무엇이 원인인지는
  알 수 없으므로 추측해서 미리 경고하지 않는다.
- `dotnet run --project src/SephPlanner.DataTool -- --check <스냅샷.json>` 으로 게임 없이도 본다.

실제 런에서 한 번 재어 보니(1.0.30, 석판 6개·아이템 13개) 석판 적용은 6개 모두 맞고 칸별 레벨도
한 칸을 빼고 전부 일치했다. 그 한 칸은 인챈트를 싣기 전 플러그인이 만든 스냅샷이라 설명이 된다.
표본이 한 런이라 일반화할 수는 없지만, 적어도 각인과 세트 효과가 늘 걸려 있는 것은 아니다.

## 아티팩트 가치

점수는 오랫동안 아티팩트를 **"켜져 있으면 1점, 레벨 하나에 1점"**으로 세고 레어도 배수
(`Worth.OfRarity`, 1.0~1.7)로만 우열을 갈랐다. 그래서 전투 효과가 센 아티팩트와 밋밋한
아티팩트가 똑같이 `+2`로 나왔다. 레어도는 근거 없는 대리값이었다.

**콤보 가중치를 재는 데 쓴 다리가 여기에도 그대로 놓였다.** `Charm_StatusInstance.stats[]`가
`{ statusID, valuesByLevel[] }`이므로, 능력치별 "레벨 하나당 얼마"(`StatExchange`)를 구해 두면
아티팩트가 레벨마다 주는 것을 레벨 단위로 옮길 수 있다.

```
값어치(레벨 L) = Σ  valuesByLevel[min(L, 상한)] / 능력치별_레벨당_증가분
              능력치
```

계산은 `CharmStatWorth`가 하고, 플러그인이 덤프 시점에 한 번 재어 `CharmDefinition.
StatWorthByLevel`에 실어 둔다. 콤보 가중치와 같은 환산율을 쓰므로 `Worth.ComboThreshold`(3.4),
`Worth.DamageBonus`(0.4)와 **같은 자로 잰 값**이다.

### 레어도는 대리값으로서 나빴다

잴 수 있는 110종(능력치만 주는 아티팩트)을 재고 레어도별로 묶어 본 결과다.

| 레어도 | n | 레벨 0 값어치(중앙) | 레벨당(중앙) | 레어도 배수 |
|---|---|---|---|---|
| 일반 | 18 | 1.50 | 1.50 | 1.00 |
| 고급 | 27 | 1.67 | 1.26 | 1.10 |
| 희귀 | 26 | 2.00 | 1.67 | 1.25 |
| 전설 | 11 | 1.47 | 2.11 | 1.45 |

**순서가 서지 않는다.** 전설의 레벨 0 값어치가 일반보다 낮다. 그리고 같은 레어도 안의 폭이
레어도 사이의 차이보다 훨씬 크다 — 전체 범위가 상한 기준 `-1.55 ~ 20.00`인데 레어도 배수가
설명하는 폭은 1.0~1.45 뿐이다. 일반인 압박 밴드가 웬만한 전설보다 높게 측정된다.

### 잡힌 결함

레벨을 올릴수록 **나빠지는** 아티팩트가 있다(도마뱀 판금 갑옷: 상한 4에서 `2.76 → -1.55`).
예전 모델은 값이 레벨에 대해 언제나 증가한다고만 알아서 이런 아티팩트를 좋은 칸에 앉혔다.
고정 테스트 `CharmWorthTests.ACharmThatGetsWorseWithLevelsGivesUpTheHighCell`이 그 경우다.

### 어디까지 답이 나왔나

아티팩트 299종 기준(1.0.30)이다.

| | 종 | 근거 |
|---|---|---|
| 잰 값이 값어치 전부 | 110 | 능력치만 주는 아티팩트(`Charm_StatusInstance`) |
| 잰 값이 아래 한계 | 47 | 파생 클래스. 능력치 밖에 고유 효과가 더 있어 표가 값어치의 일부만 담는다 |
| 레어도 어림값뿐 | 142 | 능력치를 주지 않는다. **손으로 채울 몫** |

파생 클래스에서 잰 값을 **아래 한계로만** 쓰는 것이 중요하다. 그대로 쓰면 능력치가 없다시피 한
소환 아티팩트를 0점으로 보게 된다. 잰 값이 어림값보다 클 때만 잰 값을 쓴다.

### 믿을 수 없는 환산율은 표시한다

능력치 82종 중 43종은 그것을 주는 아티팩트가 하나뿐이다. 그러면 환산율이 그 아티팩트 자신에게서
나와 "레벨 하나 = 이 아티팩트의 한 걸음"이라는 동어반복이 된다. 재어진 110종 중 42종이 값어치를
전부 그런 능력치에서 얻는다.

**값을 버리지는 않는다.** 그런 아티팩트에서도 레벨 0의 값어치는 실제 비율로 나오고, 레벨당
값어치는 1.0으로 떨어질 뿐이라 예전 모델과 같아진다 — 나빠지지 않는다. 다만 그 사실을
`StatWorthConfidence`(0~1)로 남기고 `--values`가 찍는다.

### 가격은 레어도와 같은 말이었다

능력치로 잴 수 없는 142종을 위해, 개발사가 아이템마다 매겨 둔 값을 대리값으로 쓸 수 있는지
재 봤다. `ItemEntity.cost`와 `sapphirePrice`가 후보였고 `customCost`가 선언만 되고 읽히지
않는다는 점이 근거였다. **답을 아는 110종으로 재니 순위 상관이 레어도와 소수점까지 같았다
(0.34).** 레어도마다 가격이 한 값뿐이기 때문이다 — 일반 400, 고급 600, 희귀 800, 전설 1100.
덤프에는 남겨 두어 `--values`가 매번 다시 잰다. 패치로 가격이 아이템별로 갈리면 그때 드러난다.

### 손으로 채우는 몫

`data/values/charms.json`이 그 자리다. 채우는 법은 `data/values/README.md`에 있다.

```powershell
dotnet run --project src/SephPlanner.DataTool -- --values
```

지금 무엇이 측정됐고 무엇이 비어 있는지 찍고, 비어 있는 142종을 식별자와 게임 효과 설명까지
붙여 `charms.draft.json`으로 뽑는다. 빈 표를 앞에 두면 아무도 채우지 않기 때문이다.

등급(`tier`) 다섯 칸의 값은 짐작이 아니라 **측정된 분포의 10·25·50·75·90 분위**다.

| 등급 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|
| 레벨 0 | 0.00 | 1.00 | 1.67 | 2.03 | 3.34 |
| 레벨당 | 0.67 | 1.00 | 1.46 | 2.00 | 2.49 |

그러니 3등급은 "평범한 아티팩트만큼", 5등급은 "상위 10%만큼"이라는 뜻이 된다. 게임이 패치되면
분위가 움직이므로 `--values`가 찍는 값으로 `CharmWorth.Tiers`를 다시 맞춘다.

## 점수를 어떻게 매기는가

점수는 **켜져 있는 아티팩트마다 그 아티팩트가 실제로 받는 레벨에서의 값어치**를 더한 값이다.
값어치는 `CharmWorth`가 답하며 단위는 레벨이다(위 "아티팩트 가치" 절).

레벨만 세면 안 된다. 게임에서 레벨 0인 아티팩트는 멀쩡히 작동하고 레벨은 효과의 세기를 더할
뿐이기 때문이다. 레벨만 세면 **꺼지는 칸(레벨 음수)과 레벨 0 칸이 똑같이 0점**이 되어, 아티팩트를
꺼진 채로 두고도 최적이라고 말하게 된다. 실제로 그런 화면이 나온 적이 있다. 그래서 값어치에는
언제나 레벨과 무관한 몫(`Base`)이 있다.

아티팩트 사이의 우열은 근거가 좋은 것부터 쓴다 — 손으로 채운 값, 게임에서 잰 값, 마지막이
레어도다. 사용자가 `F2` 빌드 창에서 강화 우선을 지정하면 그 위에 단계별 배수(2/4/10배)가
곱해진다. 카테고리(콤보)
가치는 일반 후보 추천에서 따로 계산하고, 열쇠·종이·침처럼 배치가 카테고리를 바꾸는
효과에는 배치 평가에도 반영한다. F2의 열쇠·종이 콤보 지정은 점수 배수와 별도의 우선순위다.

빔 탐색의 `EstimateModel`도 아티팩트별 가치와 강화 우선을 사용한다. 최종 배치와 모든
상호작용을 미리 아는 것은 아니므로 후보를 좁히는 근사이며, 남은 후보를 뒤에서 다시 평가한다.

## 배치 최적화

문제를 두 조각으로 나눈다.

**석판 배치**는 조합 탐색이다. 석판을 하나씩 놓으며 빔 서치로 후보를 좁힌다.

**아티팩트 배치**는 석판이 정해지면 칸마다 레벨이 확정되므로 배정 문제가 된다.
헝가리안 알고리즘은 주어진 비용 행렬의 최적 배정을 구한다. 이웃·활성 조건이 배치에
의존하면 그 비용 자체가 근사이므로 전체 문제의 최적해를 뜻하지 않는다. 칸이 모자라는
경우의 배정과 후보별 제외 갈래도 제한된 탐색 안에서 비교한다.

석판 조건과 아티팩트 조건이 서로의 배치에 의존하므로 몇 번 되풀이해 수렴시키고,
**최종 점수는 선택된 배치로 다시 계산한다.** 표시 점수와 배치는 같은 모델을 사용하지만,
평가 모델에 남은 추정까지 실제 전투 효과로 확정되는 것은 아니다.

## 선택지 감지

두 갈래로 나뉜다. 하나로 될 줄 알았는데 아니었다.

### 인벤토리를 가진 것

상자(`ItemChest.inventory`), 바닥에 떨어진 꾸러미(`DroppedInventory.Inventory`), 상점은
**각자 `GridInventory`를 들고 있다.** 그래서 화면별 UI 클래스를 다룰 필요 없이, 씬에 있는
`GridInventory` 중 플레이어의 것이 아니고 일정 거리 안에 있는 것을 훑으면 된다.

단, **훑는 것과 보여주는 것은 다르다.** 동기화돼 있어 읽을 수 있다는 것이 플레이어가 볼 수
있다는 뜻은 아니다. 그래서 인벤토리마다 "지금 화면에 보이는가"를 따로 판정한다.

- **상자**는 `ItemChest.isOpened`(SyncVar)가 참일 때만. 내용물이 스폰 때부터 동기화되어 있어
  그대로 읽으면 열기 전에 안이 보인다 — 실제로 열기 전 보상이 추천에 떴다는 제보로 드러났다.
- **상점**은 `UI_ShopPanel`이 열려 있고 그 창의 `Shop`(공개 속성)이 가리키는 인벤토리일 때만.
  전에는 "상점은 진열이 곧 공개"라고 보고 거리만 봤는데 **그 전제가 틀렸다.** 상점 재고는
  상인의 `CurrentSelling` 인벤토리에 있고 세상에 진열되는 것이 아니라 창 안에서만 그려진다
  (`Safe.Trade` → `UI_ShopPanel.Open(..., NetworkconnectedMerchant.CurrentSelling, ...)`).
  가까이 가기만 해도 재고가 떴다는 제보로 드러났고, 열기 전 상자와 같은 문제다.
- **금고와 시체**(상인이 죽은 뒤의 `Safe`)는 `UI_InventoryViewer`가 지금 보여주는 것일 때만.
  이 창은 무엇을 띄웠는지가 전부 비공개라 `Inventory` 속성을 리플렉션으로 읽는다. 읽지 못하면
  "보이지 않는 것"으로 물러선다 — 덜 보여주는 쪽이 원칙에 맞다.
- **바닥에 떨어진 꾸러미**(`DroppedInventory`)만 예외다. 이미 화면에 보이던 인벤토리가 통째로
  떨어진 것이라 숨길 이유가 없다.

거리는 이 판정 위에 남아 있지만, 창이 열려 있다는 것 자체가 더 정확한 신호라 사실상 상자에만
의미가 있다.

### 세피라이트

**석판과 아티팩트는 대개 세피라이트(`Sephirite`)로 나오는데 여기에는 `GridInventory`가 없다.**
대신 `SyncList<SephiriteRewardMetadata> rewards`에 `{ instanceID, entityID }`를 담고 있고,
`isGenerated`가 참이 되면 채워진다. `isAcquired`면 이미 가져간 것이다. 전부 동기화되는 값이라
클라이언트에서 읽을 수 있다.

`Sephirite.Type`에 `TABLET`, `TABLET_BOSS`, `CHARM`이 있는 데서 보이듯 이쪽이 석판의 주 경로다.
`GridInventory`만 훑던 동안에는 석판 추천이 아예 되지 않았다.

세피라이트의 판정 기준은 하나다: **보상 창(`UI_SephiriteRewardPanel`)이 열려 있고, 그 창의
`sephirite` 필드가 가리키는 세피라이트인가.** 모든 세피라이트가 같은 창으로 열리므로 이것이
"플레이어가 지금 고르는 중"의 정확한 정의다.

`isGenerated`로 판정하려던 시도는 두 번 틀렸다.

1. 레벨업 세피라이트는 레벨업 즉시 화면 밖 (-1000,-1000)에 스폰되어
   `LevelController.levelUpQueue`에 쌓이고, 내용이 창을 열기 전에 미리 생성될 수 있다
   (`GenerateItemsForReopen` - 코드에 호출자가 없어 프리팹 이벤트로 묶인 것으로 보인다).
2. "창이 열려 있으면 큐 맨 앞" 조건으로 고쳤더니, **다른 세피라이트를 여는 순간에도 같은 창이
   열리므로** 레벨업을 미뤄 둔 채 일반 세피라이트를 열면 레벨업 보상이 함께 새어 나왔다.
   실제 제보: 창에는 5개가 보이는데 후보에는 리롤로도 안 나온 아이템이 섞여 있었다.

이전 방에 열어 두고 온 세피라이트(생성됨·미획득)가 섞이는 문제도 이 기준이 함께 막는다.

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

**멀티에서는 세피라이트가 플레이어마다 하나씩 같은 자리에 겹쳐 스폰된다.**
`SephiriteSpawner.OnStartServer`가 접속마다 `TrySpawnForConnection`으로 하나씩 소환하고
소유자를 그 플레이어로 지정하며, `Sephirite.HandleInteraction`이 접속 비교로 내 것만 열어 준다.
그래서 남의 세피라이트는 생성됨(`gen=True n=5`)인 채 남아 있어도 내 보상 창이 절대 열리지 않고,
후보도 영영 0개다 - 이것은 버그가 아니라 그 보상을 내가 가질 수 없기 때문이다(2026-08-31 멀티
세션 로그로 확인, BIG 세피라이트가 이 상태였다). 리포트의 `own=`이 이 구분이다. 레벨업
세피라이트의 화면 밖 좌표와 달리, 월드 스폰 세피라이트의 거리는 플레이어 기준으로 정상적으로
나온다(같은 세션에서 d=1.1~15 관측) - 거리 기준이 둘이라는 뜻이므로 여전히 거리로 거르면 안 된다.

### 석판 제단에서는 미리 알 수 없다

`AltarOfTablet`의 선택지 세 개(`AltarOfTabletInteractable`)에는 어떤 석판인지에 대한 정보가 없다.
고르는 순간 서버가 `CmdSpawnReward`에서 세피라이트를 스폰하고 `Sephirite.Initialize(RandomID +
selectionSeedOffset)`으로 내용을 정한다. **고르기 전에 무엇이 나올지는 클라이언트가 알 수 없고,
이는 게임의 설계라 우회할 수 없다.** 추천은 세피라이트가 생긴 뒤부터 가능하다.

거리 기준은 BepInEx 설정의 `OfferRadius`로 조정한다.

## 특수 경로로 얻은 아이템

기적 보상으로 얻은 아이템이 화면에서 인식되지 않는다는 보고가 있었다. 실제 덤프로 확인한
결과 **재현되지 않았다.**

덤프에는 이름 키가 `Item_`이 아니라 `Skill_`로 시작하고(`Skill_0006`, `Skill_0020`) `activeType`이
`Locked`인, 일반 아이템 풀과 다른 경로로 들어온 아티팩트가 있었다. 이들은 전 구간에서 제대로
다뤄진다.

- `inventoryMatrix`에 `Charm_Basic`을 단 인스턴스로 그대로 들어 있다. `GameReader`는 석판만
  걸러내므로 스냅샷에 담긴다.
- 카탈로그에도 있다. `ItemCatalog`는 `Resources`를 직접 훑고 `activeType`이 `Disabled`인 것만
  제외한다. 엔티티 3002/3012가 `maxLevel`과 `categories`까지 갖춰 들어 있었다.
- 그래서 HUD에서도 자리만 차지하는 아이템이 아니라 아티팩트로 채점된다.

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
배치 점수(증가분)와는 섞지 않고 별도 열("잉걸불 7/8")로 보여준다. 가중치는 위 "콤보 가중치"
절에서 실측한 `Worth.ComboThreshold`(3.4)와 `ComboProgress`(0.43)다. 앞의 것은 잰 값이지만
뒤의 것은 아니다 — 못 채운 콤보의 값어치는 그 판에서 결국 채우게 되느냐에 달려 있어 정적
데이터로는 답이 안 나오므로, 비율(1/8)만 예전 그대로 두고 크기만 함께 옮겼다. 추천이 콤보 쪽으로
이상하게 기울면 그 비율부터 의심한다.

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

1. 현재 칸과 목표 칸을 교환하고 밀려난 아이템의 위치도 갱신한다.
2. 갱신된 위치에서 다음 목표를 처리한다. 이미 목표에 도달한 항목은 생략한다.
3. 석판 회전은 교환을 마친 최종 자리에서 안내한다. 빈 대피 칸은 필요하지 않다.

## 제안이 흔들리지 않게 하는 것

석판 배치 탐색은 빔 서치라, 후보를 살펴본 순서와 어디까지 남겼는지에 따라 **점수가 같은 다른 배치**를
내놓는다. 그대로 두면 아무것도 달라지지 않았는데도 제안이 계속 바뀐다. 세 가지로 막는다.

1. 스냅샷에서 받은 석판과 아이템의 순서를 식별자 기준으로 한 번 고정한다. 게임이 넘겨주는 순서는
   물건을 옮기면 달라진다.
2. 현재 배치를 탐색 결과와 별개로 후보 목록에 넣는다. 빔이 현재 배치를 떨어뜨리면 이미 최적인
   배치를 두고도 옮기라고 하게 된다.
3. 지금 자리에 그대로 있는 것마다 이사 비용(`PlacementSolver.MoveCost`, 수당 0.3점)만큼을 더한다.
   0.2.0 까지는 동점만 가르는 크기(1e-6)였는데, 점수가 끝 상태만 세는 탓에 한 수로 얻는 +3.35 와
   열두 수로 얻는 +3.35 가 같은 값이 되어 빈 칸이 많은 판에서 아이템 하나에 판 전체가 뒤집혔다
   (실측 12수/+3.35, 19수/+1.2). 0.3 은 그 둘은 막히고 한두 수짜리 좋은 제안은 통과하는 크기로
   잡은 추정치이지 잰 최적값이 아니다. 문턱("이득 N점 미만이면 무시")이 아니라 수당 비용이어야
   한 수로 얻는 큰 이득은 막히지 않는다. 이 몫은 배치를 고를 때만 쓰고(`Arrangement.Preference`)
   보고되는 점수에는 넣지 않는다 - 점수는 레벨 단위의 값어치라 거기에 섞이면 "현재 / 최선"이
   뜻을 잃는다. 전후 실측은 ROADMAP.md 의 "측정 기준선".

세 번째는 **아티팩트를 칸에 배정하는 단계에서 더해야 한다.** 채점할 때만 더하면 헝가리안이 이미
자리를 정한 뒤라 늦다. 값이 같은 두 아티팩트를 배정기가 임의로 골라서, 이득이 0인데도 둘을
맞바꾸라는 제안이 나온다. 실제로 그런 화면이 나온 적이 있다 — 점수는 `4 → 4`로 같은데
"봉황의 날개깃 (3,3) → (2,3), 파이어 볼트 (2,3) → (3,3)"을 제안했다.

이후 리뷰에서 빈틈 넷이 더 잡혀 닫았다(테스트는 StabilityTests·OfferAdvisorTests).

4. **필러도 자리 유지 몫을 받는다.** 필러는 어느 칸이든 0점 동률이라, 유지 몫이 없으면 배정
   순서에 따라 필러끼리 맞바꾸는 제안이 나온다.
5. **탐색 결과가 지금 배치보다 나쁘면 지금 배치가 답이다.** 조건부 아티팩트는 배정과 조건이
   서로 물려 수렴 반복(3회)이 소진될 수 있고, 그 결과가 손으로 놓은 배치보다 나쁠 수 있다 —
   테스트로 재현하니 증가분 -1.0 인 이동 제안이 나왔다(`BothSidesAreEmpty` 둘 +
   `NeighborsAreFull` 하나). `PlanBuilder.Build` 가 두 점수를 견줘 막는다.
6. **후보 줄 세우기의 마지막 동률은 정의 번호로 가른다.** 입력 순서는 게임의 오브젝트 열거
   순서(`FindObjectsByType`, 비보장)라 폴링마다 흔들릴 수 있다.
7. **읽는 쪽도 순서를 고정한다.** `OfferReader` 가 인벤토리는 netId 로, 안의 아이템은
   InstanceID 로 정렬한다. 열거 순서만 흔들려도 스냅샷 JSON 비교가 "변경"으로 오인해
   재계산이 돌고 후보 순번이 바뀐다.

이 흔들림은 당시 파이프 스냅샷을 녹화해 잡았다. WPF 제거와 함께 실시간 녹화 파이프는 없앴지만,
DataTool의 `--replay <폴더>`는 기존 기록을 새 코드로 다시 풀어 흔들림과 풀이 시간을 재는 용도로
남겼다. 개별 스냅샷은 `--check <스냅샷.json>`으로 대조한다.

멀티 실측(2026-08-31, 파이프 스냅샷 108장 녹화)에서 더 큰 결이 드러나 둘을 더했다.
**제안을 한 수씩 따라가는 동안 걸음마다 남은 목표들이 저희끼리 뒤바뀌고, 아이템 하나를
주울 때마다 같은 점수의 전혀 다른 배치로 계획이 통째로 다시 쓰였다**(15~16수 개편이 연발).
근사 최적해가 여럿인데 솔버가 무기억이라 그중 아무 것이나 골랐기 때문이다.

8. **직전 제안이 앵커다(계획 이력).** `PlanRunner` 가 직전 `Plan` 을 `PlanBuilder` 로 넘기고,
   솔버는 그 배치를 후보로 주입하며 동점일 때 `PlanBonus`(2e-8, 이사 비용 `MoveCost` 보다
   훨씬 약하게)로 저번에 말한 자리를 고른다. 우선순위가 중요하다 - 지금 자리 > 직전 제안
   순이어야, 옮길 필요가 없어진 것을 도로 옮기라는(이득 0 이동) 제안이 안 나온다. 그래서
   드래그 맞바꿈으로 밀려난 아이템이 동점 칸에 떨어지면 계획이 그쪽으로 줄어드는 것은 남는데,
   이는 이동 수를 줄이는 올바른 적응이다.

   **계획에 없던 새 석판이 오면 앵커 후보를 버리지 않고 완성한다**(`PlannedLayout`) - 계획된
   자리는 그대로 두고 새 석판만 남는 칸에서 탐욕으로 앉힌다. 새 석판이 올 때마다 앵커가 통째로
   사라지면 정확히 개편이 가장 큰 순간에 계획이 다시 쓰인다. 그렇게 완성한 후보가 있어도 새
   배치가 점수로 실제로 이기면 개편은 일어난다 - 석판 하나가 판을 +3~5점 바꾸는 실측 사례들이
   그랬고, 그건 맞는 개편이다.
10. **조건이 점유에 기대는 아티팩트는 사용자가 붙잡아 둘 수 있다(제한 해제 칸 고정, 0.2.1).**
   차가운 자물쇠(`BothSidesAreEmpty`)는 아티팩트보다 칸이 많을 때 양옆 두 칸을 비우는 자리로
   간다 - 빈 칸에는 값이 매겨져 있지 않아 그것이 공짜이기 때문이다. 점수로는 맞는 선택인데 런이
   진행돼 가방이 차면 그 공짜가 사라져 자물쇠가 자리를 옮기고, 사용자에게는 "돌릴 때마다
   바뀐다"로 보인다. 고칠 대상은 솔버가 아니라 취향을 말할 수단이 없다는 것이라, `F2` 창의
   `고정`이 그 아티팩트를 배치 조건을 무시하는 칸에만 앉힌다. 지정은 석판이 아니라 아티팩트
   쪽(종류 단위)에 두어 석판을 팔거나 갈아도 남고, 배정에서는 헝가리안 비용 행렬의 나머지 칸에
   큰 값(`PlacementSolver.HoldPenalty`)을 물려 막는다. 이 값은 이사 비용처럼 선택 기준에만
   들어가고 점수에는 안 섞인다. 그런 칸이 없으면 모든 칸이 같은 값을 물어 배정이 흔들리지 않고,
   `Arrangement.UnheldCharms`로 알린다. 지문에도 들어가 자동 배치 관문이 따라온다.
9. **효과가 같은 회전은 하나로 센다.** 쌍성("UPUP 2\nDOWNDOWN 2")처럼 180도 대칭인 석판이
   "회전 3 → 1" 같은 아무 일도 하지 않는 회전 지시를 실제로 받고 있었다. 탐색이 회전 4개를
   돌리기 전에 질의를 돌려 정규형(줄 정렬)으로 견주고, 지금 각도부터 세어 같은 효과의 회전을
   버린다(`PlacementSolver.DistinctRotations`). 탐색 폭도 그만큼 준다.

같은 녹화로 풀이 시간도 쟀다. **스냅샷당 평균 1713ms → 348ms.** 병목은 추천이 아니라 기본
빔 탐색의 최심부(`Estimate`)였다 - 호출(십수만 회)마다 낙관적 점유 해시셋 백여 건을 채우고
LINQ·정렬 리스트를 할당하고 있었다. 좌표 계산으로 답하는 `EstimateOccupancy` 와 레벨 분포
세기로 바꿨고, `TabletQuery.Parse` 결과 캐시(질의 x 칸 x 회전이 유한)와 `SimulationResult` 의
평면 배열화가 더해졌다. 남은 여지는 빔 확장의 증분 시뮬레이션(부모 결과 재사용)이다.

## 레벨 상한

아티팩트마다 `maxLevel`이 다르고, 그 위로 올라간 레벨은 효과에 반영되지 않는다. 실제 분포는
2에서 4가 대부분이다. 점수 계산은 `min(maxLevel, 레벨)`을 쓰므로 상한을 넘겨도 이득이 없지만,
넘긴다고 손해도 아니어서 탐색이 굳이 피하지도 않는다.

그래서 두 가지를 둔다. HUD는 칸의 레벨이 아니라 **그 아티팩트가 실제로 받는 레벨**을 보여주고,
남는 레벨이 있으면 색과 도움말로 알린다. 그리고 점수가 같은 배치 중에서는 덜 흘리는 쪽을 고르도록
아주 작은 차이를 준다.

## 인게임 UI (네이티브 패널)

오버레이 창 대신 게임 안에 직접 그릴 수 있는지 조사한 결과다. **가능하고, 붙을 자리가 전부
public 이다.**

- 게임 UI 는 **uGUI + TextMeshPro** 다 (`UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`).
- `UIManager.Instance.GetRootFromType(EUIObjectPoolingParent.HUD)`가 `UIRoot`를 돌려주고,
  `UIRoot.Canvas`/`CanvasGroup`이 public 이다. 그 밑에 GameObject 를 붙이면 그만이다.
  풀링 부모는 `None/HUD/DynamicHUD/World/AltWorld/GroundWorld` 여섯이다.
- **게임이 UI 를 감출 때 함께 감춰진다.** `UIManager.Hide()`가 하는 일이 `uiRoots` 각각의
  CanvasGroup 알파를 0 으로 만드는 것이라, 밑에 달려 있으면 딸려간다. 흉내 낼 필요가 없다.

### 창이 열리면 감춰지는 것이 아니라 덮인다

세피라이트 보상 창과 레벨업 창이 열리면 우리 화면이 안 보였다. **처음에는 위의 `Hide()`가
알파를 0 으로 만든 것으로 짐작했는데 틀렸다.** 보상 창을 열어 둔 채 인벤토리 덤프를 떠 보니
`root HUD alpha=1` 이고 `ours SephPlannerHud active=True alpha=1` 이었다. 감춰진 것이 아니라
**더 위 캔버스에 그려지는 창에 덮인 것**이다.

잰 값 - 화면 공간 캔버스의 정렬 순서다.

```
[UI] InteractableHUD  -2
[UI] DynamicHUD       -2
[UI] HUD               0     <- 우리가 달려 있던 곳
[UI] Panels            2     <- UI_SephiriteRewardPanel, UI_CharacterStatusPanel, UI_ItemIcon
[UI] System           10     <- 게임 자신의 툴팁·알림 (UI_HUDLogViewer, UI_NewItemPicker 등)
```

그래서 우리 것들을 중첩 캔버스(`overrideSorting`)로 올려 **Panels 위, System 아래**에 둔다
(`Widgets.Layer`, `Layers`). 부모의 `CanvasGroup` 은 그대로 상속되므로 `Hide()` 로 함께
감춰지는 성질은 유지된다. 누를 것이 있는 창에는 `GraphicRaycaster` 를 함께 올려야 한다 -
레이캐스트가 캔버스 단위라 부모의 것이 중첩 캔버스 안까지 훑지 않는다.

이 진단을 뜨는 것은 `UiDiagnostics` 이고 인벤토리 덤프(F10)의 `[ui]` 절로 나온다. 화면이 안
보이는 문제가 또 생기면 짐작하지 말고 창을 열어 둔 채 한 번 뜬다.

**화면 내용이 비면 Player.log 부터 본다.** `Update` 안에서 난 예외는 유니티가
`%USERPROFILE%\AppData\LocalLow\TEAMHORAY\Sephiria\Player.log` 에만 남기고 BepInEx
LogOutput.log 는 조용하다. 실제로 씬 전환 뒤 죽은 격자 셀을 재사용하는 버그가 매 프레임
NullReferenceException 을 3만 번 넘게 쌓는 동안(제목 줄만 나오고 격자·목록이 비는 증상)
우리 로그에는 아무것도 없었다. 지금은 그리기 예외를 잡아 같은 것 한 번씩 우리 로그에도 남긴다.
- **입력을 뺏지 않는 것도 구조가 보장한다.** 컨트롤 스택에는 `UIBase`를 단 것만 `AddControl`로
  들어가고(`UIManager.Awake`가 Awake 시점의 UIRoot 자식만 훑는다), ESC 처리도 그 스택을 탄다.
  `UIBase`를 상속하지 않고 그리는 것마다 `raycastTarget`을 끄면 키보드도 마우스도 통과한다.
- **글꼴은 게임에서 빌린다.** 씬의 `TMP_Text`에서 `font`와 `fontSharedMaterial`을 가져오면
  외곽선·그림자까지 같아진다. 픽셀 글꼴이라 재질이 다르면 흐릿해져 한눈에 티가 난다.

### 크기를 잴 때 우리 글자를 세면 안 된다

기준 크기는 HUD 글자 크기의 **중앙값**인데, 우리 화면도 그 HUD 아래에 있고 우리 글자는 전부
기준 크기의 배수(0.65~1.2배)다. 그대로 다시 재면 중앙값이 우리 쪽으로 끌려 내려가고, 그 값으로
또 만들면 다음 번엔 더 내려간다. **화면을 다시 지을 때마다 조금씩 작아졌다** - 폭을 바꾸면 다시
짓기 때문에 폭을 몇 번 바꾸는 것만으로 눈에 띄게 줄었다.

우리가 만든 것에 표시(`SephPlannerWidget`)를 달고 잴 때 걸러 낸다. 화면을 새로 짓는 순간에는
지우던 옛 화면이 아직 살아 있어(Destroy 가 프레임 끝에 돈다) 그것까지 함께 세어지므로, 이름이
아니라 표시로 거르는 것이 확실하다.

### 크기는 화면 픽셀이 아니라 게임 글자에서 온다

HUD 캔버스는 픽셀 아트라 크게 확대돼 있다. **화면 픽셀을 생각하고 숫자를 넣으면 그 배율만큼
어긋난다** — 폰트 13이 화면에서 50px 로, 폭 300이 1400px 로 나와 화면을 가로질렀다.

배율을 직접 읽어 나누는 대신, **HUD 글자 크기의 중앙값을 기준 단위로 삼고 나머지를 전부 그
비율로 잡는다**(`NativeSkin.BaseSize`). 게임이 UI 배율을 바꾸거나 해상도가 달라져도 따라간다.
중앙값을 쓰는 이유는 첫 번째로 찾은 글자가 제목처럼 유별나게 클 수 있기 때문이다.

### 판때기는 빌리지 않는다

처음에는 UIRoot 자식 중 `sprite.border != 0`(9-slice)인 `Image` 가운데 제일 큰 것을 창틀로
삼았다. **틀렸다** — 전체 화면짜리 초록 선택 테두리를 물어 왔고, 속이 비어 있어 글자가 게임
위에 그대로 떴다. 어느 스프라이트가 "창틀"인지 게임 데이터만으로는 가릴 방법이 없다.

색은 게임 패널에서 채집한 값을 `NativeSkin`에 두고 직접 그린다.
장미빛 테두리 한 겹과 어두운 속을 사용한다.

### 게임이 Galmuri 를 쓴다

`resources.assets`와 `sharedassets0.assets` 양쪽에서 문자열을 확인했다. HUD는 게임이 로드한
Galmuri 글꼴을 그대로 참조하므로 폰트 파일을 포함하거나 재배포하지 않는다.

### 에셋 방침과의 관계

씬에 이미 떠 있는 것을 런타임에 참조할 뿐 추출하지도 배포물에 넣지도 않으므로 `docs/LEGAL.md`
의 "게임 저작물 미배포"를 그대로 지킨다. WPF 제거와 함께 PNG 아이콘 덤프 코드도 제거했다.

### 솔버는 백그라운드 스레드로

`PlanBuilder.Build`는 빔 서치라 게임 루프에서 돌리면 프레임이 끊긴다. 넘겨도 되는 근거는
`SephPlanner.Core`가 유니티 객체를 건드리지 않는 순수 계산이고 `GameReader.Read`가 호출마다
새 스냅샷을 만든다는 것이다. `PlanRunner`가 한 번에 하나만 돌린다 - 폴링이 풀이보다 빠를 때
요청이 쌓이면 게임이 스레드에 잠식된다.

#### 스레드를 넘긴다고 프레임이 지켜지지는 않는다 (2026-09-03)

**위 근거는 CPU 만 보고 GC 를 보지 않았다.** 스레드를 넘겨도 힙은 하나이고, 유니티 Mono 의
GC 는 마킹하는 동안 관리 스레드를 전부 세운다. 이 게임은 증분 GC 를 켜 두었고
(`Sephiria_Data/boot.config`의 `gc-max-time-slice=3`) 그것은 **프레임마다 3ms 를 GC 에 준다는
뜻**이다. 60fps 예산 16.7ms 의 18%다. 할당 속도가 그 예산을 넘어서면 증분으로 따라잡지 못하고
전체를 멈추는 수집으로 떨어진다.

그러니 백그라운드 스레드에서 재야 할 것은 시간이 아니라 **할당량**이다. 실측(개발 PC, .NET 10)
에서 가방이 꽉 찬 채 상자를 연 계획 한 번이 **40GB** 를 할당했다. 어떤 GC 설정으로도 흡수되지
않는 양이고, 실제로 "설치하면 프레임이 매우 떨어진다"는 제보가 이것이었다.

원인은 알고리즘이 아니라 **조언의 정의**였다. "이 후보를 집으면 얼마나 좋아지나"를 판을 통째로
다시 풀어 답했고, 가방이 차면 밀려날 후보마다 그것을 반복했다 - 후보 8개에 탐색 288번.
그런데 **아티팩트 후보는 석판 배치를 하나도 건드리지 않는다.** 석판이 고정되면 아티팩트 배치는
배정 문제라 정확히, 그리고 싸게 풀린다. 42칸 판에서 재면 `Solve` 의 97%가 석판 탐색이고
배정은 3%(3ms 이하)다.

그래서 `PlacementSolver`를 `SearchLayouts`(탐색)와 `EvaluateLayouts`(채점)로 가르고,
같은 석판 구성이면 탐색을 돌려 쓰게 했다(`LayoutCache`). 갈래를 견줄 때는 배치 하나로 견주고
(`Yardstick`), 이긴 갈래만 배치 후보 전부로 다시 푼다. 수치는 ROADMAP.md "성능 여지" 절에 있다.

**바꾸기 전후로 조언을 견주어 확인했다.** 같은 스냅샷에서 후보 8개의 순서·증가분·밀려나는
항목이 소수점 넷째 자리까지 같았고, 합성 추천은 화면에 뜨는 상위 3개가 같았다(넷째 자리에서
+2.70 인 쌍이 +0.65 인 쌍에 밀렸다 - 짐작으로 줄을 세운 대가이며, 화면은 3줄이라 보이지 않는다).

### 게임 단축키

우리 단축키가 게임 조작을 함께 발동시키는 문제가 있었다. Ctrl+Alt+P 로 오버레이를 펼치면
게임의 재능 창이 같이 열렸다. **게임은 수정키를 보지 않고 글자 키만 읽으므로 Ctrl·Alt 를 붙여도
소용이 없고**, 전역 단축키(RegisterHotKey)가 조합을 가로채도 게임의 InputSystem 은 장치를 직접
읽어서 눌린 글자를 그대로 받는다.

`sharedassets0.assets` 의 InputActionAsset 바인딩을 뽑아 게임이 쓰는 키를 확정했다.

```
1 2 3 4 5 6 7 8   a b c d e f g p q r s v w x z
backquote slash space tab enter numpadEnter escape
leftArrow rightArrow upArrow downArrow   leftCtrl leftShift
```

**F 키는 하나도 쓰지 않는다.** 그래서 인게임 단축키는 F 키로 잡는다(F7 접고 펴기, F8 자동 배치,
F9 덤프, F10 인벤토리 덤프). 단축키를 새로 정할 일이 생기면 이 목록을 먼저 본다.

### 기본값을 옮겨도 설정 파일이 이긴다

단축키를 F 키로 옮겼는데 게임에서는 그대로 Ctrl+Alt+P 가 먹었다. **BepInEx 는 설정 파일에
저장된 값을 우선하므로 코드의 기본값만 바꾸면 이미 파일이 있는 사람에게는 아무 일도 일어나지
않는다.** 위치 설정에는 이 함정을 알고 열쇠 이름을 바꿔 두었으면서 단축키에는 같은 생각을
못 했다.

이름을 또 바꾸는 대신 **옛 기본값과 정확히 같을 때만 새 기본값으로 되돌린다**
(`SephPlannerPlugin.Retire`). 사용자가 손으로 정한 값은 건드리지 않는다. 그리고 설정된 키가
게임이 쓰는 키면 그 사실을 로그에 남긴다(`WarnIfGameKey`) - 손으로 겹치는 키를 넣었을 때
왜 이상한지 알 수 있어야 한다.

### 마우스는 새 InputSystem 으로만 읽힌다

키보드는 구식 `Input` 으로 읽힌다 - BepInEx 단축키가 그 길이고 F9/F10 이 잘 먹는다. **그런데
마우스는 죽어 있다.** 게임이 `<Mouse>/position`·`leftButton`·`scroll` 을 새 InputSystem 으로
바인딩하고 있어서, `Input.mousePosition` 은 커서가 한자리에 멈춰 있는 것처럼 돌려준다.
이동 모드가 "자리를 저장했다"고만 하고 화면이 따라오지 않던 원인이 이것이었다.

`UnityEngine.InputSystem.Mouse.current.position.ReadValue()` 를 쓰고 구식은 폴백으로 남긴다.
이동 중에는 커서 좌표를 화면에 그대로 띄워, 같은 증상이 다시 나면 커서를 못 읽는 것인지
자리가 안 먹는 것인지 로그 없이 갈린다.

### 설정은 우리 창에 담는다

설정(켜고 끄기·자리·크기·불투명도·후보 추천·단축키)은 **우리가 직접 그리는 창**에 있다
(`SettingsWindow`). 단축키로 열고 ESC 나 같은 키로 닫는다.

**이 창은 입력을 받는다.** HUD 화면의 무입력 원칙은 플레이 중 게임 조작을 방해하지 않기 위한
것인데, 이 창은 플레이어가 일부러 연 것이라 그 이유가 걸리지 않는다. 닫으면 그 자리에 아무것도
남지 않으므로 HUD 의 보장은 그대로다. 누를 수 있는 것을 만드는 손은 `Widgets.Clickable` 하나뿐이고,
거기서만 `raycastTarget` 이 켜진다.

### 게임의 컨트롤 스택에 올린다

창을 우리 손으로 관리하지 않는다. `PlannerPanel`이 게임의 **`UIBase`를 상속**해서
`ParentRoot.AddControl` 로 컨트롤 스택에 올라가면, 게임이 다음을 알아서 해 준다.

- **여는 동안 캐릭터 조작이 멈춘다.** 플레이어 입력 처리기가 이동·공격·핑을 전부
  `UIManager.Instance.CurrentControlStack == null` 일 때만 실행한다(디컴파일 전문 검색으로 확인).
  이것이 없으면 화살표를 누를 때마다 무기가 함께 휘둘러진다.
- **ESC 로 닫힌다.** `cancel` 액션이 스택을 위에서부터 훑어 `canCloseControlWithESC` 인 것에
  `CloseFromEsc()`를 부른다. 우리 창이 열려 있는 동안에는 ESC 가 일시정지 창을 열지도 않는다
  (그쪽은 `CurrentControlStack == null` 이 조건이다).
- **다른 창이 위에 열리면 우리 창이 알아서 비활성이 된다**(`Disable`이 CanvasGroup 의
  `interactable`/`blocksRaycasts` 를 끈다). 그래서 CanvasGroup 을 반드시 붙여 둔다.

`hasControl`, `canCloseControlWithESC`, `isPlayerUITHing`, `defaultSelectable`, `SetRoot` 이 전부
public 이라 상속만으로 된다. 컴포넌트는 **오브젝트를 꺼 둔 상태에서** 붙여야 한다 - 켜진 채로
붙이면 `UIBase.Awake` 가 CanvasGroup 을 잡기 전에 돌아 버린다.

주의: `isPlayerUITHing` 이 올리는 `UIManager.doingUIThingValue` 는 조작을 막지 않는다. 그 값은
`PlayerLocalDataStorage.doingSomeUIThings` 라는 **동기화 플래그**로만 쓰여 다른 플레이어에게
"메뉴 보는 중"을 알린다. 조작을 막는 것은 위의 컨트롤 스택 검사다.

### 창을 연다고 시간이 멈추지는 않는다

`Time.timeScale` 을 건드리는 곳은 **일시정지 창(`UI_PausePanel.OnOpened`)과 튜토리얼 팝업 몇
군데뿐**이다(`GameTimeManager.Pause`, 멀티에서는 스스로 아무것도 하지 않는다). 가방·옵션 같은
창은 시간을 멈추지 않는다 — 다만 게임에서 설정을 여는 길이 ESC 일시정지 창을 거치는 것이라,
설정을 만질 때는 아래에서 일시정지 창이 이미 멈춰 두고 있다.

그래서 우리 설정 창도 열릴 때 `Pause`, 닫힐 때 `ResetTimeScaleTo1` 을 부른다. 열 때 이미 멈춰
있으면(일시정지 창이나 팝업 위에서 열렸으면) 건드리지 않는다 - 우리가 닫으면서 남의 정지를 풀면
안 된다. 새로 주는 이득은 없다. ESC 로 언제든 멈출 수 있는 것이 게임 자신의 설계다.

### 게임 설정 창에 탭으로 붙이는 길은 접었다

한 번 만들어 붙여 봤고 동작까지 했지만 **생김새가 끝내 맞지 않아 걷어냈다.** 원인이 우리 코드가
아니라 창 자체의 구조라 다듬어서 해결될 문제가 아니었다. 같은 시도를 다시 하지 않도록 잰 값을
남긴다.

**창은 탭 다섯 개에 딱 맞게 짜여 있다.** 에셋에서 뽑은 수치다(프리팹 좌표).

```
OptionPanel
  Base                      489 x 280          창 그림 (OptionUi00)
    TabArea                 433 x 218
      Tab-GamePlay          420 x 218  ㄱ 탭 내용 다섯. 위쪽 끝이 탭 줄과 같은 높이다
      ...                              ㄴ 각자 다른 그림(OptionUi_Tab0~4)을 쓴다
      TabLabel              (가로 늘임, sizeDelta.x = -51) -> 382    탭 줄
        LB / RB             컨트롤러 안내 글리프. 줄 좌우 끝에 붙는다
        ...TabButton x5     각 74 폭, 간격 3
```

`5 x 74 + 4 x 3 = 382` — **다섯이 줄 폭을 한 치도 남기지 않고 채운다.** 그리고 `TabLabel` 에
`ContentSizeFitter` 가 붙어 있어 하나를 더 넣으면 줄이 늘어나고, 가운데 정렬이라 게임 탭 다섯이
통째로 왼쪽으로 밀린다.

**선택 표시인 흰 돌기가 탭 그림마다 박혀 있다.** `OptionUi_Tab0`(420x218)의 위 22px 띠에는
x 19~93 구간에만 그림이 있다 — 폭 74, 가운데가 -154 로 첫 탭 버튼과 정확히 겹친다. 색은 흰 테두리
`(236,236,244)` 와 남색 속 `(48,48,70)`. **탭 상자는 속이 비친 테두리라서**, 선택된 탭의 남색은
상자가 칠하는 것이 아니라 이 돌기가 비쳐 나오는 것이고 게임은 선택된 상자의 그림을 끄기만 한다
(`UI_OptionTabButton`).

여기서 막힌다. **버튼을 한 칸이라도 움직이면 게임 탭들이 제 돌기와 어긋난다**(여섯을 폭에 맞춰
고르게 나누면 최대 58 단위, 화면에서 300px 가까이). 그렇다고 게임 탭을 그대로 두면 우리 탭은
창 그림의 검은 안쪽(`OptionUi00` 기준 가운데에서 ±222.5)까지 남는 **28.5 단위**에 들어가야 하는데,
게임 탭(74)의 40% 도 안 되어 이름이 잘린다. 스크롤로 밀어 보는 길은 기본 상태에서 우리 탭이
보이지 않게 되어 설정 창으로서 못 쓴다.

곁다리로 얻은 사실 둘. `TabLabel` 은 `TabArea` 의 **마지막 자식**이라 탭 줄이 탭 내용들보다 위에
그려지고, `Instantiate` 한 새 자식은 맨 뒤에 붙으므로 그대로 두면 탭 줄을 덮는다. 그리고 프리팹에
박힌 `Button.onClick` 은 `RemoveAllListeners` 로 지워지지 않아
`SetPersistentListenerState(i, UnityEventCallState.Off)` 로 꺼야 한다.

**설정 한 줄의 구조는 그대로 쓸 만하다.** 게임의 한 줄은 `UI_HorizontalSelectionBox`(좌우로 고르는
Selectable)와, 고른 값을 게임 저장 키·로컬라이즈된 글자에 잇는 바인더
(`UI_OptionBox_Common_Integer`, `UI_HorizontalSelectionBox_Text`)로 되어 있다. 우리 창은 같은
모양(이름표 · < · 값 · >)을 우리 손으로 그린다.


### 누를 것이 없는 화면이다

`raycastTarget`을 끄면 마우스가 통과하는 대신 버튼도 못 만든다. 그래서 조작은 전부 단축키로
받는다 - 접고 펴기와 자동 배치가 BepInEx 설정의 `ExpandKey`/`AutoPlaceKey`다. 입력을 하나도
가져가지 않는 것이 게임을 방해하지 않는다는 보장의 근거이므로, 버튼을 들이려면 그 보장을
어떻게 지킬지부터 정해야 한다.

누르거나 입력해야 하는 기능은 플레이 중 HUD에 버튼을 넣지 않고 `F2` 빌드 창과 `F3` 설정 창으로
분리했다. 후보 미리보기는 `F1`, 툴팁은 커서 위치 판정으로 옮겼고 WPF 화면은 제거했다.

### 아직 확인 못 한 것

**게임을 켜서 봐야 답이 나오는 것들이다.** 설정 창이 화면 가운데에서 적당한 크기인지,
그리고 위의 프레임 수정이 실기에서 얼마나 체감되는지다. 측정은 .NET 10 데스크톱 런타임에서
한 것이라 유니티 Mono 에서는 절대값이 다르다 - 줄어든 배수는 옮겨가도 초 단위 값은 그대로
옮겨가지 않는다. `NativeHud`와 `SettingsWindow` 모두 무엇을 빌려 왔는지와 못 붙었다면 왜인지를
BepInEx 로그에 남긴다.

## 프리셋 코드 (커스텀 로드아웃 공유)

커뮤니티(디시 세피리아 갤러리, sephiria.wiki/builds)가 빌드 공략에 첨부하는
`AAF_PRESET_OBFZ|v1…` 코드의 정체다. 게임의 `UI_PresetPanel`이 클립보드로 내보내고 들여온다
(`CopyPresetToClipboard` / `PastePresetFromClipboard`). **빌드 인식 추천의 데이터 소스로 쓴다** —
작성자가 공유 목적으로 만든 게임 네이티브 포맷이라, 공략 사이트를 긁는 방식의 라이선스·파싱
문제가 전부 없다(docs/ROADMAP.md).

### 인코딩

```
"AAF_PRESET_OBFZ|v1" + Base64( XOR( GZip( 평문 ) ) )
```

XOR 키는 `"ActionAnimalFarmPresetShareKey"`의 UTF-8 바이트를 순환 적용한다. 디코딩은 게임
어셈블리 없이 재현 가능하다(`DeobfuscatePresetData` 디컴파일 확인).

### 평문 구조 (`BuildCompactPresetData`)

줄 단위 `접두사:값` 형식이고 첫 줄은 매직 `AAP1`이다.

| 줄 | 내용 |
|---|---|
| `W:` | 시작 무기 엔티티 ID |
| `C:` / `S:` | 코스튬 / 스킨 (URI 이스케이프) |
| `F:` | **즐겨찾기 아티팩트 엔티티 ID 목록** (쉼표 구분). `Item_Favorite_{id}` 저장 키에서 오며 Eternal 레어도와 Hidden 은 제외 |
| `P:` | 특성 포인트 `{id},{점수}` 목록 (세미콜론 구분, `PassiveDatabase`) |
| `D:` | 차원 주머니 내용물 `{instanceID},{entityID},{수량}` 목록 |
| `B:` | 과일 꼬치의 적응형 드롭 보너스 (기본 1이면 생략) |
| `R:` | **과일 꼬치 카테고리 성향** `{카테고리},{값}` 목록 |

`R:`의 카테고리 문자열은 게임 `ItemCategoryEntity.id`다(`UI_FruitSkewerPanel`이
`ItemDatabase.FindItemCategory`로 되찾는다). 즉 **콤보 식별자·아티팩트의 `Categories`와 같은
체계라 그대로 이어 쓸 수 있다.** 값은 부호가 있고, 같은 카테고리 과일을 여러 개 꽂으면 게임도
합쳐서 보여 준다 — 양수는 그 카테고리를 모으겠다는 뜻, 음수는 피하겠다는 뜻이다.

`F:`(이 빌드가 노리는 아티팩트)와 `R:`(카테고리 드롭 성향)이 빌드 정의의 핵심이다. 가져오기
쪽 검증은 `TryApplyCompactPresetData`가 하며, 소유하지 않은 코스튬 등은 기본값으로 보정한다.
