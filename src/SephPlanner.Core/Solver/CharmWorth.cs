using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    public enum CharmWorthSource
    {
        /// <summary>레어도로 어림잡은 값. 아무 근거가 없는 자리다.</summary>
        Rarity,

        /// <summary>게임의 레벨별 능력치 표에서 잰 값.</summary>
        Measured,

        /// <summary>누락되거나 별도인 효과가 있어 측정값과 레어도 어림값 중 큰 쪽을 쓴 것.</summary>
        MeasuredFloor,

        /// <summary>손으로 채운 값.</summary>
        Curated,
    }

    /// <summary>
    /// 아티팩트 하나가 어떤 레벨에서 얼마나 값어치가 있는지. 단위는 레벨이다.
    ///
    /// 점수 모델은 오랫동안 모든 아티팩트를 "켜져 있으면 1, 레벨 하나에 1"로 세고 레어도로만
    /// 우열을 갈랐다. 그래서 전투 효과가 센 아티팩트와 밋밋한 아티팩트가 같은 값으로 나왔다.
    /// 여기가 그 자리를 대신하며, 근거가 좋은 것부터 차례로 쓴다 - 손으로 채운 값, 게임에서
    /// 잰 값, 마지막이 레어도다. 무엇을 썼는지는 <see cref="Source"/>가 밝힌다.
    /// </summary>
    public sealed class CharmWorth
    {
        /// <summary>
        /// 켜져 있다는 것만으로 갖는 값. 레벨이 0이어도 아티팩트는 작동하므로 0이 아니다.
        /// 이것이 없으면 꺼지는 자리(레벨 음수)와 레벨 0 자리가 똑같이 0점이라, 아티팩트를
        /// 꺼진 채로 두고도 최적이라고 하게 된다.
        /// </summary>
        public double Base { get; set; } = 1;

        /// <summary>레벨 하나가 더 주는 값.</summary>
        public double PerLevel { get; set; } = 1;

        /// <summary>잰 값의 레벨별 표. 있으면 <see cref="Base"/>·<see cref="PerLevel"/>보다 우선한다.</summary>
        public IReadOnlyList<double>? ByLevel { get; set; }
        public IReadOnlyList<double>? BenefitByLevel { get; set; }
        public IReadOnlyList<double>? PenaltyByLevel { get; set; }

        /// <summary>
        /// 표를 아래 한계로만 쓸지. 능력치 밖에 고유 효과가 더 있는 아티팩트는 표가 값어치의
        /// 일부만 담으므로, 잰 값이 <see cref="Base"/>·<see cref="PerLevel"/> 쪽 어림값보다
        /// 낮다고 해서 낮게 볼 근거가 되지 못한다.
        /// </summary>
        public bool ByLevelIsFloor { get; set; }

        public CharmWorthSource Source { get; set; } = CharmWorthSource.Rarity;

        /// <summary>잰 값이 얼마나 믿을 만한지(0~1). 재지 않은 값에서는 0이다.</summary>
        public double Confidence { get; set; }

        /// <summary>
        /// 레벨 한 칸의 크기. 강화처럼 게임이 수치로 말해 주지 않는 이득을 이 아티팩트에 맞는
        /// 크기로 옮길 때 쓴다. 지금 레벨과 무관한 값이라야 최고 레벨에서 0이 되지 않는다.
        /// </summary>
        public double LevelStep
        {
            get
            {
                var step = At(1) - At(0);
                return step > 0 ? step : 0;
            }
        }

        public static double ApplyWeight(double value, double weight) =>
            Math.Max(0, value) * weight + Math.Min(0, value);

        /// <summary>
        /// 선호도 배수를 걸었을 때의 값어치. 배수는 이득에만 걸리고 측정한 패널티는 그대로 남는다.
        ///
        /// <b>하한과 측정값은 각자의 규칙으로 가중한 뒤 큰 쪽을 쓴다.</b> 하한(레어도 어림값)은
        /// 환산하지 못한 효과를 대신하는 이득 추정이라 <see cref="ApplyWeight"/>로 가중하고, 측정한
        /// 표는 이득에만 배수를 걸고 패널티를 더한다. 둘 중 큰 쪽이라는 것은 <see cref="At"/>가
        /// 하는 일과 같고, 배수가 1이면 그 값과 같아진다.
        ///
        /// 예전에는 하한에서 측정 순가치를 뺀 차이를 이득에 더했다. 그 차이에 <b>측정한 패널티가
        /// 부호를 바꿔 섞여 들어가</b> 배수를 함께 받았고, 결과는 패널티가 클수록 점수가 높아지는
        /// 것이었다 - 실드 메이트(★★★)가 레벨 2(103.5)를 레벨 3(88.0)보다 좋게 보고 낮은 칸에
        /// 머물렀다. 패널티가 레벨이 오르며 줄어드는 아티팩트는 전부 이 모양이 된다.
        /// </summary>
        public double WeightedAt(int level, double weight)
        {
            if (level < 0) level = 0;
            if (ByLevel is null || BenefitByLevel is null || PenaltyByLevel is null ||
                ByLevel.Count == 0 || BenefitByLevel.Count != ByLevel.Count || PenaltyByLevel.Count != ByLevel.Count)
                return ApplyWeight(At(level), weight);

            var index = Math.Min(level, ByLevel.Count - 1);
            var measured = BenefitByLevel[index] * weight + PenaltyByLevel[index];
            if (!ByLevelIsFloor) return measured;

            return Math.Max(measured, ApplyWeight(Base + PerLevel * level, weight));
        }

        public double At(int level)
        {
            if (level < 0) level = 0;

            var straight = Base + PerLevel * level;
            if (ByLevel is null || ByLevel.Count == 0) return straight;

            var measured = ByLevel[Math.Min(level, ByLevel.Count - 1)];
            return ByLevelIsFloor ? Math.Max(measured, straight) : measured;
        }

        /// <summary>
        /// 다섯 칸의 기준값. 측정된 순수 능력치 아티팩트 110종의 분포에서 10·25·50·75·90 분위를
        /// 그대로 가져왔다(1.0.30). 사람이 매기는 등급이라도 칸의 크기는 짐작이 아니어야 한다.
        /// <c>--values</c>가 지금 덤프 기준의 분위를 다시 찍어 주므로 패치 뒤 다시 맞출 수 있다.
        /// </summary>
        private static readonly (double Base, double PerLevel)[] Tiers =
        {
            (0.00, 0.67),
            (1.00, 1.00),
            (1.67, 1.46),
            (2.03, 2.00),
            (3.34, 2.49),
        };

        public const int MaxTier = 5;

        public static (double Base, double PerLevel) OfTier(int tier) =>
            tier >= 1 && tier <= Tiers.Length ? Tiers[tier - 1] : (1.0, 1.0);

        /// <summary>
        /// 한 아티팩트의 값어치를 정한다. <paramref name="curated"/>가 있으면 그것이 마지막
        /// 말이고, 없으면 덤프가 실어 온 측정 표, 그것도 없으면 레어도다.
        /// </summary>
        public static CharmWorth Resolve(CharmDefinition definition, CharmValueEntry? curated = null)
        {
            var fallback = Worth.OfRarity(definition.Rarity);

            if (curated is not null && (curated.Base.HasValue || curated.PerLevel.HasValue || curated.Tier > 0))
            {
                var (tierBase, tierPerLevel) = OfTier(curated.Tier);
                return new CharmWorth
                {
                    Base = curated.Base ?? tierBase,
                    PerLevel = curated.PerLevel ?? tierPerLevel,
                    Source = CharmWorthSource.Curated,
                };
            }

            if (definition.HasNoActivationEffect)
                return new CharmWorth { Base = 0, PerLevel = 0, Source = CharmWorthSource.Measured, Confidence = 1 };

            if (definition.StatWorthByLevel.Count > 0)
            {
                // 능력치형이라도 전환·해금 같은 고정 효과는 환산되지 않을 수 있다.
                // 누락된 효과를 0점으로 단정하지 않고 기존 레어도 어림값을 함께 쓴다.
                var statsAreEverything = definition.Behavior == "Charm_StatusInstance" &&
                                         definition.StatWorthCoverageKnown &&
                                         definition.StatWorthUnconverted.Count == 0;
                return new CharmWorth
                {
                    ByLevel = definition.StatWorthByLevel,
                    BenefitByLevel = definition.StatBenefitByLevel,
                    PenaltyByLevel = definition.StatPenaltyByLevel,
                    ByLevelIsFloor = !statsAreEverything,
                    Base = fallback,
                    PerLevel = fallback,
                    Confidence = definition.StatWorthConfidence,
                    Source = statsAreEverything ? CharmWorthSource.Measured : CharmWorthSource.MeasuredFloor,
                };
            }

            // 레어도 어림값. 켜져 있는 값과 레벨 하나의 값에 같은 배수를 걸던 예전 모델 그대로다.
            return new CharmWorth { Base = fallback, PerLevel = fallback, Source = CharmWorthSource.Rarity };
        }
    }

    /// <summary>엔티티 번호나 식별자로 손으로 채운 가치 항목을 찾는다.</summary>
    public sealed class CharmValueBook
    {
        public static readonly CharmValueBook Empty = new CharmValueBook(null);

        private readonly Dictionary<int, CharmValueEntry> _byEntity = new Dictionary<int, CharmValueEntry>();
        private readonly List<CharmValueEntry> _entries = new List<CharmValueEntry>();
        private readonly Dictionary<string, CharmValueEntry> _byId =
            new Dictionary<string, CharmValueEntry>(StringComparer.Ordinal);

        public CharmValueBook(CharmValueFile? file)
        {
            foreach (var entry in file?.Charms ?? new List<CharmValueEntry>())
            {
                _entries.Add(entry);
                if (entry.EntityId > 0 && !_byEntity.ContainsKey(entry.EntityId)) _byEntity[entry.EntityId] = entry;
                if (entry.Id.Length > 0 && !_byId.ContainsKey(entry.Id)) _byId[entry.Id] = entry;
            }
        }

        public int Count => _byEntity.Count + _byId.Count;

        public CharmValueFile Export() => new CharmValueFile
        {
            Version = 1,
            Charms = new List<CharmValueEntry>(_entries),
        };

        internal IEnumerable<KeyValuePair<int, CharmValueEntry>> EntityValues => _byEntity;
        internal IEnumerable<KeyValuePair<string, CharmValueEntry>> IdValues => _byId;

        /// <summary>
        /// 엔티티 번호를 먼저 본다. 게임 패치로 번호가 밀리면 식별자가 받아 주는데, 그때
        /// 번호로 잘못 찾는 일이 없도록 번호가 가리킨 항목의 식별자가 다르면 식별자를 믿는다.
        /// </summary>
        public CharmValueEntry? Of(CharmDefinition definition)
        {
            if (_byEntity.TryGetValue(definition.EntityId, out var byEntity)
                && (byEntity.Id.Length == 0 || definition.Id.Length == 0 || byEntity.Id == definition.Id))
                return byEntity;

            return definition.Id.Length > 0 && _byId.TryGetValue(definition.Id, out var byId) ? byId : null;
        }
    }
}
