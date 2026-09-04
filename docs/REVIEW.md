# 전체 검토 보고 (2026-09-03)

> **후속 문서가 있다.** 0.1.3 코드가 다 들어간 뒤의 검토는
> [REVIEW-0.1.3.md](REVIEW-0.1.3.md) 다. 이 문서가 열어 둔 것 중 지금도 열려 있는 것은
> 그쪽 5절에 목록으로 정리돼 있다.

0.1.2 릴리스 직후, 커밋 `75ca1db` 기준으로 저장소 전체를 정독한 결과다. 무엇이 부족하고 무엇을
고치면 좋은지를 **안전 → 솔버 품질 → 성능 → UI/UX → 구조 → 테스트 → 빌드 → 도구·데이터 → 문서
→ 기능** 순으로 적고, 마지막에 실행 순서를 제안한다. 줄 번호는 검토 시점 기준이다.

읽은 범위: `src/` 전체(Core 약 6k줄, Plugin 약 6k줄), `tests/` 29파일 192개, `scripts/`, CI, 문서
일곱 편. 네 갈래(Core, Plugin 런타임, UI, 테스트·도구·빌드·문서)로 나눠 병렬로 읽고, 우선순위를
가르는 지적 여덟 개는 다시 코드로 확인했다. ROADMAP "알려진 한계"에 적힌 게임 일치용 특이점
(격자 폭 6 하드코딩, MUL 덧셈 누적 등)은 결함으로 세지 않았다.

## 총평

**튼튼한 곳.** 쓰기 경로는 세 겹(정책 → 재읽기 → 적용 직전 재검증)이고 한 프레임 안에서 끝나
낡은 계획이 끼어들 틈이 없다. 숨은 정보 규칙은 실시간 읽기 경로에서 정확히 지켜진다. 결정마다
이유가 문서에 남아 있고, 테스트는 구현이 아니라 동작을 잡는다. 성능은 짐작 대신 실측으로 고쳤다.

**약한 곳.** 다섯 가지가 두드러진다.

1. **카탈로그 갱신이 실패하면 그 세션은 끝이다.** F9 를 누르는 순간 이전 카탈로그가 무효가 되고,
   실패하면 되살릴 길도 재시도도 없다. 사용자가 겪을 수 있는 가장 큰 구멍이다.
2. **솔버가 동점에서 흔들릴 여지가 세 군데 남았다.** 꺼진 아티팩트에 자리 유지 몫이 없고, 수렴
   반복이 마지막 결과를 돌려주며, 검증이 실패한 세션에서는 앵커가 비어 있다.
3. **메인 스레드에 매 프레임 버리는 문자열이 있다.** 안내 줄, 레이아웃 서명, 지문 세 번 계산.
   프레임 예산 안이지만 GC 3ms 규칙과 어긋나는 습관이다.
4. **접힌 HUD 의 안내 줄이 판 밖으로 흘러넘치고, 경고가 한 줄만 보인다.** 멀티 세션 안내가
   사실상 보이지 않는다.
5. **F10 덤프와 `--values` 초안이 원칙을 어긴다.** 덤프는 안 보이는 세피라이트의 보상을 적고,
   초안은 게임 효과 문장을 `note` 에 미리 채워 그대로 두면 DLL 에 실려 나간다.

## 우선순위 요약

| | 항목 | 어디 | 크기 |
|---|---|---|---|
| **P0** | 갱신 실패가 활성 카탈로그를 죽이는 것 | `CatalogBundleStore.cs:100-147`, `CatalogDump.cs:33-53` | 중 |
| **P0** | F10 덤프의 숨은 정보 | `InventoryDiagnostics.cs:27-56` | 소 |
| **P0** | `--values` 초안의 게임 문장 | `CharmValueDraft.cs:111` | 소 |
| **P1** | 꺼진 아티팩트의 자리 유지 몫 | `PlacementSolver.cs:495` | 소 |
| **P1** | 수렴 반복의 최선 유지 | `PlacementSolver.cs:424-437` | 소 |
| **P1** | 접힌 안내 줄 넘침, 경고 한 줄 | `NativeHud.cs:467,887 / :277,599` | 소 |
| **P1** | 되돌리기 검증과 적용 후 대조 | `PlanApplier.cs:217-234, 64-71` | 소 |
| **P1** | 매 프레임 문자열(안내·서명·State) | `Plugin.cs:394,411,447,488-505,636-684` | 중 |
| **P1** | 지문 세 번 계산 | `PlanRunner.cs:104-106`, `PlanFingerprint.cs` | 소 |
| **P1** | 타이틀 화면의 `FindLocalPlayer` 폴백 | `GameReader.cs:78-93` | 소 |
| **P2** | 빔 확장 할당 재작성 | `PlacementSolver.cs:305-331` | 대 |
| **P2** | 버전 단일화·재현 빌드·MVID | `Directory.Build.props`, `make-release.ps1:53` | 소 |
| **P2** | 자체 호스팅 CI 러너 | `.github/workflows/ci.yml` | 소 |
| **P2** | `--check/--replay` 의 입력이 없음 | `SnapshotCheck.cs`, `README.md:51-56` | 소 |
| **P2** | 문서 재편과 낡은 곳 다섯 | 아래 "문서" | 중 |

## 1. 안전과 정확성

### 카탈로그 갱신이 활성 카탈로그를 먼저 무효로 만든다 (P0)

`CatalogBundleStore.Begin` 이 `catalog-refresh-state` 를 곧바로 `Refreshing` 으로 쓰고,
`TryGetActiveCore` 는 상태가 `Ready` 가 아니면 포인터를 읽지도 않고 실패한다. 그래서 F9 를
누르는 순간(또는 패치 뒤 부팅 자동 덤프가 시작되는 순간) 디스크의 멀쩡한 이전 세대가 쓸 수 없게
되고, 갱신이 실패하면 - 로컬라이제이션 30초 대기 초과(`Plugin.cs:196-208`), `WriteRoutine` 예외,
도중 크래시 - `HasCatalog()` 가 거짓이 되어 `FeedNativePanel` 이 `:335` 에서 영영 물러선다.
`_runner` 는 `:194` 에서 비워진 채 남고, `_catalogChecked` 는 일회성이라 재시도도 없다. 화면은
"데이터 준비 중"만 보여 준다(실패는 로그에만 남는다).

고치는 법. 상태 파일은 **새 세대**의 진행 상태만 뜻하게 하고, `TryGetActive` 는
`active-catalog.txt` 포인터와 그 세대의 manifest 만 믿는다. 포인터는 `Publish` 에서만 바꾼다.
그러면 실패한 갱신은 "이전 카탈로그가 그대로 활성"으로 물러선다. 다음 런 시작 때 자동 재시도를
한 번 걸고, `Publish` 뒤에 비활성 세대 디렉터리를 정리한다(지금은 무한히 쌓인다). 이 상태 기계는
순수 로직이라 Core 로 옮기면 `CatalogBundleStoreTests` 가 그대로 잡는다.

### 되돌리기가 되돌아갔는지 보지 않는다 (P1)

`PlanApplier.Rollback` 은 `Swap` 을 부르고 결과를 확인하지 않는다. 앞으로 가는 길(`:178`)은
"게임의 `LocalSwap` 은 거부해도 예외 없이 돌아온다"는 이유로 칸을 확인하는데, 역방향은 그
확인이 없어 거부된 되돌리기가 "원래 배치로 되돌렸습니다"로 보고될 수 있다. 저널에 인스턴스
번호를 함께 적고 되돌린 뒤 `InstanceAt` 으로 확인한다.

같은 자리에 하나 더. 회전까지 끝난 뒤 게임의 `levelMatrix` 를 `plan.Best.AllLevels` 와 견주는
사후 대조가 없다. 되돌릴 일은 아니고(상태는 합법이다) 경고 로그면 된다. 게임 규칙이 바뀐 것을
다음 폴링이 아니라 첫 적용에서 잡는 유일한 신호다.

### F10 덤프가 안 보이는 것을 적는다 (P0)

`InventoryDiagnostics.WriteOffers` 가 씬의 **모든** 세피라이트의 보상 `entityID` 를 적고, 열지
않은 상자의 아이템 수를 적는다. 레벨업 세피라이트는 창을 열기 전에 미리 생성된다는 것을
RESEARCH 가 직접 밝혔으므로, 이 파일은 플레이 중 열어 볼 수 있는 숨은 정보다. TEAM HORAY 에
보낸 문구("화면에서 볼 수 없는 정보는 보여주지 않습니다")와 어긋난다. 보상 번호는 지금 보이는
세피라이트(`UI_SephiriteRewardPanel.sephirite`)에만 적고, 나머지는 `gen/acq/n` 플래그만 남긴다.
인벤토리 `cells=` 도 `OfferReader` 의 보임 판정을 노출해 보일 때만 적는다. BepInEx 로그의
세피라이트 보고에 있는 `n=` 도 `shown=False` 면 뺀다.

### `--values` 초안이 게임 문장을 `note` 에 넣는다 (P0)

`CharmValueDraft.cs:111` 이 `Note = string.Join(" / ", charm.EffectLines)` 로 초안을 만든다.
README 는 바꾸라고 하지만, 한 항목이라도 그대로 남으면 `charms.json` 이 DLL 에 임베드되어
(`Plugin.csproj:23`) 게임 문장이 배포물에 실린다. LEGAL.md "게임 저작물 미배포" 위반이다.
효과 문장은 `CharmValueEntry` 에 없는 필드(`effect`)로 내보내 두 역직렬화기가 모두 무시하게 하고,
아래 "테스트"의 `charms.json` 검사에서 `note == effect` 인 항목을 거부한다.

### 그 밖에

- `IsOnGrid` 가 세 곳에 따로 있고 `GameReader.cs:274` 것만 storage 경계를 보지 않는다.
  `GridSpec.Contains(GridPos)` 하나로 모은다(`PlanBuilder.cs:460`, `ApplyPlanValidator.cs:73`,
  `PlanApplier.cs:316`, `TabletEffectSummary.cs:83` 까지 다섯 곳).
- `PlanBuilder.cs:76-91` 석판은 `IsOnGrid` 로 거르지 않고 아이템만 거른다. 지금은 닿지 않는
  길이지만 비대칭이 함정이다.
- `PlanBuilder.cs:657-660` `HasPlacementChanges` 를 `targets.Clear()` 전에 계산해, 미배치 석판이
  있을 때 "옮길 것이 없습니다"라는 엉뚱한 사유가 나온다. 대입을 clear 뒤로 옮긴다.
- `TabletQuery.Parse` 가 캐시된 `List<QueryCell>` 을 그대로 돌려준다. 호출자가 고치면 이후 모든
  시뮬레이션이 오염된다. `IReadOnlyList` 로 바꾼다.
- `LayoutCache.Of` 가 취소된 토큰으로 중단된 불완전한 빔을 캐시한다. 지금은 빌드마다 새 캐시라
  안전하지만 캐시를 밖으로 끌어올리는 순간 터진다. 취소됐으면 저장하지 않는다.
- `PlacementSolver.cs:328` 이 LINQ `OrderByDescending` 의 **안정 정렬**에 기대어 동점을 삽입
  순서로 가른다. 계획 안정성의 근거인데 주석이 없다. 명시적 2차 키를 둔다.

## 2. 솔버 품질

- **꺼진 아티팩트에 자리 유지 몫이 없다** (`PlacementSolver.cs:495`). `Value` 가 이유가 있으면
  `0` 을 돌려주고 `Anchors` 를 건너뛴다. 필러는 같은 이유로 `Anchors` 를 받게 해 두었으면서
  (바로 윗줄) 무기 불일치 아티팩트는 전 칸이 0점 동률이라 헝가리안이 빈 낮은 번호 칸으로 보낸다.
  이득 0 이동 제안이 그대로 살아남는다. `return Anchors(...)` 한 줄이면 된다. 고정 테스트 필요.
- **수렴 반복이 마지막 반복을 돌려준다** (`:424-437`). 조건부 아티팩트는 배정과 점유가 2주기로
  진동할 수 있고 3회 반복은 나쁜 쪽 위상에서 멈출 수 있다. `PlanBuilder` 는 "지금보다 나쁜가"만
  막고 "1회차보다 나쁜가"는 못 막는다. 반복마다 `Describe` 하고 최선을 들고 있는다. 후보·합성
  증가분에도 같은 왜곡이 들어간다.
- ~~**같은 석판이 여럿이면 빔이 순열 중복으로 낭비된다**~~ (`:286-334`). 같은 정의·질의·회전
  가능성의 슬롯은 순열마다 같은 추정치를 내므로 빔 400 자리를 나눠 먹는다. 앞 슬롯보다 큰 칸
  번호만 허용하는 대칭 제거를 넣는다.
  **넣었다**(`Twins`, 2026-09-04). 겹치는 판에서 점수는 그대로이고 풀이가 20~36% 빨라졌다
  (3종 9장 420ms → 191ms). 여기를 파다가 더 큰 것 둘을 찾았다 - 어림값이 아티팩트 값어치를
  모르던 것과, 빔을 자를 때 한 부모가 다 가져가던 것. 셋 다 같은 병이다: **1단계가 2단계와
  다른 것을 재고 있었다.** `PlacementSolver` 클래스 주석과 RESEARCH.md 에 재어 본 값과 함께
  적어 두었다.
- **`OfferAdvisor.Without` 이 약속한 완전성 검사를 하지 않는다** (`:447-458`). 주석은 "온전하지
  않으면 만들지 않는다"인데 코드는 `index < layout.Count` 만 본다. 기준 배치가 불완전하면
  `Describe` 에서 위치와 슬롯이 어긋나 엉뚱한 갈래 점수가 나온다. 기대 개수를 넘겨 검사한다.
- **석판 제거 갈래의 잣대가 아티팩트 밀어내기 쪽으로 기운다** (`:350-359`). "갈래를 가르는
  것은 배치가 아니다"는 석판 집합이 같은 갈래에만 맞다. 석판 하나를 빼면 그 칸이 비고 최선
  배치가 달라지는데 잣대는 "유지 배치에서 그것만 뺀 것" 하나다. 후보 석판을 빠진 칸에 넣은
  회전별 배치를 잣대에 하나 더 얹으면 탐색 없이 상당 부분 보정된다.
- **앵커가 `previous.Targets` 에서 나온다** (`PlanBuilder.cs:147-156`). 검증 실패나 미배치 석판이
  있으면 `Targets` 가 비워지므로(`:660`) 그 세션 내내 앵커가 없다. 클라이언트 세션(고정 각인을
  못 읽어 늘 불일치)이 정확히 그 경우다. `previous.Best.TabletPositions/CharmPositions` 에서
  뽑는다.
- `Estimate` 의 낙관(빈 칸은 전부 켜진 아티팩트, 상한·값어치 무시)은 문서화된 어림값이라
  결함은 아니지만, `EstimateStats` 가 이미 `levelCap` 을 세므로 상한별 개수 배열 하나면 싸게
  나아진다.
- 콤보 모델은 일관되다. `ComboProgress` 가 남은 걸음 수와 무관한 것과 `WhitePaperWorth` 가
  스냅샷 개수를 쓰는 것은 근사이지 오류가 아니다(ROADMAP 백로그와 같다).

## 3. 성능

측정된 자리(빔 탐색 할당, 폴링 씬 탐색)는 고쳤고, 남은 것은 **작지만 매 프레임 도는 것들**과
**아직 재지 않은 자리** 둘이다.

### 메인 스레드

- `Plugin.cs:394,411` `_settings.LayoutSignature` 가 float 다섯 개를 보간한 문자열이고 프레임마다
  두 번 만든다. 144fps 면 초당 40KB 쓰레기다. `SettingChanged` 로 무효화하는 캐시로 바꾼다.
- `Plugin.cs:447,488-505,636-684` 펼친 상태에서 매 프레임 `_runner.State`(잠금 안 할당) 두 번,
  `Hint()→Guide()` 열 개 남짓 연결, `AutoPlaceContext`, `HudFrame`. `NativeHud.Render` 가 결과
  문자열로 중복을 거르므로 만들었다 버린다. 입력(`_moving`, `_expanded`, `_previewKey`, 보고
  만료, 게시 세대, 키 바인딩)이 바뀔 때만 다시 만든다.
- `Plugin.cs:639-643` 이동 중에는 안내 줄에 커서 좌표가 들어가 `FrameKey` 가 매 프레임 달라지고
  격자·목록이 통째로 다시 그려진다. 좌표를 빼거나 안내 줄만 키 밖에서 갱신한다.
- `PlanRunner.Submit` 이 지문 셋을 겹쳐 계산한다(`PlanningContext` 3회, `Placement` 2회). 플러그인
  쪽 `Plugin.cs:355-359` 까지 합치면 폴링마다 `Placement` 두 번이다. `PlanningContext` 는
  `(prefs.Revision, catalogGeneration)` 으로만 바뀌니 캐시하고, `Submit` 이 계산한 지문을
  돌려준다. `PluginPreferences.ToPreferences` 도 폴링마다 `HashSet` 셋을 만든다 - `Revision` 으로
  캐시. 측정된 "지문 계산과 제출" 0.62ms 의 대부분이 여기다.
- `GameReader.FindLocalPlayer` 가 `localPlayer` 가 없으면 `FindObjectsByType<PlayerAvatar>` 로
  물러서고, 폴링마다 두 번(`Read`, `CheckSimulation`) 불린다. **타이틀·로비·씬 전환 중이 정확히
  그 상태**라 기준선 세션(런 안)이 못 본 비용이다. `!NetworkClient.active` 면 즉시 null, 폴백은
  `SceneCache`, 아바타는 폴링당 한 번 구해 둘에 넘긴다. `FrameCost` 구간을 하나 더 둔다.
- `PollIntervalSeconds` 하한 0.05 는 초당 20회이고, `Mathf.Max(0.05f, NaN)` 은 NaN 이라 설정에
  NaN 이 들어가면 매 프레임 폴링한다. `AcceptableValueRange<float>(0.1f, 5f)` 로 바인드하면
  BepInEx 가 잘라 주고 설명도 써 준다. 다른 숫자 설정(불투명도, 배율, 폭, 반경)도 범위가 없다.
- `NetworkClient.spawned` 를 도는 것이 `FindObjectsByType` 을 대신할 수 있다. `GridInventory` 와
  `Sephirite` 는 `NetworkBehaviour` 라 전부 거기 있고 비활성도 포함되며, 새로 떨어진 꾸러미의
  1초 지연도 없어진다. `FrameCost` 로 재고 나서 바꾼다(ROADMAP 성능 4번을 닫는 길).
- `Panel` 이 꺼져 있어도 폴링이 상자 탐색·시뮬레이터 대조를 다 돈다. `BuildWindow` 만
  스냅샷을 쓰므로 그때는 추천과 대조를 건너뛴다.
- `SyncDictionary<ItemPosition,int>` 를 셀·아이템마다 선형으로 훑는다(`GameReader.cs:277`,
  `SimulationVerifier.cs:103`, `PlanApplier.cs:203`). 키가 `ItemPosition` 이므로 `TryGetValue` 가
  된다. `CellKey` 는 폴링마다 문자열 126개를 만든다 - 42개를 한 번 인턴한다.

### 백그라운드 (GC)

- **빔 확장이 확장마다 대여섯 개를 할당하고 가지치기 전에 전부 실체화한다**
  (`PlacementSolver.cs:305-331`, `Estimate:383-411`). 석판 한 단계에 최대 400×42×4 = 67,200 확장,
  각각 `List<TabletPlacement>` 복사, `TabletPlacement`, `EstimateOccupancy`+`bool[]`,
  `SimulationResult`(int[]×4+bool[]), `int[levelCap+1]`, 각인이 있으면 `WithFixed` 복사. 확장당
  1~1.5KB, 단계당 80~100MB 가 정렬이 끝날 때까지 살아 있다. ROADMAP "빔 탐색 내부의 할당"이
  이것이다. 순서대로: 확장을 `(parent, cell, rotation, score)` 구조체 배열로 두고 상위 400만
  실체화(리스트 할당 168분의 1), `SimulationResult`·`EstimateOccupancy` 를 탐색당 하나로 재사용
  (`Reset`), `FixedTablets` 를 `TabletSimulator.Run` 의 둘째 인자로 넘겨 `WithFixed` 복사 제거.
- `HungarianAssignment.cs:34-36` `minimum`·`used` 배열을 행마다 새로 만든다. 42행이면 풀이당
  17KB, `Solve` 한 번에 5MB. 루프 밖으로 올리고 `Array.Fill`.
- `Evaluate` 가 배치마다 HashSet 셋 + `OptimisticOccupancy`(HashSet 셋 더) + LINQ `Any`. 이미
  좌표 계산으로 답하는 `EstimateOccupancy` 를 여기서도 쓴다.
- `TabletQuery.Parse` 캐시 조회가 질의 문자열 전체를 해시한다(단계당 13만 번). 슬롯에
  회전별 파싱 결과 `QueryCell[][4]` 를 들고 있으면 사전이 핫패스에서 사라진다.
- `OfferAdvisor.cs:347-359` 가방이 안 찼을 때는 갈래가 하나인데 `Yardstick` 을 돌고 다시
  `EvaluateLayouts` 를 돈다. 갈래가 하나면 잣대를 건너뛴다.

## 4. UI/UX

플레이어가 보는 것을 코드에서 재구성하고 평가했다.

### 고쳐야 하는 것

- **접힌 안내 줄이 판 밖으로 넘친다** (`NativeHud.cs:467` vs `:887`). `FitHeight` 가 펼친 폭
  `_inner` 로 높이를 재는데 `SetCompact` 는 폭을 `min(_width, S(18))` 로 줄인다. 8키 안내
  (70자 남짓)가 18배 폭에서 서너 줄로 접히면서 할당된 높이를 넘어 게임 화면 위로 흘러내린다.
  ROADMAP "접힌 HUD 의 폭" 지적의 실체는 폭이 아니라 **세로 넘침**이다. 접힌 안쪽 폭을 따로
  계산해 넘긴다.
- **경고가 한 줄만 보인다** (`:277,599`). `Warning()` 은 `\n` 으로 잇는데 `_notice` 는 `Line`
  (줄바꿈 없음, 말줄임, 1.4배 고정 높이)이라 첫 경고만 보인다. 다른 경고가 하나라도 있으면
  "멀티플레이 세션 - 제안만 표시합니다"는 영영 안 보인다. `Paragraph` + `FitHeight`.
- **그리기 예외가 반쪽 화면을 남긴다** (`:456`). `_drawn = key` 를 그리기 전에 대입해서 예외가
  나면 다음 프레임에 키가 같아 재시도하지 않는다. 마지막 `Render*` 뒤에 대입하거나 catch 에서
  `_hasDrawn = false`.
- **창이 열린 채 파괴되면 시간이 멈춘 채 남는다** (`PlannerWindow.cs:196-215`). `OnOpened` 에서
  멈추고 `OnClosed` 에서만 푼다. `Destroy()` 나 씬 언로드는 `Close` 를 거치지 않는다.
  `OnDestroy` 에서 되돌린다.
- **덤프 실패가 화면에 닿지 않는다.** 위 P0 과 같은 자리. `Waiting()` 에 `Failed` 분기를 두고
  "데이터 생성 실패 - F9 로 다시 시도(로그 참조)"를 띄운다.
- `"F9"` 가 네 곳(`NativeHud.cs:578`, `AutoPlacePolicy.cs:48`, `PlanApplier.cs:29`,
  `Plugin.cs:202`)에 글자로 박혀 있다. 키는 바꿀 수 있고 `Waiting()` 은 `Describe(DumpKey)` 를
  쓴다. 키 이름을 넘기거나 자리표시자를 둔다.
- 창(`PlannerWindow.cs:105`)은 `Scale` 설정을 무시하고 HUD 와 쪽지는 따른다.
- 쪽지 첫 프레임: `SetActive(true)` 가 텍스트 갱신보다 먼저라 0 높이 상자가 한 프레임 보일 수
  있다. `Place` 뒤에 켠다.

### 더 낫게 만들 수 있는 것 (가치/노력 순)

1. **접혔을 때 키 단서가 없다.** 접힌 상태가 기본 플레이 상태인데 안내는 처음과 키를 누른 뒤
   6초뿐이다. 키를 하나는 알아야 안내를 되찾는다. F4 로 숨기면 되돌리는 키가 로그에만 남는다.
   점수 줄 끝에 0.65배로 `F7 펼치기 · F3 설정` 을 늘 두고, F4 는 숨기기 직전 3초간 "F4 로 다시
   보기"를 띄운다. F9/F10 은 게임 안에서 한 번도 안 보인다 - 설정 창 단축키 목록에 있으니 거기서
   충분하지만, 첫 안내에 한 번은 넣을 만하다.
2. **F8 결과가 안내 줄과 구별되지 않는다.** 6초 0.75배 흐린 회색이라 거절 사유가 오류처럼
   보이지 않고, 다른 키를 누르면 덮인다. 결과별로 색(성공/주의/실패)을 주고 실패는 10초, 만료
   전에는 F7/F4 가 덮지 않게 한다.
3. **색으로만 갈리는 신호 셋.** 콤보 완성(초록)/상실(빨강)/진행(민트), 상한 초과 레벨(주황),
   못 사는 가격(빨강). 초록·빨강은 적녹색약의 고전 쌍이다. `▲ 7/8`, `▼`, `(상한)` 같은 글자
   표식을 곁들인다.
4. **"데이터 준비 중"에 진행이 없다.** 카탈로그 대기 30초와 프레임에 나눈 질의 검증 동안
   정적 문구뿐이다. 코루틴이 검증한 석판 수 `N/68` 을 넘기면 된다.
5. **펼친 화면에 최대 높이가 없다.** 6줄 격자 기준 45~50 기준단위. 배율 130% 이상이거나 가방이
   크면 아래로 나간다(`ClampToCanvas` 는 이동·표시 때만). F8 이 가능할 때는 "옮길 것" 절을
   접는 것만으로도 크게 준다.
6. **글자 크기 비율.** 0.65/0.75/0.8/0.95 배는 픽셀 글꼴(Galmuri)에서 정수 배가 아니면 번진다.
   0.65배 칸 이름과 0.75배 안내가 80% 배율에서 먼저 읽기 어려워진다. 정수 배로 맞출 수 있는지
   본다.
7. **언어.** 아이템·콤보 이름은 게임 언어를 따르는데 우리 문장 80여 개는 한국어다. 게임이
   15개 언어를 싣고 있으니 게임 언어 키로 ko/en 표 하나를 두는 것이 "게임에서 빌린다"는 방침에
   맞다. 크기가 커서 뒤로 미룰 만하다.
8. INSTALL.txt 1번("처음 한 번은 F9")은 낡았다. `Plugin.cs:157` 이 카탈로그가 없으면 자동으로
   덤프한다. 해롭지는 않지만 맞춘다.

### 잘 된 것

쪽지 자리 잡기(양쪽 뒤집기와 최종 클램프)는 맞다. 파괴된 유니티 객체 처리는 전 지점이
유니티 `==` 를 쓰고 `Build()`/`Cleared()` 가 셀과 줄을 비워, NRE 3만 건 사건의 수리가 제대로
들어가 있다. 위젯 풀링도 되어 있어 렌더당 할당은 `RenderGrid` 의 사전·해시셋 둘뿐이다.

## 5. 코드 품질과 구조

### 플러그인을 쪼개서 Core 로

`Plugin.cs`(822줄)에 여덟 책임이 섞여 있다: 단축키 분기, HUD 수명, 안내 문장, 미리보기 순환,
카탈로그 코루틴, 폴링과 비용 보고, 자동 배치, 이동 모드. 필드가 파일 중간(`:92`, `:161`)에도
선언된다. CI 가 플러그인을 못 짓는 구멍을 좁히는 것이 ROADMAP 의 해법이므로 **게임 타입이 없는
것부터 Core 로** 옮긴다. 순서는 CI 이득 순이다.

1. `FrameCost` - `Stopwatch` 뿐이다. `Write` 가 `CatalogSource.Attempts` 를 참조하는 것만 인자로.
2. `SessionKind.Of(clientActive, serverActive, connectionCount)` - 호스트/클라이언트/솔로 행렬이
   테스트된다.
3. **카탈로그 갱신 상태 기계** - `CatalogDump.cs:33-53,93-98,134-151` 의 전이는 순수하다. 위 P0
   이 사는 자리인데 지금은 테스트가 없다.
4. `PreviewCycler`(`Plugin.cs:539-584`), `GuideText`/`HintComposer`(`:636-684`), `AutoExpand`
   (`:623-630`), `Waiting()` 매핑(`:515-533`).
5. 남는 유니티 접착제를 `CatalogRefreshCoordinator`, `PlannerSession`, `HudController` 셋으로.
6. `SimulationVerifier.CompareMatrices/CompareEffects` 를 `LevelMatrixComparer` 로 Core 에 -
   `DataTool --check` 와 같은 메시지 형식을 쓰게 된다. `ItemCatalog.IdFromKey`, `OfferReader.KindOf`
   도 순수하다.

`PluginPreferences` 는 Core 에 Newtonsoft 참조가 없어 그대로는 못 옮긴다. 모델과 `ToPreferences`,
`TryImport` 만 옮기고 직렬화는 플러그인에 둔다.

### 중복

- `Faster()` 가 `OfferAdvisor.cs:133` 과 `TabletMixAdvisor.cs:50` 에 따로 있다. 둘이 한
  `LayoutCache` 를 나눠 쓰고 키에 옵션 값이 들어가므로, 한쪽만 손대면 캐시가 조용히 갈라져
  후보와 합성이 다른 배치 위에서 겨루게 된다. `SolverOptions.ForAdvice()` 하나로.
- 문제 복제 두 벌: `OfferAdvisor.Clone`(485-505)과 `TabletMixAdvisor.Without`(274-299).
  `PlacementProblem.Clone(keepTablet, keepCharm)`.
- `NativeHud.Reach()`(792-800)는 `Explain.Reach` 의 복사본이다. 지운다.
- 판 뼈대(테두리 → 안쪽 → 패딩)가 `NativeHud`, `PlannerWindow`, `Tooltip` 세 곳에, 화살표 버튼과
  "줄 + 늘어나는 이름표 + 고정 상세"가 세 곳에 각각 다른 높이(1.5/1.4/1.2)로 있다.
  `Widgets.Panel`, `Widgets.LabeledRow`, `PlannerWindow.Arrow`.
- `NativeSkin` 은 색만 모았고 치수는 40개 남짓 리터럴로 흩어져 있다. `NativeSkin.Metrics`.
  `Tint` 가 부를 때마다 `ColorUtility.ToHtmlStringRGB` 를 돈다 - 미리 구워 둔다.
- `Clamp(scale, 0.8, 1.5)`, `Clamp(width, 18, 36)`, `S(18)` 이 `PluginSettings` 의
  `ScaleSteps/WidthSteps` 를 다시 말한다. 설정 쪽이 경계를 내보낸다.
- 요약 주석이 두 개 겹친 곳: `PlacementSolver.cs:336-343`, `GameReader.cs:101-112`,
  `CatalogSource.cs:21-25`. `SearchTabletLayouts:300` 이 `CurrentRotation()` 을 다시 구현한다.

### 공개 표면과 불변식

- `PlacementSolver.SearchLayouts/EvaluateLayouts`, `LayoutCache`, `HungarianAssignment`,
  `GridOccupancy`, `QueryCell` 의 가변 필드, `PlanRunState` 의 setter 가 public 이다. 테스트가
  `LayoutCache.Searches` 를 쓰므로 `InternalsVisibleTo("SephPlanner.Tests")` 를 걸고 솔버 배관을
  internal 로 내린다. 플러그인 검증기가 쓰는 `Solve/Score`, `TabletQuery.Parse/Rotated`,
  `TabletSimulator.Run` 은 public 으로 남긴다.
- `WastePenalty 1e-4 > StabilityBonus 1e-6 > 42×PlanBonus` 순서는 문서화됐지만 아무것도
  단언하지 않는다. 정적 생성자 `Debug.Assert` 나 테스트 하나.
- `Plugin.csproj` 가 `<Nullable>disable</Nullable>` 이라 Core 의 비-null 매개변수에 null 을
  넘겨도 경고가 없다. `annotations` 로만 올려도 검사가 붙는다.
- `UnityEngine.ImageConversionModule` 참조는 아이콘 덤프 제거 뒤 쓰는 곳이 없다.
  `BepInProcess("Sephiria.exe")` 를 달면 다른 게임에서 로드되지 않는다.
- `PluginSettings` 의 `entry.Value = x` 마다 동기 `Save()` 가 돈다(`ChangeCorner` 4회,
  `SavePanelMargin` 3회). `SaveOnConfigSet` 을 끄고 한 번에 저장한다. `PluginPreferences.Save` 는
  예외를 조용히 삼킨다 - 세션당 한 번은 로그. 고아가 된 `MultiplayerAutoPlace` 키도 `Retire`.
- `PinnedCharms`/`PriorityCategories` 가 카탈로그와 대조되지 않아 낡은 번호가 영원히 남는다.

### 로그와 진단

- 부팅 줄이 정체를 싣지 않는다. 플러그인 버전, `Application.version`, MVID, BepInEx 버전, 활성
  카탈로그 세대와 검증 여부를 한 줄로. 제보마다 필요한데 F10 덤프에도 없다.
- `Plugin.cs:285` 스냅샷 실패가 5초마다 전체 스택으로 남는다. 패치로 멤버가 사라지면 세션 내내
  스팸이다. 종류+메시지로 중복 제거, 60초까지 지수 백오프, "이 빌드는 게임 X 기준" 한 줄.
- `FrameCost.Summary()`(5분 로그)에 `Catalog` 횟수·합계와 `Attempts`, `Poll.Count` 가 없다.
  ROADMAP 이 진입 지연 진단에 필요하다고 한 두 숫자가 정작 사용자가 가진 로그에는 안 남는다.
- `Guarded` 와 `Render` catch 가 `_lastRenderError` 를 나눠 써서 두 오류가 번갈아 나면 매 프레임
  남는다.
- 세피라이트 보고의 변경 키에 `d=`(거리)가 들어 있어 창을 연 채 움직이면 다시 남는다.
  `type/gen/acq/own/shown` 만 키로.
- 없는 신호: 멀티 잠금이 걸리고 풀리는 순간, 부팅 시 활성 세대, 적용 후 기대 점수와 사후 대조.

## 6. 테스트

전반적으로 좋다. 동작 중심이고 시드가 고정되며 `Thread.Sleep` 이 없고 각 클래스에 "왜"가 있다.

- **`OfferAdvisorTests.cs:54` 의 벽시계 단언**(`ElapsedMilliseconds < 5`, JIT 콜드, xUnit 병렬)은
  느린 CI 에서 깨진다. `LayoutCache` 를 넘겨 `Searches == 0` 을 단언한다 - 그것이 실제로 검증하려
  던 성질이다.
- `SolverCostTests` 가 `Searches/Reuses` 로 비용을 고정한 것은 옳다. 다만 `PlanBuilder.Build` 가
  자기 캐시를 만드는 배선(`:173-187`)은 안 재므로, 거기서 `layouts:` 를 안 넘기게 되는 회귀는
  통과한다. `PlanBuilder` 수준 테스트 하나(후보 + 합성기)를 더한다. `Reuses > 100` 은 매직 문턱.
- `PlanRunnerTests` 는 건전하다. 빠진 것: 같은 지문 중복 제출 제거, 실패 후 재시도 지연
  (`DateTime.UtcNow` 라 시계 주입이 필요).
- 한국어 문구에 결합된 단언(`ApplyPlanValidatorTests` "인벤토리가 바뀌어" 7회 등). 사유 코드
  enum 을 두고 그것을 단언한다.
- 회전 성질 테스트는 5개 토큰만, 위치·값만 견준다. 테두리·월드좌표 플래그·순서를 안 본다.

**없는 테스트 여덟** (가치 순): 취소 토큰(`PlanBuilder` null, `Searches == 0`), 강화 우선
`PinnedWeight`(README 가 2배를 약속하는데 참조 0), 42토큰 전수 회전 정리(`Rotated(q,4)==q`,
합성 법칙, 34/42 storage), `X`/`IGNORECRITERIA`/고정 효과 순서, 조건 질의 `PLACED/CHARM/ITEM`
과 미지 토큰 통과, `PlanRunner` 중복 제출과 재시도, **`charms.json` 파싱 + Newtonsoft↔STJ
왕복**(플러그인은 Newtonsoft PascalCase 로 쓰고 DataTool 은 STJ 대소문자 무시로 읽는데 아무것도
안 잡는다; `GridPos.cs:8` 이 함정을 적어만 두었다), `PlanBuilder` 수준 탐색 횟수.

그 밖에 참조 0인 공개 표면: `Explain.*` 아홉 개 전부, `TabletSimulator.MeetsCriteria`,
`QueryValue.ReadCriteria`, `TabletMix.Rotations`, `PlanFingerprint.PlanningContext`,
`CatalogBundleStore.WriteAtomic/NewGeneration`, `StatExchange` 경계, `MoveOrder` 대피처 없음.

## 7. 빌드·릴리스·CI

- **버전이 세 곳에 손으로 적힌다.** `BepInPlugin` 은 클래스 특성이라 `AssemblyAttribute` 로는
  안 된다. `Plugin.csproj` 에 `CoreCompile` 앞 타깃으로 `$(Version)` 을 담은
  `PluginVersion.g.cs` 를 생성해 `[BepInPlugin(PluginGuid, "SephPlanner", PluginVersion.Value)]`.
  `make-release.ps1` 은 CHANGELOG 에 `## $version` 절이 있는지 단언.
- **빌드가 재현되지 않는다.** 확인했다: `obj/Release` 의 정보 버전이 `0.1.2+9e76d38…` 이고 DLL 에
  절대 PDB 경로(`C:\Users\Nyabi\…\obj\Release\SephPlanner.Plugin.pdb`)가 박혀 있다. 같은 태그를
  다른 곳에서 다시 지어도 해시가 다르다. `IncludeSourceRevisionInInformationalVersion=false` 와
  Release 한정 `ContinuousIntegrationBuild=true`(경로를 `/_/` 로). 그러면 릴리스 스크립트가
  "태그 재빌드 == manifest" 를 증명할 수 있고, 커밋은 manifest 에 남는다.
- `manifest.json` 의 `gameAssembly.fileVersion 0.0.0.0` 은 유니티 `Assembly-CSharp.dll` 에 버전
  리소스가 없어서다. `PEReader` 로 MVID 를 읽으면 플러그인의 `GameAssemblyId` 와 같은 값이라
  릴리스 manifest 와 카탈로그 manifest 가 맞는다. Steam `appmanifest_2436940.acf` 의 `buildid`
  도 함께.
- `make-release.ps1` 이 확인하지 않는 것: HEAD 가 `v$version` 태그인지, `[BepInPlugin]` 이
  `$version` 인지, DataTool 빌드. `-Publish` 스위치로 `gh release create` 까지 하면 이번 릴리스에서
  손으로 한 단계가 없어진다.
- **CI 의 플러그인 사각지대**는 자체 호스팅 윈도우 러너(개발 PC, `runs-on: [self-hosted, windows]`,
  `if: github.repository == 'nyattic/SephPlanner'`)로 `check.ps1` 을 돌리는 것이 현실적이다.
  참조 어셈블리(Refasmer)를 저장소에 두는 것은 게임 타입 표면의 파생물이라 LEGAL 방침과 어긋나고,
  손으로 쓴 스텁은 유지가 안 된다. Core 로 옮기는 것은 병행 트랙.
- CI 는 테스트 프로젝트만 복원해 DataTool 의 lock 파일 어긋남은 릴리스 때 드러난다. `check.ps1`
  과 CI 에 DataTool 빌드 추가. `TreatWarningsAsErrors` 를 CI 한정으로. `.editorconfig` 22줄은
  공백과 CA 넷뿐이라 `dotnet format` 이 거의 아무것도 안 잡는다 - 명명 규칙과 `dotnet_style_*`.
  `NuGet.config` 에 `packageSourceMapping`.

## 8. DataTool 과 데이터

- **`--check`/`--replay` 의 입력을 만드는 것이 없다.** 둘 다 `GameSnapshot` JSON 을 먹는데
  플러그인에 스냅샷 쓰기가 없다(WPF 파이프와 함께 제거, RESEARCH:854). README:51-56 은 아직
  살아 있는 작업 흐름으로 적고 있다. F10 이 `inventory-snapshot.json` 도 함께 쓰게 하면(다섯 줄)
  제보에 재생 가능한 입력이 딸려 온다. 아니면 두 모드와 문서를 지운다. 전자를 권한다.
- 경로는 어긋나지 않았다. 다섯 모드 모두 `PlannerData.ActiveDataFile` 을 거친다(P1-4 완료).
  다만 어느 세대·게임 버전으로 돌았는지 보고서 머리에 안 찍고, `TryGetActive` 의 오류를 버려
  "덤프가 없습니다"가 `Refreshing/Failed` 일 때도 나온다.
- `Program.cs` 가 `args.Contains` 로 분기해 오타(`--valeus`)가 조용히 텍스트 추출로 떨어진다.
  `--help` 없음, `--solve` 는 어디에도 문서가 없고 `--replay` 는 RESEARCH 에만. 기본 모드가 쓰는
  `data/generated/text.json` 은 **아무 코드도 읽지 않는다** - 조사용이라고 표시하거나 지운다.
- `Load<T>` 가 세 곳에 중복. `SnapshotReplay` 의 정적 가변 `_previous`.
- **142종을 채우는 길**: (1) 행동 클래스 기본값 - `Charm_Magic` 26, `Charm_LeadNPC` 9,
  `Charm_SummonGreenBat` 7 은 `behaviorDefaults: [{behavior, tier, note}]` 절 세 줄로 42종을
  "레어도"가 아니라 "같은 종류의 중앙값"으로 올린다. `CharmWorth.Resolve` 에서 손 값과 레어도
  사이에 끼운다. (2) `--values --top N` - 런 종료 때 `seen-charms.txt` 에 본 횟수를 적는 최소
  런 기록으로 자주 만나는 것부터. (3) 쪽지가 "레어도로 어림잡은 것"이라고 말할 때 누르는 키
  하나로 `wanted-values.txt` 에 적기 - 실제 런에서 아쉬웠던 것만 모인다. (4) `--values --lint`
  로 번호·중복·`note == effect` 검사, `make-release` 에서 실행.

## 9. 문서

### 낡은 곳 (확인됨)

- "우클릭으로 강화 우선": `RESEARCH.md:430, :629`, `PlanBuilder.cs:305` 주석. 우클릭은 WPF 와
  함께 사라졌고 지금은 F2 창이다.
- `data/values/README.md:46` "오버레이 실행 파일에 포함된다" → `SephPlanner.Plugin.dll` 에 임베드.
- `RESEARCH.md:453` "`Worth.DamageBonus` 아직 실측 전" → 0.4 로 실측됨(`Worth.cs:38`).
- `ROADMAP.md` "테스트 176개" → 192.
- README 키 표는 F1~F8 만, F9/F10 은 산문에만. INSTALL.txt 1번은 자동 덤프와 어긋남.
- "아티팩트 257종"(로컬라이제이션 키) vs "299종"(카탈로그, 특수 경로 포함)은 모순은 아니지만
  설명이 없다. 한 문장.
- `P1_REMEDIATION.md` "현재 문제" 절은 이미 없는 코드(`CountLevelMismatches`,
  `Plugin/PlanRunner.cs`)를 설명한다. 끝난 인수 문서다.

### 재편 제안

README(284줄)는 네 문서다: 개발 빌드, 릴리스 절차, 플레이어 기능 설명서(106-255), 설치.
INSTALL.txt 가 기능 설명을 산문으로 되풀이하고 CHANGELOG 0.1.0 이 세 번째로 되풀이한다.
ROADMAP 은 계획과 성능 일지와 완료 목록과 끝난 체크리스트가 섞여 있다. RESEARCH 는 가장 좋은
문서지만 게임 내부와 우리 설계(618-650 채점, 823-885 안정성, 983-1015 스레드·LayoutCache)가
섞여 있다.

| 문서 | 담을 것 |
|---|---|
| `README.md` (≤120줄) | 소개·법적 한 줄, 키 표(F1~F10), 빌드·테스트·check·릴리스 명령, 구조 다이어그램, 문서 색인 |
| `docs/INSTALL.txt` | 그대로 + **문제 해결** 절: `LogOutput.log` 가 없다(doorstop 미주입, `winhttp.dll` 백신 격리, x86 zip), `plugins` 폴더가 안 생긴다(스팀으로 한 번 실행), HUD 가 안 보인다(F4, F10 `[ui]`), F9 가 멈춘다(`catalog-refresh-state.txt`), 패치 뒤에는 F9, 멀티는 읽기 전용 |
| `docs/FEATURES.md` | README 106-255 그대로 |
| `docs/ARCHITECTURE.md` | RESEARCH 의 우리 설계 세 절, ROADMAP 성능 표, P1 불변식, `%LOCALAPPDATA%\SephPlanner` 배치, 스레드·취소 모델 |
| `docs/DATATOOL.md` | 여섯 모드 전부, 입출력, 게임이 필요한지 |
| `docs/RESEARCH.md` | 게임 내부만 |
| `docs/ROADMAP.md` | "다음 단계", "백로그", "알려진 한계"만. 일지는 `HISTORY.md` 로 |
| `docs/archive/P1_REMEDIATION.md` | 보관 |
| `CHANGELOG.md` | 날짜와 릴리스 링크 |

## 10. 추가할 만한 기능

작은 것부터. 대부분 위 지적에서 자연스럽게 따라온다.

1. **F10 에 JSON 스냅샷 동봉.** 제보가 재생 가능해지고 `--check/--replay` 가 살아난다. 다섯 줄.
2. **부팅 정체 한 줄 + 5분 요약에 카탈로그·폴링 횟수.** 제보를 로그만으로 진단한다.
3. **적용 후 사후 대조 경고.** 게임 규칙 변경을 첫 F8 에서 잡는다.
4. **행동 클래스 기본값** 세 줄로 142종 중 42종을 레어도 어림에서 뺀다.
5. **접힌 화면 키 단서, F4 되돌리기 안내, F8 결과 색.** 첫 사용자가 헤매는 세 지점.
6. **"데이터 준비 중" 진행률**(N/68).
7. **최소 런 기록**(`seen-charms.txt`). 백로그의 "로컬 런 기록"의 가장 작은 형태이고
   `--values --top N` 의 입력이 된다.
8. **"아쉬웠던 아티팩트" 표시 키** → `wanted-values.txt`. 판단 데이터가 실제 플레이에서 모인다.
9. **F8 가능 시 "옮길 것" 절 접기 / 절별 토글.** 펼친 화면 높이 문제의 절반.
10. **계획 갈아타기 문턱**(백로그, 사용자 결정 대기). +1.2점에 19수 개편이 실측됐다. "이득이
    문턱을 넘을 때만 계획을 바꾼다" 옵션은 취향이라 설정으로 두는 것이 맞다.
11. **`NetworkClient.spawned` 기반 탐색**으로 ROADMAP 성능 4번(상자 1.17ms, 1초 지연)을 닫는다.
12. **ko/en 문장 표.** 게임 언어를 따른다. 크기가 커서 마지막.
13. 이웃 의존 아티팩트 열 종, 프리셋 "피하는 콤보" 반영, 인스턴스 질의 대조 - 백로그 그대로.

## 실행 순서 제안

> **처리 현황 (2026-09-03).** 아래 "0.1.3" 묶음은 코드가 전부 들어갔다. 무엇이 어디로 갔는지는
> [ROADMAP.md](ROADMAP.md) 의 "0.1.3" 절에 있고, 태그는 게임에서 확인한 뒤에 단다. 그 과정에서
> 이 문서에 없던 것 하나가 더 나왔다 - F10 덤프가 **다른 플레이어의 인벤토리 칸 수**도 적고
> 있었다(멀티 세션 덤프에서 `owner=PlayerAvatar dist=1.2 cells=17`). 같은 P0 에서 함께 막았다.
>
> 이 문서의 줄 번호는 검토 시점(`75ca1db`) 기준이라 0.1.3 이후로는 맞지 않는다. `PlanBuilder.cs`
> 의 `:657-660` 은 그때도 틀렸다 - 그 파일은 519줄이고, 가리키던 것은 `:197-201` 이다.

**0.1.3 (고침만, 화면·조작 동일).** P0 셋(카탈로그 갱신 상태, F10 숨은 정보, 초안 `note`) +
솔버 한 줄짜리 둘(꺼진 아티팩트 앵커, 수렴 최선 유지) + HUD 넘침·경고 한 줄·반쪽 화면·timeScale
+ 되돌리기 검증 + `AcceptableValueRange` + `OfferAdvisorTests` 벽시계 제거 + 부팅 정체 줄과
5분 요약 보강 + F10 JSON 스냅샷. 전부 소~중 크기이고 각각 테스트가 붙는다.

**0.2 (구조).** 매 프레임 문자열 정리와 지문 캐시, `FindLocalPlayer` 게이트, `Plugin.cs` 분해와
Core 이동(FrameCost → 세션 종류 → 카탈로그 상태 기계 → 미리보기·안내), 중복 여섯 곳 통합,
`InternalsVisibleTo`, 버전 단일화와 재현 빌드와 MVID, 자체 호스팅 CI, 없는 테스트 여덟, 문서 재편.

**그다음.** 빔 확장 할당 재작성(먼저 실기에서 GC 끊김이 남았는지 잰다 - ROADMAP 의 말 그대로),
석판 제거 갈래 잣대 보정, ~~대칭 제거~~(2026-09-04 에 넣었다), `NetworkClient.spawned`,
행동 클래스 기본값과 런 기록, 언어 표.
