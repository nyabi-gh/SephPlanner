# 인계

직전 세션이 무엇을 했고 다음에 무엇을 할지. **[현재 상태](STATUS.md)가 정본이고 이 문서는 그날의 길잡이다.**
둘이 어긋나면 STATUS 를 믿는다. 세션마다 이 파일을 통째로 덮어쓴다 - 날짜별로 쌓지 않고, 제보별 조사 경위도
여기 남기지 않는다(결론은 해당 문서에, 경위는 커밋 메시지에).

## 2026-10-30 - 1.0.33·1.1.0 동시 지원

지원 범위는 사용자와 정했다. 한 DLL 이 1.0.33 과 1.1.0 베타에서 함께 돈다. 1.1.0 이 베타이고, 게임 안 업데이트가
`releases/latest` 만 보기 때문이다. 새 게임 판 지원이라 다음 판은 0.4.28 이 아니라 **0.5.0** 으로 열었다(사용자 제안).
`main` 은 푸시했고 태그·릴리스는 하지 않았다.

- 플러그인은 침 계열 클래스를 하나도 컴파일 시점에 참조하지 않는다. `ItemCatalog.Dependency` 가 타입 이름과 공개 필드로
  읽고, `GameReader` 는 나침반의 지금 카테고리를 하얀 종이처럼 싣는다. 카탈로그 형식은 27 이고, 26 은 재현에서 계속 읽는다.
- Core 는 `Compass`(새 파일)가 나침반 규칙을 맡는다. 부모 칸 여럿, 뿌리 합집합, 꺼지면 카테고리·몫이 없고 사슬도 끊김,
  돌파의 희귀도 무시. `PositionalWorth`·`ComboCounting`·`DirectedCharmSupport`·`PriorityPlacement`·`PlacementSolver`·
  `Explain` 이 동작 클래스 이름으로 갈래를 탄다. 어림인 곳은 [게임 내부 조사](RESEARCH.md)의 "침은 1.1.0 에서 나침반이 됐다".
- 규칙은 두 판 디컴파일로 다시 확인했다. 1.0.33 어셈블리 사본은 `C:\Users\Nyabi\Documents\Sephiria-1.0.33` 에 있다.

검증한 것:

- `scripts/check.ps1` 이 `SEPHIRIA_DIR` 을 1.1.0 설치본과 1.0.33 사본으로 바꿔 둘 다 통과했다(나침반 커밋 때 968개, 경고 0). 새 테스트는 `CompassTests`.
- `reports/` 넷의 `--reproduce --allow-model-change` 가 고치기 전과 줄마다 같다(빌드 줄 제외). **다만 넷 다 침이 없는
  판이라 옛 침 규칙을 지켰다는 증거는 아니다.** 그쪽은 기존 침 테스트(`NeedleConnectionTests` 등)가 받친다.
- 두 설치본으로 빌드한 DLL 의 게임 참조를 Mono.Cecil 로 두 판 `Managed` 에서 `Resolve()` 했다. 1.0.33 에서는 깨진 것이 없다.
  1.1.0 에서는 Harmony 의 `Patch`(인자 여섯)와 `UnpatchSelf` 둘만 걸린다. 게임이 싣는 Lib.Harmony 2.4.3 에는 없지만
  BepInEx 의 HarmonyX 에는 있는 멤버이고, 이미 알려진 것이다.

나머지 1.1.0 항목:

- 개인 합성기: `GameReader.ReadMixer` 가 내 것이 아닌 `TabletMix_Personal` 을 거른다.
- 콤보 능력치: 스냅샷이 `UNLIMITEDCOMBO`·`COMBOBONUSDAMAGE` 를 싣고(둘 다 0 이면 없음 - 지문·재현 그대로),
  `PlacementProblem.ComboStep` 이 첫 단계와 최고 단계 너머에 피해 몫을 더한다. 테스트 `ComboStatBonusTests`.
- 배치 보너스: 에셋을 읽어 보니 레시피 둘 다 합치기형이 아니라 아이템이 사라지지 않는다. 재계산 반복과 함께 ROADMAP 4번
  표에 다시 열 조건을 적고 두었다.
- 두 설치본 check.ps1 통과(976개, 경고 0), `reports/` 넷 전후 동일, Cecil 대조 결과는 위와 같다.

게임에서는 아무것도 보지 못했다. 이 PC 의 게임 폴더에는 BepInEx 가 없다.

## 다음 세션

1. **0.5.0 릴리스.** 태그 `v0.5.0`, `make-release.ps1` 로 Windows 드래프트, Mac ZIP, 공개. 공개는 사용자가 정한다.
2. **실기.** STATUS 표 맨 위 0.5.0 네 줄 - 1.1.0 카탈로그와 Harmony, 나침반 콤보, 1.0.33 침 판, 남의 개인 합성기.
3. STATUS 표의 0.4.27·0.4.26·0.4.25 실기 줄.
4. **다음 제보의 `[perf]` 로 정할 것 둘**(추천이 바뀔 수 있어 사용자와 정한다). "활성" 이 구성의 큰 몫이면 빔 열쇠에서
   지금 활성 빼기, 각도가 크면 각도 빼기([성능](PERFORMANCE.md) 4번).
5. 이전 인계에서 남은 것: 진단 자료 가림, 자동 진단 수집 계측, `Guarded`, `CharmCriteria.ReadsNeighbors` 시험.

**문서를 새로 만들기 전에** STATUS 의 문서 지도에 들어갈 자리가 있는지 먼저 본다. 날짜가 붙은 보고서 파일은
만들지 않는다.
