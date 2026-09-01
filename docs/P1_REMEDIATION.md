# P1 개선 작업 및 검증 인수인계

작성일: 2026-09-01

기준 커밋: `ee7e4f786f210465ce16b56fedeabfcfc9ff5ed5`

이 문서는 macOS 코드 감사에서 확인한 P1 문제를 Windows 게임 설치 환경에서 수정하기 위한
실행 계획으로 시작했다. 초기 문서 작성 시점에는 구현 코드를 바꾸지 않았고,
아래에 2026-09-01 구현 결과와 남은 Windows 검증을 추가했다.

## 구현 상태 (2026-09-01)

구현 작업 기준 HEAD는 `246cb6deafe13a12b111d2ba0d2a95231bf5ea5d`이다. 네 개 P1의 코드와
회귀 테스트는 구현했다. 현재 인수 상태는 다음과 같다.

- P1-1 코드 완료: 계획 검증을 `Unavailable/Passed/Failed`로 명시하고, 열린 모든 칸의
  레벨·아이템 유효 레벨·disable·석판 `IsApplied`를 사전 검증한다.
- P1-2 코드 완료: `PlanRunner`를 Core로 옮기고 generation, latest-pending, 오래된 완료 폐기,
  실패 재시도, 배치/전체 지문을 구현했다.
- P1-3 코드 완료: 가방이 차면 후보를 반드시 포함한 상태에서 charm/tablet/filler
  교체를 열거하고, 실제 제거 인스턴스와 콤보 증감·상실을 사후 인벤토리 기준으로
  계산한다.
- P1-4 코드 완료: 세대별 디렉터리, 파일 크기/SHA-256 manifest, 원자적 active pointer,
  지속적 `Refreshing/Failed/Ready` 상태, 게임 버전과 `Assembly-CSharp` MVID 검증을 추가했다.
- 로컬 검증 완료: Core/DataTool Release 빌드 경고 0개·오류 0개, 테스트 176개 통과,
  `git diff --check` 통과.
- 남은 인수 조건: 이 변경 후 Plugin Release 빌드와 게임 내 수동 확인. macOS에는
  `Assembly-CSharp.dll`, Mirror, Unity, TextMeshPro 등 게임 DLL이 없어 Plugin 전체 빌드를
  완료할 수 없다.

### 구현한 안전 불변식

- 검증할 수 없거나 하나라도 다른 상태는 자동 배치에서 성공으로 간주하지 않는다.
- 화면과 F8은 `RequestedGeneration == PublishedGeneration == Plan.RequestGeneration`인 계획만
  현재 계획으로 취급한다.
- 계획을 만든 배치 지문·무기·카탈로그 generation이 현재 상태와 다르면 쓰기 직전에
  전체 명령을 취소한다. 지문이 비어 있는 예전 명령도 실행하지 않는다.
- 실패하거나 중단된 카탈로그 generation은 이전 성공 상태를 재사용하지 못한다.
- 가득 찬 가방 후보 평가에서 후보 자신을 탈락시킨 가짜 0점 계획은 유효하지 않다.
- 콤보 진행도는 `현재 + 후보 - 실제 교체 대상`의 순변화로 계산하며, 임계값
  상실은 음수 가치로 반영한다.

### 회귀 테스트

새 테스트는 주로 다음 파일에 있다.

- `PlanBuilderTests`: sparse level matrix, 아이템 유효 레벨, disable, 석판 적용 불일치
- `AutoPlacePolicyTests`: 최신 generation, 카탈로그 generation, Core/실시간 검증 사유
- `PlanRunnerTests`: latest-pending, 오래된 완료 폐기, 실패 후 재시도, 추천 설정 재계산
- `PlanFingerprintTests`: 목록 순서 독립성, 무기/추천 설정/수동 가치표 변경
- `ApplyPlanValidatorTests`: 무기·배치 지문 변경과 빈 지문의 fail-closed 처리
- `CatalogBundleStoreTests`: 완전 게시, 중단/실패, 변조, 게임 버전/MVID, 검증 실패
  generation의 이전 성공 미재사용
- `OfferAdvisorTests`, `OfferPreviewTests`: 후보 강제 포함, 음수 gain, 구조화 교체 대상,
  charm/tablet/filler 교체, 동점 결정, 콤보 완성/상실, preview 일치

### Windows 최종 검증 시 기록할 것

아래 `Windows 검증 명령`과 `게임 내 수동 확인표`를 변경 후 코드로 다시 실행한다.
인수 기록에는 다음을 남긴다.

- 실제 게임 버전과 `Assembly-CSharp.dll` MVID
- Plugin Release 빌드의 경고/오류 수
- F9 정상 완료 후 생성된 active generation과 질의 검증 비교/불일치 수
- 아래 수동 시나리오의 통과/실패와 `BepInEx/LogOutput.log`의 관련 한 줄

## 범위

이번 작업에서 해결할 것은 다음 네 묶음이다.

1. 자동 배치 검증의 fail-open 경로
2. 오래된 계획의 게시·표시·적용
3. 가방이 찬 상태의 후보 및 콤보 추천
4. 실패한 카탈로그 갱신이 이전 검증 성공을 재사용하는 문제

멀티플레이 자동 배치 정책 불일치는 P0이므로 이 문서의 구현 범위에는 넣지 않는다. 다만 자동
배치 가능 여부를 중앙화할 때 멀티플레이 조건도 같은 결정 객체를 사용하게 만들어, 후속 P0 작업이
한 곳에서 끝나도록 한다.

## 초기 기준선

macOS에서 게임 DLL 비의존 범위는 다음과 같이 검증됐다.

- Core Release 빌드 성공, 경고 0, 오류 0
- DataTool Release 빌드 성공, 경고 0, 오류 0
- 테스트 141개 통과, 실패·건너뜀 0
- Core, Tests, DataTool 포맷 검증 통과
- Plugin 공백 포맷 검증 통과
- 전체 NuGet 패키지에서 알려진 취약점 없음
- xUnit 2.9.3 계열은 NuGet에서 Legacy로 분류됨

Plugin 전체 빌드와 의미 기반 포맷 검사는 로컬에 `Assembly-CSharp.dll`, Mirror, Unity,
TextMeshPro 등 게임 어셈블리가 없어 완료하지 못했다. Windows 세션에서는 이 검증부터 다시 한다.

## 공통 완료 조건

네 묶음 모두 다음 조건을 만족해야 완료로 본다.

- 검증하지 못한 상태를 성공으로 취급하지 않는다.
- 화면에 표시된 계획과 자동 배치에 쓰는 계획이 같은 요청 세대다.
- 자동 배치 가능 여부와 불가 사유를 한 함수 또는 한 결과 객체가 결정한다.
- 실패 후 이전 성공 결과를 최신 결과인 것처럼 노출하지 않는다.
- 새 회귀 테스트가 결함을 수정 전에는 실패하고 수정 후에는 통과한다.
- 기존 141개 테스트를 변경 없이 통과시킨다. 의도적으로 바뀐 동작만 테스트를 갱신한다.
- Plugin을 실제 게임 DLL로 Release 빌드하고 게임 안에서 수동 확인한다.

## P1-1. 자동 배치 검증을 fail-closed로 변경

### 현재 문제

`PlanBuilder.CountLevelMismatches`는 게임의 `LevelMatrix`에 칸이 없으면 비교를 건너뛴다.

```csharp
if (!inventory.LevelMatrix.TryGetValue(key, out var reported)) continue;
```

따라서 행렬이 비어 있거나 동기화가 덜 된 상태도 `LevelMismatches == 0`이 될 수 있다. 감사 중 만든
최소 재현에서는 레벨 행렬이 비어 있는데도 다음 결과가 나왔다.

```text
mismatches=0, hasChanges=True, targets=2
```

추가로 `GameReader`가 읽는 다음 값이 계획 안전성 판단에 사용되지 않는다.

- `PlacedItem.EffectiveLevel`
- `PlacedItem.IsActive`
- `InventoryState.DisabledCells`

`SimulationVerifier`가 `IsApplied`, 효과 범위 또는 레벨 차이를 발견해도 `_lastSimulationIssue`에
기록할 뿐 `Guide`, `AutoPlace`, `PlanApplier`의 실행 조건에는 들어가지 않는다.

### 권장 설계

`LevelMismatches == 0`만으로는 “검증 통과”와 “비교할 데이터가 없음”을 구분할 수 없다. Core에
명시적인 검증 결과를 둔다. 이름과 세부 필드는 구현하면서 조정해도 되지만 세 상태는 유지한다.

```csharp
public enum PlanVerificationStatus
{
    Unavailable,
    Passed,
    Failed,
}

public sealed class PlanVerification
{
    public PlanVerificationStatus Status { get; init; }
    public int LevelMismatches { get; init; }
    public int DisabledMismatches { get; init; }
    public string Reason { get; init; } = "";
}
```

구현 원칙은 다음과 같다.

1. `SimulationVerifier.LookupLevel`과 게임 행렬의 실제 의미를 Windows에서 확인한다.
2. 누락 키가 게임에서 0을 뜻한다면 `TryGetValue` 실패를 0으로 정규화한다. 의미가 불명확하거나
   동기화 완료를 확인할 수 없다면 `Unavailable`로 둔다. 어느 경우에도 생략 후 통과시키지 않는다.
3. 열린 모든 칸의 계산 레벨과 게임 레벨을 비교한다.
4. `Arrangement` 또는 별도 시뮬레이션 결과에 비활성 칸 집합을 노출해 `DisabledCells`와 비교한다.
5. 현재 `PlacedItem.IsActive`는 실제 전체 활성 여부가 아니라 `disableMatrix <= 0`만 담는다. 이름만
   믿고 전체 활성 검증으로 사용하지 말고, 게임의 `Charm_Basic`에서 최종 활성 상태를 읽을 수
   있는지 먼저 확인한다.
6. 런타임 `_lastSimulationIssue`가 있으면 자동 배치를 금지한다.
7. 검증이 `Passed`가 아니면 `Plan.Targets`를 만들지 않거나, 최소한 자동 배치 결정 객체가 거부한다.

### 자동 배치 결정 중앙화

현재 가능 조건이 `Guide`, `AutoPlace`, `PlanApplier`에 조금씩 다르게 복제돼 있다. 다음 입력을 받는
순수 결정 로직을 만들고 가능한 곳에서 같은 결과를 사용한다.

- 최신 계획 존재 여부와 요청 generation
- 배치 변경 및 targets 존재 여부
- Core 계획 검증 상태
- 카탈로그 질의 검증 상태와 generation
- 런타임 시뮬레이션 검증 결과
- 멀티플레이 정책
- 호스트/서버 쓰기 가능 여부

결과는 단순 `bool`보다 `Allowed`와 사용자용 `Reason`을 함께 가지는 것이 좋다. HUD 안내와 F8 실패
문구가 동일한 근거를 사용해야 한다. `PlanApplier`의 살아 있는 인벤토리 검증은 마지막 방어선으로
그대로 유지한다.

### 필요한 회귀 테스트

- 빈 `LevelMatrix`에서 계산 레벨이 0이 아닌 칸이 있으면 통과하지 않는다.
- 일부 키만 있는 행렬이 조용히 통과하지 않는다.
- 게임과 시뮬레이터의 disable 칸이 다르면 targets가 비거나 자동 배치가 거부된다.
- 런타임 시뮬레이션 이슈가 있으면 다른 조건이 모두 맞아도 자동 배치가 거부된다.
- 모든 검증이 통과하면 기존 정상 스냅샷은 targets를 유지한다.
- 검증 실패 사유가 HUD 안내와 F8 결과에서 동일하다.

### 주요 수정 후보

- `src/SephPlanner.Core/Planning/Plan.cs`
- `src/SephPlanner.Core/Planning/PlanBuilder.cs`
- `src/SephPlanner.Core/Solver/PlacementProblem.cs`
- `src/SephPlanner.Core/Runtime/GameSnapshot.cs`
- `src/SephPlanner.Plugin/GameReader.cs`
- `src/SephPlanner.Plugin/SimulationVerifier.cs`
- `src/SephPlanner.Plugin/Plugin.cs`
- `src/SephPlanner.Plugin/PlanApplier.cs`

## P1-2. 계획 요청과 결과에 generation 도입

### 현재 문제

현재 `_lastStateJson`은 “마지막으로 솔버에 제출한 상태”가 아니라 “마지막으로 관측한 상태”다.
`PollGameState`가 먼저 이 값을 갱신한 뒤 `FeedNativePanel`이 화면 설정을 보고 반환한다. 그 결과
화면을 끈 동안 상태가 바뀌면, 다시 켠 첫 폴링에서 최신 상태가 이미 소비된 것으로 보여 이전
계획을 그대로 사용할 수 있다.

또한 다음 문제가 연결돼 있다.

- `Recommendations` 설정은 `_prefs.Revision`에 포함되지 않아 즉시 재계산되지 않는다.
- 작업 중 새 요청은 호출자가 나중에 재제출해야 하며, 중간에 이전 결과가 게시될 수 있다.
- 이전 성공 계획이 있는 상태에서 새 계산이 실패하면 `_latest`는 이전 계획으로 남는다.
- 위 실패 뒤 상태와 revision이 같으면 조기 반환되어 자동 재시도가 일어나지 않는다.
- `AutoPlace`는 runner 오류, busy 상태, 결과 generation을 확인하지 않는다.
- `ApplyPlanCommand`는 위치·회전·격자 크기만 확인하고 무기나 모델 입력 변경을 확인하지 않는다.

### 권장 설계

관측, 제출, 완료를 서로 다른 상태로 관리한다.

1. 계획에 영향을 주는 입력으로 요청 fingerprint를 만든다.
2. fingerprint가 바뀔 때마다 generation을 증가시킨다.
3. `PlanRunner.Submit`에 generation을 함께 넘긴다.
4. 작업이 끝났을 때 그 generation이 아직 최신 요청일 때만 결과를 게시한다.
5. busy 중 새 요청이 오면 하나짜리 latest-pending 슬롯에 가장 최신 요청만 남긴다. 이전 요청을
   모두 큐에 쌓지 않는다.
6. 새 요청이 생긴 순간 이전 결과는 표시용으로 남길 수 있어도 `stale`로 표시하고 자동 배치에는
   절대 사용하지 않는다.
7. 실패한 generation은 오류 상태로 게시하고 이전 계획을 최신 계획으로 가장하지 않는다.
8. 재시도는 다음 폴링 또는 짧은 backoff 뒤 가능해야 한다.

요청 fingerprint에는 최소한 다음을 포함한다.

- 격자 크기와 열린 칸 수
- 아이템 ID, 인스턴스 ID, 위치, 인챈트
- 석판 ID, 위치, 회전, 인스턴스 질의·조건, 회전 가능 여부
- 고정 효과, 레벨 행렬, disable 상태
- 장착 무기
- pinned/priority/preset revision
- `Recommendations` 설정
- 카탈로그 및 검증 generation

추천 후보와 골드는 배치 targets 자체에는 영향을 주지 않지만 화면의 `Plan.Offers`에는 영향을 준다.
필요하면 “전체 계획 fingerprint”와 “자동 배치 fingerprint”를 분리한다. 자동 배치 직전에는 현재
스냅샷으로 배치 관련 fingerprint를 다시 계산해 계획과 대조한다. 단순히 모든 JSON을 비교해 골드
변화만으로 자동 배치를 막는 방식은 안전하지만 사용자 경험이 나쁘므로 피한다.

`ApplyPlanCommand`에 fingerprint 또는 snapshot revision을 싣는 경우, Plugin이 살아 있는 상태를
다시 읽어 대조한 뒤 기존 `ApplyPlanValidator`의 구조 검증으로 이어지게 한다.

### 필요한 회귀 테스트

- Panel이 꺼진 동안 인벤토리가 바뀌고 다시 켜지면 반드시 새 요청이 제출된다.
- `Recommendations`를 끄면 같은 스냅샷에서도 새 generation이 생기고 offers 계산을 건너뛴다.
- `Recommendations`를 다시 켜면 같은 스냅샷에서도 offers가 다시 계산된다.
- 느린 이전 generation의 결과가 빠른 최신 generation 뒤에 완료돼도 게시되지 않는다.
- 이전 성공 뒤 다음 계산이 실패하면 이전 계획을 최신으로 적용할 수 없다.
- 실패한 요청은 상태가 그대로여도 재시도할 수 있다.
- 위치가 같아도 무기가 바뀌면 이전 계획의 자동 배치가 거부된다.
- 최신 generation의 검증된 계획만 F8 실행 조건을 통과한다.

### 주요 수정 후보

- `src/SephPlanner.Plugin/PlanRunner.cs`
- `src/SephPlanner.Plugin/Plugin.cs`
- `src/SephPlanner.Core/Runtime/ApplyPlanCommand.cs`
- `src/SephPlanner.Core/Runtime/ApplyPlanValidator.cs`
- `tests/SephPlanner.Tests/ApplyPlanValidatorTests.cs`

PlanRunner 자체는 게임 타입을 사용하지 않는다. 가능한 한 Core 또는 게임 DLL 비의존 프로젝트로
상태 기계를 분리해 Windows 전용 테스트가 되지 않게 한다.

## P1-3. 가득 찬 가방의 후보·교체·콤보를 실제 사후 상태로 계산

### 현재 문제

`OfferAdvisor`는 후보를 평범한 charm/tablet 슬롯으로 추가해 솔버에 넘긴다. 가방이 차 있고 후보가
약하면 헝가리안 배정이 후보 자신을 탈락시킬 수 있다. 그런데 `DisplacedBy`는 후보 ID를 건너뛰므로
교체 대상도 빈 문자열이 된다.

감사 중 최소 재현 결과는 다음과 같다.

```text
full-bag: gain=0.000, displaced='', candidatePlaced=False
```

실제 의미는 “후보를 집었을 때”인데 계산은 “후보를 가방 옆에 두고, 필요하면 후보를 무시했을 때”가
되어 있다. 약한 후보의 실제 교체 손실이 0으로 잘못 표시될 수 있다.

콤보 계산도 후보 카테고리를 무조건 현재 개수에 더하고, 밀려나는 아티팩트의 카테고리를 빼지 않는다.
같은 카테고리 아티팩트를 교체한 재현에서는 실제 개수가 그대로인데 콤보 완성으로 표시됐다.

```text
full-bag-combo: displaced='old-ember', comboCompletes=True, comboText='EMBER 2/2'
```

현재 교체 이름 탐색은 charms만 보므로, 게임에서 기존 석판도 버릴 수 있다면 그 경우도 모델링되지
않는다.

### 먼저 게임에서 확인할 것

가방이 찬 상태에서 아티팩트와 석판을 집을 때 실제 UI가 어떤 기존 물건을 제거 대상으로 허용하는지
확인한다.

- 아티팩트만 제거할 수 있는가
- 석판도 제거할 수 있는가
- 일반 filler/소비 아이템도 제거할 수 있는가
- 제거 불가능하거나 고정된 종류가 있는가

이 규칙을 확인하지 않고 모든 슬롯을 교체 가능하다고 가정하지 않는다.

### 권장 설계

후보를 반드시 포함하는 사후 인벤토리를 명시적으로 만든다.

1. 빈 칸이 있으면 후보를 추가한 trial 하나를 푼다.
2. 빈 칸이 없으면 제거 가능한 기존 인스턴스를 하나씩 제외한 trial을 만든다.
3. 각 trial에는 후보를 추가하고, 총 한 칸 아이템 수가 storage를 넘지 않게 한다.
4. 각 trial을 같은 `SolverOptions`로 풀어 가장 높은 실제 사후 점수를 고른다.
5. 선택한 trial의 제거 인스턴스를 `Displaced`와 구조화된 필드에 기록한다.
6. 후보가 최종 배치에 없으면 그 trial은 유효하지 않은 것으로 취급한다.
7. 제거할 수 있는 기존 물건이 없으면 후보를 `Unavailable`로 표시한다.

문자열 이름만 남기지 말고 필요하면 다음 정보를 함께 둔다.

- 제거 인스턴스 ID
- 제거 대상 종류(charm/tablet/filler)
- 제거 대상 정의 ID
- 제거 대상 이름

콤보는 선택된 사후 인벤토리를 기준으로 계산한다. 최소한 후보 카테고리 추가와 제거 charm의
카테고리 감소를 순증감으로 함께 처리해야 한다. 같은 카테고리를 교체하면 delta는 0이다.

다만 현재 `ComboCounts`는 유니크 페어 보정과 중복 제한 등 게임 서버 규칙이 이미 반영된 값이다.
단순 `+1/-1`만으로 모든 경우가 정확한지는 Windows에서 `SearchSetEffectInInventory` 규칙을 다시
확인한다. 비선형 규칙이 후보 교체에도 영향을 준다면 필요한 런 상태를 스냅샷에 추가하고 그 규칙을
Core로 포팅한다. 정확히 재현하지 못하는 전이는 완료·상실을 단정하지 말고 근사임을 표시한다.

콤보 손실도 점수에 반영해야 한다. 예를 들어 기존 카테고리의 임계값 아래로 떨어지는 교체는
`ComboBonus`가 음수가 될 수 있어야 한다.

### 필요한 회귀 테스트

- 가득 찬 가방에서 약한 후보도 사후 배치에 반드시 존재한다.
- 약한 후보의 gain은 교체 손실을 반영해 음수가 될 수 있다.
- `Displaced`는 실제 제외된 기존 인스턴스와 일치한다.
- 같은 카테고리 charm 교체는 콤보 개수를 올리지 않는다.
- 다른 카테고리 교체는 기존 콤보 손실과 신규 콤보 진행을 함께 반영한다.
- 후보 tablet도 최종 사후 배치에 반드시 존재한다.
- 게임 규칙상 제거 가능한 tablet/filler가 있다면 각각의 교체를 평가한다.
- 제거 가능한 항목이 없으면 잘못된 0점 추천 대신 선택 불가 상태를 반환한다.
- 동점 교체 후보는 인스턴스 ID 등 명시적인 기준으로 결정되어 폴링마다 흔들리지 않는다.
- preview에 후보와 실제 교체 결과가 그대로 나타난다.

### 주요 수정 후보

- `src/SephPlanner.Core/Solver/OfferAdvisor.cs`
- `src/SephPlanner.Core/Planning/PlanBuilder.cs`
- `src/SephPlanner.Core/Solver/PlacementProblem.cs`
- `src/SephPlanner.Core/Planning/Explain.cs`
- `tests/SephPlanner.Tests/OfferAdvisorTests.cs`
- `tests/SephPlanner.Tests/OfferPreviewTests.cs`

## P1-4. 카탈로그와 질의 검증 결과를 한 트랜잭션으로 게시

### 현재 문제

`CatalogDump.WriteRoutine`은 다음 순서로 동작한다.

1. `_queryVerified = null`
2. 새 tablets/charms/combos/measurement 파일을 각각 교체
3. 질의 전수 검증
4. 검증 보고서, 상태, catalog version 기록
5. 메모리의 `_queryVerified` 설정

중간에 예외가 나면 Plugin은 로그만 남기고 코루틴을 끝낸다. 이후
`QueryVerificationPassed()`는 이전 `query-verification.json`을 다시 읽을 수 있다. 데이터 파일
일부는 새것이고 검증 상태와 version은 예전인 혼합 상태도 가능하다.

### 권장 설계

개별 파일의 원자적 교체가 아니라 “검증된 카탈로그 묶음”의 원자적 게시가 필요하다. Windows에서
가장 명확한 구조는 generation 디렉터리와 작은 active manifest다.

```text
SephPlanner/
  catalog-generations/
    <generation-id>/
      tablets.json
      charms.json
      combos.json
      stat-measure.json
      query-verification.txt
      query-verification.json
      manifest.json
  active-catalog.json
```

게시 순서는 다음과 같다.

1. 새 generation 디렉터리에 모든 파일을 쓴다.
2. 질의 검증을 완료한다.
3. 파일 크기와 필요하면 SHA-256을 포함한 manifest를 쓴다.
4. 모든 파일을 다시 열어 manifest와 검증 결과를 확인한다.
5. 마지막에 작은 `active-catalog.json` 포인터만 `File.Replace` 또는 temp+move로 교체한다.
6. reader는 active pointer가 가리키는 한 generation만 읽는다.

더 작은 변경을 원하면 flat 파일을 유지할 수 있지만, 마지막 commit manifest에 모든 파일의 hash와
generation을 담고 `HasCatalog`가 항상 이를 검증해야 한다. 이 방식은 실패 시 이전 묶음을 그대로
유지하기 어려우므로 generation 디렉터리가 더 안전하다.

추가 원칙은 다음과 같다.

- 갱신 시작 즉시 현재 자동 배치 검증 상태를 `Refreshing/Unavailable`로 바꾼다.
- 실패 시 명시적으로 `Failed`를 기록하고 이전 상태 파일을 성공으로 재사용하지 않는다.
- 실패한 갱신 뒤 표시용으로 이전 카탈로그를 유지할지는 선택할 수 있지만 자동 배치는 새 검증 성공
  전까지 잠근다.
- `DumpRoutine`의 catch가 `CatalogDump`에 실패를 통지하도록 한다.
- 검증 캐시는 catalog generation, 게임 버전과 결합한다.
- 가능하면 `Application.version`뿐 아니라 `Assembly-CSharp.dll`의 MVID 또는 hash도 묶는다. 버전
  문자열이 같은 핫픽스에서도 이전 검증을 재사용하지 않기 위해서다.
- 실패한 generation 정리는 성공 게시 후 별도 단계에서 한다. 활성 generation은 절대 삭제하지 않는다.

DataTool이 기존 flat 경로를 읽고 있으므로 `PlannerData`에 active bundle 경로 해석을 한 곳으로 모은다.
Plugin과 DataTool이 각자 다른 경로 규칙을 구현하지 않게 한다.

### 필요한 회귀 테스트

파일 저장 로직을 게임 타입에서 분리하고 임시 디렉터리로 다음을 검증한다.

- 이전 검증 성공 상태에서 새 갱신이 데이터 파일 기록 전에 실패하면 자동 배치는 잠긴다.
- 일부 데이터 파일 기록 뒤 실패해도 혼합 bundle이 active가 되지 않는다.
- 검증 상태 기록 직전 또는 active pointer 교체 직전 실패해도 이전/새 bundle이 섞이지 않는다.
- 완전 성공한 generation만 재시작 후 `HasCatalog`와 `QueryVerificationPassed`를 통과한다.
- active manifest의 hash 또는 generation이 다르면 통과하지 않는다.
- 게임 버전 또는 게임 어셈블리 식별자가 바뀌면 이전 검증을 재사용하지 않는다.

### 주요 수정 후보

- `src/SephPlanner.Plugin/CatalogDump.cs`
- `src/SephPlanner.Plugin/Plugin.cs`
- `src/SephPlanner.Core/Runtime/PlannerData.cs`
- `src/SephPlanner.DataTool/SnapshotCheck.cs`
- `src/SephPlanner.DataTool/SnapshotReplay.cs`

## 권장 구현 순서

1. Windows 기준선 빌드와 테스트를 먼저 기록한다.
2. 자동 배치 결정 로직을 게임 타입에서 분리하고 테스트 가능한 형태로 만든다.
3. Core 계획 검증을 `Unavailable/Passed/Failed`로 바꾼다.
4. PlanRunner generation과 최신 결과 게시 규칙을 구현한다.
5. 자동 배치 직전 freshness와 검증 상태를 중앙 결정 로직에 연결한다.
6. 카탈로그 저장을 generation/manifest 방식으로 바꾸고 검증 generation을 계획 요청에 포함한다.
7. OfferAdvisor의 후보 강제 포함·교체 열거를 구현한다.
8. 사후 인벤토리 기준 콤보 계산을 구현한다.
9. 전체 테스트, 포맷, Plugin 빌드 후 게임에서 수동 검증한다.

P1-1과 P1-2는 자동 배치 안전 경계를 함께 바꾸므로 먼저 끝낸다. P1-4의 catalog generation은
P1-2의 요청 fingerprint에 들어가야 하므로 OfferAdvisor보다 먼저 하는 편이 재작업이 적다.

## Windows 검증 명령

PowerShell에서 게임 설치 경로를 명시한다.

```powershell
$env:SEPHIRIA_DIR = "D:\SteamLibrary\steamapps\common\Sephiria"

dotnet --info
dotnet restore SephPlanner.slnx --locked-mode
dotnet format SephPlanner.slnx --verify-no-changes --no-restore
dotnet test tests/SephPlanner.Tests/SephPlanner.Tests.csproj -c Release --no-restore
dotnet build src/SephPlanner.DataTool/SephPlanner.DataTool.csproj -c Release --no-restore
dotnet build src/SephPlanner.Plugin/SephPlanner.Plugin.csproj -c Release --no-restore -p:DeployToGame=false
dotnet list SephPlanner.slnx package --vulnerable --include-transitive
```

수정한 C# 파일에는 저장소의 기존 포맷 도구를 적용한다. 새 패키지를 추가하지 않고 구현할 수 있는
범위이므로 의존성 추가는 피한다.

## 게임 내 수동 확인표

- 정상 싱글 런에서 검증 통과 후 F8이 기존처럼 동작한다.
- 덤프 중, 덤프 실패 후, 질의 검증 실패 후에는 F8 안내가 보이지 않고 눌러도 쓰지 않는다.
- 게임 레벨/disable 결과가 계산과 다르면 이유를 보여 주고 쓰지 않는다.
- 설정에서 Panel을 끈 채 아이템·석판·무기를 바꾼 뒤 다시 켜면 최신 계획을 계산한다.
- 후보 추천을 끄고 켰을 때 상태 변화 없이도 offers가 즉시 사라지고 다시 계산된다.
- 계산 중 상태를 연속으로 바꿔도 마지막 상태의 계획만 표시된다.
- 계산 실패를 유도했을 때 이전 계획으로 F8을 실행할 수 없다.
- 가득 찬 가방에서 약한 후보를 보면 음수 gain과 실제 교체 대상이 표시된다.
- 같은 카테고리 교체가 거짓 콤보 완성으로 표시되지 않는다.
- F9 갱신을 중간에 실패시킨 뒤 재시작해도 이전 성공 상태를 새 검증 성공으로 오인하지 않는다.

## 최종 인수 조건

작업 완료 보고에는 다음을 남긴다.

- 각 P1별 수정 요약과 새 불변식
- 추가한 회귀 테스트 이름
- 전체 테스트 결과
- Plugin Release 빌드 결과
- 실제 게임에서 확인한 시나리오와 게임 버전
- 해결하지 못한 항목과 그 이유
- 작업 트리에 남아 있는 사용자 변경과 이번 변경의 구분
