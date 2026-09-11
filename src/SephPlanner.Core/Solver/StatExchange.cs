using System;
using System.Collections.Generic;
using System.Linq;

namespace SephPlanner.Core.Solver
{
    /// <summary>환산율을 맞출 때 쓰는 아티팩트 신원. 능력치 표만 봐서는 알 수 없는 것들이다.</summary>
    public sealed class CharmStatProfile
    {
        public int EntityId { get; set; }

        /// <summary>게임의 레벨 상한. 모르면 -1 이고, 그때는 표 전체를 쓴다.</summary>
        public int MaxLevel { get; set; } = -1;

        /// <summary>
        /// 능력치 표가 이 아티팩트의 값어치 전부인가. 고유 효과가 따로 있으면 레벨 예산이
        /// 능력치만의 것이 아니라 척도를 정하는 표본이 못 된다.
        /// </summary>
        public bool StatsAreEverything { get; set; }
    }

    /// <summary>수축 세기 하나를 표본 밖에서 시험한 결과.</summary>
    public sealed class ShrinkageTrial
    {
        public double Weight { get; set; }

        /// <summary>겹을 빼고 맞춘 뒤 그 겹에서 잰 평균제곱오차.</summary>
        public double CrossValidated { get; set; }

        /// <summary>겹 사이의 표준오차. 최소와 이만큼 안에 드는 것은 구별되지 않는다.</summary>
        public double StandardError { get; set; }

        public double InSample { get; set; }
    }

    /// <summary>
    /// 능력치를 "아티팩트 레벨 몇 개어치인가"로 옮기는 환산율.
    ///
    /// 아티팩트도 콤보도 같은 <c>StatusDatabase</c> 체계를 쓴다는 점이 다리다. 능력치별로
    /// "레벨 하나가 이 능력치를 얼마 올려 주는가"를 구해 두면, 능력치로 표현된 것은 무엇이든
    /// 레벨 단위로 옮길 수 있다. 콤보 가중치(<see cref="ComboWorthMeasure"/>)와 아티팩트
    /// 가치(<see cref="CharmStatWorth"/>)가 같은 표를 쓰므로 두 값이 같은 자로 잰 값이 된다.
    ///
    /// <b>능력치 하나만 떼어 세면 안 된다.</b> 표본 아티팩트는 레벨 하나로 능력치를 여러 개
    /// 동시에 사므로, 능력치마다 따로 재면 같은 레벨을 능력치 수만큼 중복해서 세게 된다.
    /// 그러면 묶여 나오는 능력치일수록 환산율이 작게 나오고 단위값이 부풀려진다 - 1.0.31
    /// 자료에서 잰 값 106종의 레벨당 값어치 중앙값이 정의상 1 이어야 하는데 1.50 이었다.
    /// <see cref="From"/>은 아티팩트 하나의 레벨 예산을 그 아티팩트가 산 능력치들에게 나눠 준다.
    /// </summary>
    public sealed class StatExchange
    {
        /// <summary>능력치별 레벨 하나당 증가분. 아무 아티팩트도 주지 않는 능력치는 없다.</summary>
        public Dictionary<string, double> PerLevel { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>나누기 전, 능력치 하나만 떼어 센 값. 얼마나 움직였는지 보라고 남긴다.</summary>
        public Dictionary<string, double> SeedPerLevel { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>그 환산율을 뒷받침한 아티팩트 수. 1이면 그 아티팩트 자신뿐이라 환산이 동어반복이다.</summary>
        public Dictionary<string, int> Samples { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>척도와 보정을 정한 표본 - 능력치 표가 값어치 전부인 아티팩트 수.</summary>
        public int ScaleSamples { get; private set; }

        /// <summary>능력치별로 따로 보정한 수. 나머지는 전체 척도만 받는다.</summary>
        public int FittedStats { get; private set; }

        /// <summary>나누기 전 척도가 얼마나 부풀어 있었는지. 1 이면 부풀지 않았다는 뜻이다.</summary>
        public double Scale { get; private set; } = 1;

        /// <summary>
        /// 환산율을 믿을 만하다고 볼 최소 표본 수. 표본이 이보다 적으면 환산은 되지만 그 값은
        /// 사실상 "평균쯤 되겠거니"라는 뜻이므로, 결과에 그렇게 표시한다.
        /// </summary>
        public const int ReliableSamples = 3;

        /// <summary>
        /// 능력치별 보정을 전체 척도 쪽으로 끌어당기는 세기.
        ///
        /// <b>보정을 자유롭게 두면 안 된다.</b> 표본 106종으로 능력치 75개를 맞추는데 대부분의
        /// 능력치는 한두 종에만 나온다. 자유롭게 두면 표본이 적은 능력치가 그 아티팩트의 남는
        /// 몫을 전부 가져가며 발산한다(1.0.31 자료의 <c>FINAL_DAMAGE</c> - 조화의 수정이 탄다).
        ///
        /// 교차검증으로 골랐다. 표본 밖 평균제곱오차가 1 에서 가장 낮고(0.346), 자유롭게 둔
        /// 쪽(0.1)은 0.40 으로 아무 보정도 안 한 쪽(0.59)보다 나쁘다 - 과적합이다. 표준오차
        /// 한 칸 규칙은 5 를 고르지만 그쪽은 토끼마을 경비병 투구의 비단조를 못 푼다.
        /// <c>--measure</c> 가 이 곡선을 다시 찍어 준다.
        /// </summary>
        public const double ShrinkageWeight = 1.0;

        public bool IsReliable(string statusId) =>
            Samples.TryGetValue(statusId, out var count) && count >= ReliableSamples;

        /// <summary>레벨 단위로 옮긴 값. 환산율이 없는 능력치는 <c>false</c>.</summary>
        public bool TryConvert(string statusId, double value, out double levels)
        {
            levels = 0;
            if (!PerLevel.TryGetValue(statusId, out var perLevel) || perLevel <= 0) return false;

            levels = value / perLevel;
            return true;
        }

        /// <summary>
        /// 아티팩트들의 레벨별 능력치 표에서 환산율을 뽑는다.
        ///
        /// 세 걸음이다. 능력치마다 아티팩트별로 재어 중앙값을 씨앗으로 삼고, 능력치 표가
        /// 값어치 전부인 아티팩트들의 레벨당 값어치 중앙값이 1 이 되도록 전체 척도를 잡고,
        /// 표본이 받쳐 주는 만큼만 움직이도록 <see cref="ShrinkageWeight"/>로 수축한
        /// 능력치별 보정을 얹는다. <paramref name="profiles"/>가 비어 있으면 척도를 정할
        /// 표본이 없어 씨앗을 그대로 쓴다.
        /// </summary>
        public static StatExchange From(
            IEnumerable<CharmStatTable> tables, IEnumerable<CharmStatProfile>? profiles = null)
        {
            var design = Design.Build(tables, profiles);
            var exchange = new StatExchange { ScaleSamples = design.Rows.Count };
            foreach (var pair in design.Seed)
            {
                exchange.SeedPerLevel[pair.Key] = pair.Value;
                exchange.PerLevel[pair.Key] = pair.Value;
            }
            foreach (var pair in design.SampleCount) exchange.Samples[pair.Key] = pair.Value;
            if (design.Rows.Count == 0) return exchange;

            exchange.Scale = design.Scale;
            var correction = Ridge(design.Rows, design.Stats.Count, ShrinkageWeight);
            foreach (var statusId in design.Seed.Keys.ToList())
            {
                var scaled = design.Seed[statusId] * design.Scale;
                if (!design.Column.TryGetValue(statusId, out var column) || correction[column] <= 0)
                {
                    exchange.PerLevel[statusId] = scaled;
                    continue;
                }
                exchange.PerLevel[statusId] = scaled / correction[column];
                exchange.FittedStats++;
            }

            // 보정은 제곱오차를 줄이는 쪽이라 중앙값을 1 에 두지 않는다. 눈금을 다시 맞춘다.
            var renormalize = Median(design.Rises
                .Select(rises => Design.LevelWorth(rises, exchange.PerLevel))
                .ToList());
            if (renormalize > 0)
                foreach (var statusId in exchange.PerLevel.Keys.ToList())
                    exchange.PerLevel[statusId] *= renormalize;

            return exchange;
        }

        /// <summary>
        /// 수축 세기를 표본 밖에서 시험한다. 겹 나누기는 색인 순서라 같은 자료에서 늘 같은
        /// 답이 나온다.
        /// </summary>
        public static List<ShrinkageTrial> CrossValidate(
            IEnumerable<CharmStatTable> tables, IEnumerable<CharmStatProfile>? profiles,
            IReadOnlyList<double> weights, int folds = 10)
        {
            var design = Design.Build(tables, profiles);
            var trials = new List<ShrinkageTrial>();
            if (folds < 2 || design.Rows.Count < folds) return trials;

            foreach (var weight in weights)
            {
                var byFold = new double[folds];
                for (var fold = 0; fold < folds; fold++)
                {
                    var training = design.Rows.Where((_, index) => index % folds != fold).ToList();
                    var correction = Ridge(training, design.Stats.Count, weight);
                    var held = 0;
                    for (var index = fold; index < design.Rows.Count; index += folds)
                    {
                        var error = design.Predict(index, correction) - 1;
                        byFold[fold] += error * error;
                        held++;
                    }
                    if (held > 0) byFold[fold] /= held;
                }

                var fitted = Ridge(design.Rows, design.Stats.Count, weight);
                double inSample = 0;
                for (var index = 0; index < design.Rows.Count; index++)
                {
                    var error = design.Predict(index, fitted) - 1;
                    inSample += error * error;
                }

                var mean = byFold.Average();
                trials.Add(new ShrinkageTrial
                {
                    Weight = weight,
                    CrossValidated = mean,
                    StandardError = Math.Sqrt(
                        byFold.Sum(value => (value - mean) * (value - mean)) / (folds - 1) / folds),
                    InSample = inSample / design.Rows.Count,
                });
            }
            return trials;
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;

            var sorted = values.OrderBy(value => value).ToList();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        /// <summary>
        /// "능력치만 주는 아티팩트는 레벨 하나가 값어치 1" 이라는 식을 표본마다 한 줄씩 세운
        /// 것. 미지수는 능력치별 보정이고, 씨앗 환산율에 전체 척도를 곱한 값이 보정 1 이다.
        /// </summary>
        private sealed class Design
        {
            public readonly Dictionary<string, double> Seed = new Dictionary<string, double>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> SampleCount = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Column = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<Dictionary<string, double>> Rises = new List<Dictionary<string, double>>();
            public readonly List<(int Column, double Value)[]> Rows = new List<(int, double)[]>();
            public List<string> Stats = new List<string>();
            public double Scale = 1;

            public double Predict(int row, double[] correction)
            {
                double sum = 0;
                foreach (var cell in Rows[row]) sum += cell.Value * correction[cell.Column];
                return sum;
            }

            public static double LevelWorth(
                Dictionary<string, double> rises, Dictionary<string, double> perLevel)
            {
                double sum = 0;
                foreach (var rise in rises)
                    if (perLevel.TryGetValue(rise.Key, out var rate) && rate > 0)
                        sum += rise.Value / rate;
                return sum;
            }

            public static Design Build(
                IEnumerable<CharmStatTable> tables, IEnumerable<CharmStatProfile>? profiles)
            {
                var design = new Design();
                var byEntity = new Dictionary<int, List<CharmStatTable>>();
                foreach (var table in tables)
                {
                    if (!byEntity.TryGetValue(table.EntityId, out var list))
                        byEntity[table.EntityId] = list = new List<CharmStatTable>();
                    list.Add(table);
                }

                var profileOf = new Dictionary<int, CharmStatProfile>();
                foreach (var profile in profiles ?? Enumerable.Empty<CharmStatProfile>())
                    profileOf[profile.EntityId] = profile;

                var entities = byEntity.Keys.OrderBy(id => id).ToList();
                var tops = new Dictionary<int, int>();
                foreach (var entity in entities)
                {
                    var span = byEntity[entity].Max(table => table.ValuesByLevel.Count);
                    var cap = profileOf.TryGetValue(entity, out var profile) ? profile.MaxLevel : -1;
                    tops[entity] = cap >= 0 ? Math.Min(cap, span - 1) : span - 1;
                }

                // 걸음별로 재면 중간에 안 오르는 레벨이 있는 아티팩트의 환산율이 부풀려진다.
                var samples = new Dictionary<string, List<double>>(StringComparer.Ordinal);
                foreach (var entity in entities)
                {
                    foreach (var table in byEntity[entity])
                    {
                        if (table.FromCode) continue;

                        var rise = RisePerLevel(table, tops[entity]);
                        if (rise <= 0) continue;

                        if (!samples.TryGetValue(table.StatusId, out var list))
                            samples[table.StatusId] = list = new List<double>();
                        list.Add(rise);
                    }
                }
                foreach (var pair in samples)
                {
                    design.Seed[pair.Key] = Median(pair.Value);
                    design.SampleCount[pair.Key] = pair.Value.Count;
                }

                foreach (var entity in entities)
                {
                    if (tops[entity] < 1) continue;
                    if (!profileOf.TryGetValue(entity, out var profile) || !profile.StatsAreEverything) continue;

                    // 환산하지 못하는 능력치가 있으면 레벨 예산에 값을 못 매긴 몫이 섞여 있다.
                    var rises = new Dictionary<string, double>(StringComparer.Ordinal);
                    var covered = true;
                    foreach (var table in byEntity[entity])
                    {
                        if (table.FromCode) { covered = false; break; }

                        if (!design.Seed.ContainsKey(table.StatusId))
                        {
                            if (table.ValuesByLevel.Any(value => value != 0)) covered = false;
                            continue;
                        }
                        var rise = RisePerLevel(table, tops[entity]);
                        if (rise != 0) rises[table.StatusId] = rise;
                    }
                    if (!covered || rises.Count == 0) continue;

                    design.Rises.Add(rises);
                    foreach (var statusId in rises.Keys) design.Column[statusId] = 0;
                }
                if (design.Rises.Count == 0) return design;

                design.Stats = design.Column.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
                for (var index = 0; index < design.Stats.Count; index++)
                    design.Column[design.Stats[index]] = index;

                design.Scale = Median(design.Rises
                    .Select(rises => rises.Sum(rise => rise.Value / design.Seed[rise.Key]))
                    .ToList());
                if (design.Scale <= 0)
                {
                    design.Scale = 1;
                    design.Rises.Clear();
                    design.Column.Clear();
                    design.Stats.Clear();
                    return design;
                }

                foreach (var rises in design.Rises)
                    design.Rows.Add(rises
                        .Select(rise => (Column: design.Column[rise.Key],
                            Value: rise.Value / (design.Seed[rise.Key] * design.Scale)))
                        .OrderBy(cell => cell.Column)
                        .ToArray());
                return design;
            }

            private static double RisePerLevel(CharmStatTable table, int top)
            {
                if (table.ValuesByLevel.Count == 0 || top < 1) return 0;

                var last = table.ValuesByLevel[Math.Min(top, table.ValuesByLevel.Count - 1)];
                return (last - (double)table.ValuesByLevel[0]) / top;
            }
        }

        /// <summary>
        /// 능력치별 보정을 정규방정식으로 푼다. 대각선에 얹는 <paramref name="weight"/>가 보정을
        /// 1 쪽으로 끌어당기며, 그 덕에 행렬이 항상 풀린다.
        /// </summary>
        private static double[] Ridge(
            IReadOnlyList<(int Column, double Value)[]> rows, int columns, double weight)
        {
            var normal = new double[columns, columns];
            var target = new double[columns];
            foreach (var row in rows)
                for (var i = 0; i < row.Length; i++)
                {
                    target[row[i].Column] += row[i].Value;
                    for (var j = i; j < row.Length; j++)
                    {
                        var product = row[i].Value * row[j].Value;
                        normal[row[i].Column, row[j].Column] += product;
                        if (j != i) normal[row[j].Column, row[i].Column] += product;
                    }
                }
            for (var i = 0; i < columns; i++)
            {
                normal[i, i] += weight;
                target[i] += weight;
            }
            return Solve(normal, target, columns);
        }

        /// <summary>부분 추축을 쓰는 가우스 소거. 풀리지 않는 자리는 보정 없음(1)으로 둔다.</summary>
        private static double[] Solve(double[,] matrix, double[] target, int size)
        {
            for (var column = 0; column < size; column++)
            {
                var pivot = column;
                for (var row = column + 1; row < size; row++)
                    if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
                if (Math.Abs(matrix[pivot, column]) < 1e-12) continue;

                if (pivot != column)
                {
                    for (var k = column; k < size; k++)
                        (matrix[column, k], matrix[pivot, k]) = (matrix[pivot, k], matrix[column, k]);
                    (target[column], target[pivot]) = (target[pivot], target[column]);
                }

                var head = matrix[column, column];
                for (var row = 0; row < size; row++)
                {
                    if (row == column) continue;

                    var factor = matrix[row, column] / head;
                    if (factor == 0) continue;

                    for (var k = column; k < size; k++) matrix[row, k] -= factor * matrix[column, k];
                    target[row] -= factor * target[column];
                }
            }

            var solution = new double[size];
            for (var index = 0; index < size; index++)
                solution[index] = Math.Abs(matrix[index, index]) > 1e-12
                    ? target[index] / matrix[index, index]
                    : 1;
            return solution;
        }
    }
}
