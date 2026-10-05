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
                lines.Add("종이끼리 연결하면 옮긴 뒤의 콤보가 예상과 다를 수 있습니다.");

            if (worth.Source == CharmWorthSource.Curated && entry is { Note.Length: > 0 }) lines.Add(entry.Note);

            // 점수는 진행한 만큼 올라가는데 쪽지에 이유가 없으면, 같은 아티팩트가 왜 다르게
            // 매겨지는지 알 길이 없다. 진행도는 인스턴스마다 달라 여기서 숫자를 적지는 못한다.
            if (ScalesPosition.IsSideBound(definition))
                lines.Add("지금 놓인 쪽을 유지합니다. 반대쪽 효과를 원하면 직접 옮기세요. 왼쪽은 1~3열, 오른쪽은 4열부터입니다.");
            if (ScalesPosition.FollowsPriority(definition))
                lines.Add("새로 얻어 아직 옮기지 않은 천칭은 우선 콤보에 잉걸불만 있으면 왼쪽, 빙하만 있으면 "
                          + "오른쪽으로 보냅니다.");

            if (definition.GrowthQuestGoal > 0)
                lines.Add("성장이 끝나면 바뀌는 아티팩트와 현재 성장 정도를 고려해 추천합니다.");

            if (definition.ContextStats.Count > 0 && worth.Source != CharmWorthSource.Curated)
            {
                if (definition.ContextStats.Exists(bonus => !bonus.WorthPerUnit.HasValue))
                    lines.Add("일부 효과는 정확히 비교하기 어려워 추천이 실제 체감과 다를 수 있습니다.");
                return lines;
            }

            if (definition.MagicSupport is not null)
            {
                lines.Add(SupportDirection(definition.MagicSupport) + "의 사용 가능한 마법과 연결돼야 효과를 냅니다.");
                lines.Add(worth.Source == CharmWorthSource.Curated
                    ? "연결된 마법을 강화하도록 배치합니다."
                    : definition.MagicSupport.Effect == MagicSupportEffect.ManaCostReduction
                        ? "마나를 아끼는 효과를 고려해 추천합니다. 주로 쓰는 마법에 맞춰 선택하세요."
                        : "마법을 더 자주 쓰는 효과를 고려해 추천합니다. 주로 쓰는 마법에 맞춰 선택하세요.");
                return lines;
            }

            if (worth.Source == CharmWorthSource.Rarity)
                lines.Add("이 아이템은 희귀도를 기준으로 추천합니다. 실제 효과가 더 좋은지는 직접 확인하세요.");

            if (worth.Source == CharmWorthSource.MeasuredFloor)
                lines.Add("일부 효과만 비교할 수 있어 추천이 실제 체감과 다를 수 있습니다.");
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
                ? "다른 아이템과 효과를 비교할 자료가 부족합니다. 추천보다 실제 체감을 우선하세요."
                : "일부 효과를 비교할 자료가 부족합니다. 추천보다 실제 체감을 우선하세요.");
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
                    lines.Add("침은 꺼져 있어도 공격 대상과 연결되면 일부 피해 보너스가 남습니다.");
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

        /// <summary>
        /// 다음 칸이 열렸을 때의 증가분이 지금 값과 눈에 띄게 다른가. 한 자리까지 적는 화면에서
        /// 같은 숫자가 두 번 나오면 잡음이므로, 그만큼 벌어졌을 때만 말한다.
        /// </summary>
        private const double SoonVisible = 0.05;

        public static bool HasSoon(double gain, double? soon) =>
            soon.HasValue && Math.Abs(soon.Value - gain) >= SoonVisible;

        /// <summary>줄 오른쪽에 붙는 짧은 표. 감쌀지 말지는 부르는 쪽이 정한다.</summary>
        public static string SoonTag(double gain, double? soon) =>
            HasSoon(gain, soon) ? $"다음 칸 {soon!.Value:+0.#;-0.#;0}" : "";

        /// <summary>쪽지에 들어가는 한 줄. 왜 순위가 지금 증가분과 어긋나 보이는지를 답한다.</summary>
        public static string SoonLine(double gain, double? soon) =>
            HasSoon(gain, soon)
                ? $"가방이 한 칸 더 열렸을 때의 예상 점수 변화는 {soon!.Value:+0.#;-0.#;0}입니다. 추천 순위는 이때의 이득을 기준으로 합니다."
                : "";

        public static string Level(int level) => level > 0 ? "+" + level : level.ToString();

        /// <summary>
        /// 같은 종류 여럿을 한 줄로 묶었을 때의 레벨. 가장 높은 것 하나만 적으면 나머지도 그
        /// 레벨인 줄 안다 - 제보 ae100e4c 는 +3 을 보고 레벨 2 인 셋째를 배치 탓으로 여겼다.
        /// </summary>
        public static string LevelRange(int lowest, int highest) =>
            lowest == highest ? Level(lowest) : Level(lowest) + "~" + Level(highest);

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

            var soon = SoonLine(advice.Gain, advice.SoonGain);
            if (soon.Length > 0) lines.Add(soon);

            return lines;
        }

        /// <summary>
        /// 제단에서 어느 아티팩트에 걸지. 순위에 안 드러나는 것 - 상한까지 얼마가 남았는지와,
        /// 이 인챈트로 무엇이 되살아나는지 - 를 말한다.
        /// </summary>
        public static List<string> Enchant(EnchantAdvice advice)
        {
            var lines = new List<string>
            {
                $"인챈트 {advice.Enchant - 1} → {advice.Enchant} (상한 {advice.MaxEnchant}). "
                + "인챈트는 아이템을 따라다니므로 자리를 옮겨도 남습니다.",
            };

            if (advice.Enchant >= advice.MaxEnchant)
                lines.Add("이 아티팩트가 받을 수 있는 마지막 인챈트입니다.");

            if (advice.Activated.Count > 0)
                lines.Add("이 인챈트로 효과가 되살아납니다: " + string.Join(", ", advice.Activated));

            var soon = SoonLine(advice.Gain, advice.SoonGain);
            if (soon.Length > 0) lines.Add(soon);

            return lines;
        }

        /// <summary>합성 재료를 격자에서 짚는 표. 글꼴에 없으면 화면이 다른 표를 넘긴다.</summary>
        public const string FirstMark = "①";
        public const string SecondMark = "②";

        /// <summary>① 과 ② 의 이름. ① 은 먼저 넣어 돌릴 쪽이다(<see cref="MixAdvice.TurnBFirst"/>).</summary>
        public static (string First, string Second) MixNames(MixAdvice advice) =>
            advice.TurnBFirst ? (advice.NameB, advice.NameA) : (advice.NameA, advice.NameB);

        public static List<string> Mix(MixAdvice advice, string first = FirstMark, string second = SecondMark)
        {
            var (one, two) = MixNames(advice);
            var lines = new List<string>
            {
                $"{first} {With(one, "과", "와")} {second} {With(two, "을", "를")} 합칩니다. 재료 둘은 사라집니다.",
                Turn(advice, first, second),
                advice.Rotatable ? "결과는 돌릴 수 있습니다."
                : advice.RotatableA != advice.RotatableB ? "한쪽이 돌아가지 않아 결과는 돌릴 수 없습니다. 합칠 때 게임이 한 번 더 묻습니다."
                : "결과는 돌릴 수 없습니다.",
            };

            var reach = Reach(advice.Effect);
            if (reach.Length > 0) lines.Add($"결과가 미치는 범위: {reach}");

            if (!advice.Affordable) lines.Add("소지금이 모자랍니다.");

            var soon = SoonLine(advice.Gain, advice.SoonGain);
            if (soon.Length > 0) lines.Add(soon);

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
        /// 합성 창에서 할 일. 창은 칸마다 넣은 석판 하나를 가방에 놓인 방향으로 받고, 우클릭 한 번에
        /// 그 칸만 90° 돌린다(<c>UI_TabletMixPanel</c>). 돌릴 것을 먼저 넣고 돌리게 하면 창에 석판이
        /// 하나뿐이라 같은 석판 둘이어도 어느 쪽을 돌릴지 헷갈리지 않는다.
        /// </summary>
        public static string Turn(MixAdvice advice, string first = FirstMark, string second = SecondMark)
        {
            if (advice.Turns == 0) return "합성 창에 넣은 방향 그대로 합치면 됩니다.";

            var (one, two) = MixNames(advice);
            return $"합성 창에 {first} {With(one, "을", "를")} 먼저 넣고 우클릭 {advice.Turns}번 돌린 뒤 " +
                   $"{second} {With(two, "을", "를")} 넣으세요(한 번에 90°).";
        }

        /// <summary>합성 추천 줄에 붙는 짧은 표. 자세한 것은 <see cref="Turn"/>이 쪽지에서 말한다.</summary>
        public static string TurnTag(MixAdvice advice, string first = FirstMark) =>
            advice.Turns == 0 ? "" : $"{first} 회전 {advice.Turns}번";

        /// <summary>
        /// 받침을 보고 조사를 붙인다. 한글로 끝나지 않는 이름은 받침을 알 수 없어 둘을 함께 적는다.
        /// </summary>
        internal static string With(string word, string afterConsonant, string afterVowel)
        {
            if (word.Length == 0) return afterConsonant + "(" + afterVowel + ")";
            var last = word[word.Length - 1];
            if (last < '가' || last > '힣') return $"{word}{afterConsonant}({afterVowel})";
            return word + ((last - '가') % 28 != 0 ? afterConsonant : afterVowel);
        }

        public static string Join(IReadOnlyList<string> lines) => string.Join(Environment.NewLine, lines);
    }
}
