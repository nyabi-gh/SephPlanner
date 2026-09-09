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

            if (worth.Source == CharmWorthSource.Curated && entry is { Note.Length: > 0 }) lines.Add(entry.Note);

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
