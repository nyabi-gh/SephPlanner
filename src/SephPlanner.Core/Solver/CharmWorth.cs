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

        /// <summary>잰 값을 아래 한계로만 쓴 것. 능력치 밖에 고유 효과가 더 있는 아티팩트다.</summary>
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

        /// <summary>
        /// 표를 아래 한계로만 쓸지. 능력치 밖에 고유 효과가 더 있는 아티팩트는 표가 값어치의
        /// 일부만 담으므로, 잰 값이 <see cref="Base"/>·<see cref="PerLevel"/> 쪽 어림값보다
        /// 낮다고 해서 낮게 볼 근거가 되지 못한다.
        /// </summary>
        public bool ByLevelIsFloor { get; set; }

        public CharmWorthSource Source { get; set; } = CharmWorthSource.Rarity;

        /// <summary>잰 값이 얼마나 믿을 만한지(0~1). 재지 않은 값에서는 0이다.</summary>
        public double Confidence { get; set; }

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

            if (definition.StatWorthByLevel.Count > 0)
            {
                // 능력치만 주는 아티팩트는 표가 값어치를 다 담는다. 파생 클래스는 그 위에 고유
                // 효과가 더 있으므로 표를 아래 한계로만 쓴다.
                var statsAreEverything = definition.Behavior == "Charm_StatusInstance";
                return new CharmWorth
                {
                    ByLevel = definition.StatWorthByLevel,
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
        private readonly Dictionary<string, CharmValueEntry> _byId =
            new Dictionary<string, CharmValueEntry>(StringComparer.Ordinal);

        public CharmValueBook(CharmValueFile? file)
        {
            foreach (var entry in file?.Charms ?? new List<CharmValueEntry>())
            {
                if (entry.EntityId > 0 && !_byEntity.ContainsKey(entry.EntityId)) _byEntity[entry.EntityId] = entry;
                if (entry.Id.Length > 0 && !_byId.ContainsKey(entry.Id)) _byId[entry.Id] = entry;
            }
        }

        public int Count => _byEntity.Count + _byId.Count;

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
