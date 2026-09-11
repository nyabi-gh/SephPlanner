using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 화면이 "이게 왜 이런가"를 설명할 때 쓰는 문장들.
    ///
    /// 설명 규칙을 화면 코드와 분리해 한 자리에 둔다.
    ///
    /// 문장만 만들고 어떻게 보여줄지는 부르는 쪽이 정한다 - 줄 목록으로 돌려주는 것도 그래서다.
    /// 조작법처럼 특정 화면에서만 참인 말은 부르는 쪽이 덧붙인다.
    /// </summary>
    public static class Explain
    {
        /// <summary>
        /// 아티팩트가 무슨 일을 하는지, 그리고 우리가 그 값어치를 어떻게 정했는지. 잰 값일 때는
        /// 굳이 말하지 않고, 근거가 레어도뿐일 때만 밝힌다 - 그 자리가 추천이 가장 흔들리는 곳이라
        /// 사용자가 "왜 이게 위에 있지"라고 물을 지점이다.
        /// </summary>
        public static List<string> Charm(CharmDefinition? definition, CharmValueBook? values)
        {
            if (definition is null) return new List<string>();

            var entry = (values ?? CharmValueBook.Empty).Of(definition);
            var lines = new List<string>(definition.EffectLines);
            var worth = CharmWorth.Resolve(definition, entry);
            if (definition.Behavior == "Charm_WhitePaper")
                lines.Add("종이끼리 연결되면 최근 관측한 카테고리로 추정합니다. 갱신 순서에 따라 이동 후 결과가 달라질 수 있습니다.");

            if (worth.Source == CharmWorthSource.Curated && entry is { Note.Length: > 0 }) lines.Add(entry.Note);

            // 점수는 진행한 만큼 올라가는데 쪽지에 이유가 없으면, 같은 아티팩트가 왜 다르게
            // 매겨지는지 알 길이 없다. 진행도는 인스턴스마다 달라 여기서 숫자를 적지는 못한다.
            if (ScalesPosition.IsSideBound(definition))
                lines.Add("가방의 어느 편에 있느냐로 주는 것이 달라집니다. 왼쪽은 1~3열, 오른쪽은 4열 "
                          + "이후이며 지금 놓인 쪽을 유지합니다. 바꾸려면 직접 반대쪽으로 옮기세요. "
                          + "좌우 중 어느 쪽이 나은지는 비교하지 않습니다.");

            if (definition.GrowthQuestGoal > 0)
                lines.Add("다 키우면 다른 아티팩트가 됩니다. 얼마나 키웠는지에 따라 값어치를 그쪽으로 끌어올려 평가합니다.");

            if (definition.ContextStats.Count > 0 && worth.Source != CharmWorthSource.Curated)
            {
                lines.Add("배치의 아이템 수량·행에 따른 능력치를 함께 평가합니다. 점수는 능력치 환산값이며 실제 DPS가 아닙니다.");
                if (definition.ContextStats.Exists(bonus => !bonus.WorthPerUnit.HasValue))
                    lines.Add("환산하지 못한 능력치가 있어 기존 어림값을 함께 사용합니다.");
                return lines;
            }

            if (definition.MagicSupport is not null)
            {
                lines.Add(SupportDirection(definition.MagicSupport) + "의 사용 가능한 마법과 연결돼야 효과를 냅니다.");
                lines.Add(worth.Source == CharmWorthSource.Curated
                    ? "연결된 상태에서 직접 지정한 값어치를 사용합니다."
                    : definition.MagicSupport.Effect == MagicSupportEffect.ManaCostReduction
                        ? "보너스의 가치는 대상 마법의 기본 마나 비용 절약률로 추정합니다. 다른 비용 변경·버프·실제 시전 빈도는 반영하지 않습니다."
                        : "보너스의 가치는 대상 마법과 회복 속도로 추정합니다. 추가 버프·마나·실제 시전 빈도는 반영하지 않습니다.");
                return lines;
            }

            if (worth.Source == CharmWorthSource.Rarity)
                lines.Add("값어치는 레어도로 어림잡은 것입니다. 효과의 세기는 아직 점수에 없습니다.");

            if (worth.Source == CharmWorthSource.MeasuredFloor)
                lines.Add("효과의 일부만 측정되어 레어도 어림값을 함께 사용합니다. 실제 전투 효과와 다를 수 있습니다.");
            if (definition.StatEffects.Count > 0 && worth.Source != CharmWorthSource.Curated)
                lines.Add("능력치 표의 환산값입니다. 확인된 마법 지원 능력치는 주력 선호를 반영하지만 실제 사용률·전투 피해량은 추정하지 않습니다.");
            lines.AddRange(Circular(definition, worth));

            return lines;
        }

        /// <summary>
        /// 환산율이 이 아티팩트 자신에게서 나온 경우. 그 능력치를 주는 아티팩트가 이것뿐이면
        /// "레벨 하나 = 이 아티팩트의 한 걸음"이라는 동어반복이라, 다른 아티팩트와 견줄 때 쓸
        /// 수 있는 값이 아니다. 1.0.31 에서 잰 값 173종 중 43종이 여기 해당한다.
        ///
        /// 잰 값이라는 것만 말하고 근거의 두께를 안 말하면, 짐작에 가까운 숫자가 실측과 같은
        /// 무게로 보인다.
        /// </summary>
        private static List<string> Circular(CharmDefinition definition, CharmWorth worth)
        {
            var lines = new List<string>();
            if (definition.StatEffects.Count == 0 || worth.Source == CharmWorthSource.Curated) return lines;
            if (worth.Confidence >= 0.5) return lines;

            lines.Add(worth.Confidence <= 0
                ? "다만 이 능력치를 주는 아티팩트가 이것뿐이라 환산율이 자기 자신에서 나왔습니다. "
                  + "다른 아티팩트와 견주는 근거로는 약합니다."
                : "다만 환산율의 근거가 얇은 능력치가 섞여 있습니다. 다른 아티팩트와 견줄 때 "
                  + "그만큼 덜 믿을 값입니다.");
            return lines;
        }

        /// <summary>
        /// 격자 한 칸. 이름 다음에 무슨 아티팩트인지가 오고, 이 자리에서만 해당하는 이야기가
        /// 그 뒤에 온다.
        /// </summary>
        public static List<string> Cell(
            string name, int level, int effective, CharmInactiveReason reason,
            CharmDefinition? definition, CharmValueBook? values)
        {
            var lines = new List<string> { name };
            lines.AddRange(Charm(definition, values));

            if (reason != CharmInactiveReason.None)
            {
                lines.Add(InactiveReason(reason));
                if (definition is not null && PositionalWorth.IsNeedle(definition))
                    lines.Add("침은 비활성 상태에서도 공격 대상과 연결되면 0레벨 피해 보너스가 남습니다. 사용 유지는 활성 상태까지 요구합니다.");
                return lines;
            }

            if (level > effective)
                lines.Add($"칸 레벨 {level}, 이 아티팩트는 {effective}까지만 반영됩니다");

            lines.AddRange(LevelCosts(definition, effective, values));
            return lines;
        }

        /// <summary>
        /// 여기서 한 칸 더 올리면 값어치가 내려가는 아티팩트인지. 내려가지 않으면 빈 목록이다.
        ///
        /// 레벨이 올라도 값어치가 내려가는 아티팩트가 있다 - 이득보다 손해가 빨리 자라는 것들이고,
        /// 도마뱀 판금 갑옷은 모든 구간이 그렇다. 솔버는 값어치가 큰 배치를 고르므로 그런 것은
        /// 낮은 칸에 남는데, 말해 주지 않으면 "왜 여기에 박아 두느냐"로 보인다(2026-09-11 제보).
        /// 모델이 틀린 것이 아니라 게임이 실제로 그런 맞교환이므로, 점수로 덮지 않고 설명한다
        /// (docs/PLACEMENT-OBJECTIVE.md 의 "탐색과 최적성").
        /// </summary>
        private static List<string> LevelCosts(CharmDefinition? definition, int effective, CharmValueBook? values)
        {
            var lines = new List<string>();
            if (definition is null || effective < 0) return lines;

            foreach (var drop in WorthCurveReview.Of(definition, (values ?? CharmValueBook.Empty).Of(definition)))
            {
                if (drop.FromLevel != effective) continue;

                lines.Add($"레벨 {drop.ToLevel}로 올리면 늘어나는 손해가 이득보다 커서 값어치가 내려갑니다. "
                          + "더 높은 칸을 비워 둔 것은 그 때문이며, 올리고 싶으면 직접 옮기세요.");
                break;
            }
            return lines;
        }

        public static string SupportMissing(CharmDefinition definition) =>
            definition.MagicSupport is { } support
                ? SupportDirection(support) + "에 사용 가능한 마법이 없어 강화 효과를 받지 못합니다."
                : PositionalWorth.IsNeedle(definition)
                    ? "침의 연결 끝에 사용 가능한 공격 아티팩트가 없습니다."
                    : "강화 대상과 연결되지 않았습니다.";

        private static string SupportDirection(DirectedMagicSupport support)
        {
            var parts = new List<string>();
            if (support.OffsetX != 0) parts.Add((support.OffsetX > 0 ? "오른쪽 " : "왼쪽 ") + Math.Abs(support.OffsetX) + "칸");
            if (support.OffsetY != 0) parts.Add((support.OffsetY > 0 ? "아래 " : "위 ") + Math.Abs(support.OffsetY) + "칸");
            return parts.Count == 0 ? "같은 칸" : string.Join("·", parts);
        }

        public static string InactiveReason(CharmInactiveReason reason) => reason switch
        {
            CharmInactiveReason.Weapon => "연동된 무기를 들고 있지 않아 꺼져 있습니다. 옮겨도 켜지지 않습니다.",
            CharmInactiveReason.Disabled => "석판이 이 칸을 사용 불가로 만들었습니다.",
            CharmInactiveReason.NegativeLevel => "레벨이 0 미만이라 꺼져 있습니다.",
            CharmInactiveReason.Criteria => "이 아티팩트의 배치 조건을 만족하지 못했습니다.",
            _ => "",
        };

        /// <summary>지금 집을 수 있는 후보 하나. 왜 그 자리에 있는지를 순위 대신 설명한다.</summary>
        public static List<string> Offer(OfferAdvice advice, int gold, CharmValueBook? values)
        {
            var lines = Charm(advice.Candidate.Charm, values);

            if (!advice.Available) lines.Add("가방의 공간과 사용 유지 조건을 만족하는 후보 배치를 찾지 못했습니다.");
            if (!advice.Affordable) lines.Add($"소지금 {gold}골드로는 살 수 없습니다.");

            if (advice.MatchesPreset) lines.Add("가져온 빌드가 즐겨찾기로 찍어 둔 아티팩트입니다.");
            if (advice.MatchesPriority) lines.Add("밀고 있는 빌드의 아티팩트입니다.");
            if (advice.Displaced.Length > 0) lines.Add($"가방이 차 있어, 집으면 빠지는 것: {advice.Displaced}");

            if (advice.ComboText.Length > 0)
            {
                lines.Add(advice.ComboCompletes && advice.ComboLoses
                    ? $"콤보 구성이 바뀝니다: {advice.ComboText}"
                    : advice.ComboCompletes
                        ? $"콤보가 발동합니다: {advice.ComboText}"
                        : advice.ComboLoses
                            ? $"콤보 효과를 잃습니다: {advice.ComboText}"
                            : $"콤보 진행: {advice.ComboText}");
            }

            var effect = advice.Effect;
            if (effect.RaisedCells > 0)
                lines.Add($"{effect.RaisedCells}칸의 레벨을 모두 합쳐 {effect.RaisedTotal} 올립니다.");
            if (effect.LoweredCells > 0)
                lines.Add($"{effect.LoweredCells}칸은 합쳐 {effect.LoweredTotal} 내립니다.");
            if (effect.DisabledCells > 0) lines.Add($"{effect.DisabledCells}칸은 쓸 수 없게 만듭니다.");
            if (effect.MultipliedCells > 0) lines.Add($"{effect.MultipliedCells}칸에 배수가 걸립니다.");
            if (effect.IgnoreCriteriaCells > 0)
                lines.Add($"{effect.IgnoreCriteriaCells}칸은 배치 조건을 무시합니다.");

            return lines;
        }

        public static List<string> Mix(MixAdvice advice)
        {
            var lines = new List<string>
            {
                $"{advice.NameA} 와(과) {advice.NameB} 을(를) 합칩니다. 재료 둘은 사라집니다.",
            };

            var turn = Turn(advice);
            if (turn.Length > 0)
                lines.Add($"합성기에 넣기 전에 {turn} 만큼 돌려 두어야 이 결과가 나옵니다.");

            lines.Add(advice.Rotatable
                ? "결과는 돌릴 수 있습니다 (재료가 둘 다 돌아가므로)."
                : "결과는 돌릴 수 없습니다 (재료 중 하나가 돌아가지 않으므로).");

            var reach = Reach(advice.Effect);
            if (reach.Length > 0) lines.Add($"결과가 미치는 범위: {reach}");

            if (!advice.Affordable) lines.Add("소지금이 모자랍니다.");

            return lines;
        }

        /// <summary>
        /// 석판이 실제로 미치는 범위. 증가분이 같아 보일 때 무엇이 다른지 이 줄에서 드러난다.
        /// </summary>
        public static string Reach(TabletEffectSummary effect)
        {
            if (effect.IsEmpty) return "";

            var text = effect.RaisedCells > 0 ? $"{effect.RaisedCells}칸 +{effect.RaisedTotal}" : "";
            if (effect.LoweredCells > 0) text += $" −{effect.LoweredTotal}";
            if (effect.DisabledCells > 0) text += $" 막힘{effect.DisabledCells}";
            return text.Trim();
        }

        /// <summary>
        /// 합성 전에 재료를 돌려 놓아야 하는 각도. 돌릴 것이 없으면 빈 문자열이라, 이 값 하나로
        /// 돌릴 것이 있는지까지 답한다. 문장으로 감싸는 것은 부르는 쪽 몫이다.
        /// </summary>
        public static string Turn(MixAdvice advice)
        {
            var parts = new List<string>();
            if (advice.RotationA != 0) parts.Add($"{advice.NameA} {advice.RotationA * 90}°");
            if (advice.RotationB != 0) parts.Add($"{advice.NameB} {advice.RotationB * 90}°");
            return string.Join(", ", parts);
        }

        public static string Join(IReadOnlyList<string> lines) => string.Join(Environment.NewLine, lines);
    }
}
