using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Runtime
{
    public sealed class ReplayResult
    {
        public Dictionary<string, string> Facts { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, double> Scores { get; set; } = new Dictionary<string, double>();

        public static ReplayResult From(Plan plan)
        {
            var result = new ReplayResult();
            result.Arrangement("현재", plan.Current);
            result.Arrangement("최선", plan.Best);
            result.Fact("검증", plan.Verification.Status);
            result.Fact("검증/칸", plan.Verification.LevelMismatches);
            result.Fact("검증/레벨", plan.Verification.EffectiveLevelMismatches);
            result.Fact("검증/비활성", plan.Verification.DisabledMismatches);
            result.Fact("검증/석판", plan.Verification.TabletMismatches);
            result.Fact("변경 있음", plan.HasPlacementChanges);
            result.Fact("수동 안내 가능", plan.ManualMoveInstructionsAvailable);
            result.Fact("생략 후보", plan.SkippedOffers);
            for (var i = 0; i < plan.Targets.Count; i++)
            {
                var target = plan.Targets[i];
                result.Fact("적용/" + i, FormattableString.Invariant(
                    $"{target.InstanceId}:{target.IsTablet}:{target.From.X},{target.From.Y}r{target.FromRotation}>{target.To.X},{target.To.Y}r{target.Rotation}"));
            }
            for (var i = 0; i < plan.Offers.Count; i++)
            {
                var offer = plan.Offers[i];
                var key = "후보/" + i;
                result.Fact(key, offer.Key);
                result.Fact(key + "/가능", offer.Available);
                result.Fact(key + "/구매", offer.Affordable);
                result.Fact(key + "/배치", offer.CandidatePlaced);
                result.Fact(key + "/교체", offer.Displacement?.InstanceId ?? 0);
                result.Fact(key + "/콤보", offer.ComboText);
                result.Fact(key + "/프리셋", offer.MatchesPreset);
                result.Fact(key + "/우선", offer.MatchesPriority);
                result.Scores[key + "/이득"] = offer.Gain;
                result.Scores[key + "/콤보"] = offer.ComboBonus;
                if (offer.Preview is null) continue;
                result.Scores[key + "/미리보기"] = offer.Preview.Score;
                foreach (var cell in offer.Preview.Names)
                    result.Fact(key + "/칸/" + cell.Key, cell.Value);
                foreach (var cell in offer.Preview.EffectiveLevels)
                    result.Fact(key + "/레벨/" + cell.Key, cell.Value);
            }
            for (var i = 0; i < plan.Mixes.Count; i++)
            {
                var mix = plan.Mixes[i];
                var key = "합성/" + i;
                result.Fact(key, FormattableString.Invariant($"{mix.InstanceA}r{mix.RotationA}+{mix.InstanceB}r{mix.RotationB}"));
                result.Fact(key + "/구매", mix.Affordable);
                result.Fact(key + "/질의", mix.Query);
                result.Scores[key] = mix.Gain;
            }
            for (var i = 0; i < plan.Discards.Count; i++)
            {
                var discard = plan.Discards[i];
                var key = "빼기/" + i;
                result.Fact(key, discard.InstanceId);
                result.Scores[key] = discard.Gain;
            }
            result.Fact("경고/콤보", string.Join("\n", plan.ComboPlacementWarnings));
            result.Fact("경고/유지", string.Join("\n", plan.RetentionWarnings));
            result.Fact("경고/연결", string.Join("\n", plan.SupportWarnings));
            result.Fact("경고/활성", string.Join("\n", plan.ActivationWarnings));
            result.Fact("끄기 허용 필요", plan.HasUnapprovedDeactivation);
            return result;
        }

        public List<string> Differences(ReplayResult actual)
        {
            var differences = new List<string>();
            foreach (var key in Facts.Keys.Union(actual.Facts.Keys).OrderBy(key => key, StringComparer.Ordinal))
            {
                Facts.TryGetValue(key, out var expected);
                actual.Facts.TryGetValue(key, out var value);
                if (expected != value) differences.Add($"{key}: 저장={expected ?? "없음"}, 재생={value ?? "없음"}");
            }
            foreach (var key in Scores.Keys.Union(actual.Scores.Keys).OrderBy(key => key, StringComparer.Ordinal))
            {
                var hasExpected = Scores.TryGetValue(key, out var expected);
                var hasActual = actual.Scores.TryGetValue(key, out var value);
                // Mono와 .NET의 부동소수 연산 차이만 허용한다. 좌표·순위·상태에는 허용 오차가 없다.
                if (!hasExpected || !hasActual || !Finite(expected) || !Finite(value) || Math.Abs(expected - value) > 1e-8)
                    differences.Add($"{key}: 저장={(hasExpected ? expected.ToString("R", CultureInfo.InvariantCulture) : "없음")}, 재생={(hasActual ? value.ToString("R", CultureInfo.InvariantCulture) : "없음")}");
            }
            return differences;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void Fact(string key, object value) => Facts[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        private void Arrangement(string key, Arrangement arrangement)
        {
            Scores[key] = arrangement.Score;
            Scores[key + "/선택 기준"] = arrangement.Preference;
            Scores[key + "/콤보 진행"] = arrangement.PriorityComboProgress;
            Fact(key + "/콤보 만족", arrangement.PriorityComboMatches);
            Fact(key + "/미배치 석판", arrangement.UnplacedTablets);
            Fact(key + "/비활성", string.Join(",", arrangement.InactiveCharms.OrderBy(id => id)));
            Fact(key + "/고정 실패", string.Join(",", arrangement.UnheldCharms.OrderBy(id => id)));
            if (arrangement.WrongSideCharms.Count > 0)
                Fact(key + "/천칭 방향 실패", string.Join(",", arrangement.WrongSideCharms.OrderBy(id => id)));
            Fact(key + "/유지 실패", string.Join(",", arrangement.UnretainedCharms.OrderBy(id => id)));
            Fact(key + "/활성 보호 실패", string.Join(",", arrangement.UnpreservedCharms.OrderBy(id => id)));
            Fact(key + "/미허용 비활성", string.Join(",", arrangement.UnapprovedDeactivations.OrderBy(id => id)));
            Fact(key + "/감점 빈칸", arrangement.UnsafeEmptyCells);
            Fact(key + "/상한 초과", arrangement.WastedLevels);
            Fact(key + "/연결 실패", string.Join(",", arrangement.UnlinkedCharms.OrderBy(id => id)));
            foreach (var item in arrangement.CharmPositions) Fact(key + "/아이템/" + item.Key, item.Value);
            foreach (var item in arrangement.TabletPositions)
                Fact(key + "/석판/" + item.Key, FormattableString.Invariant($"{item.Value.Position.X},{item.Value.Position.Y}r{item.Value.Rotation}"));
            foreach (var cell in arrangement.CellLevels) Fact(key + "/칸 레벨/" + cell.Key, cell.Value);
            foreach (var cell in arrangement.EffectiveLevels) Fact(key + "/유효 레벨/" + cell.Key, cell.Value);
        }
    }
}
