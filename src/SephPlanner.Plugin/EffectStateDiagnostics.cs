using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace SephPlanner.Plugin
{
    internal static class EffectStateDiagnostics
    {
        internal static void Append(StringBuilder text, GridInventory inventory, PlayerAvatar avatar)
        {
            var stats = avatar.customStats.Keys.Concat(avatar.calculatedBonusStats.Keys).Distinct().OrderBy(id => id).ToList();
            text.AppendLine();
            text.AppendLine("[효과 상호작용 검증]");
            text.AppendLine(JsonConvert.SerializeObject(stats.Select(id => new
            {
                능력치 = id,
                직접기여 = avatar.customStats.TryGetValue(id, out var direct) ? direct : 0,
                계산보너스 = avatar.calculatedBonusStats.TryGetValue(id, out var bonus) ? bonus : 0,
                기본합 = avatar.GetCustomBaseStatUnsafe(id),
                증폭 = avatar.GetCustomStatAmp(id),
                최종값 = avatar.GetCustomStatUnsafe(id),
            }), Formatting.Indented));
            var ordered = inventory.charms.Values.ToList();
            text.AppendLine("[정렬 전 아티팩트 열거 순서]");
            text.AppendLine(JsonConvert.SerializeObject(ordered.Select(charm => charm.Item.InstanceID)));
            ordered.Sort((left, right) => left.Order.CompareTo(right.Order));
            text.AppendLine("[콤보 전 갱신 순서와 관측 카테고리]");
            text.AppendLine(JsonConvert.SerializeObject(ordered.Select(charm => new
            {
                인스턴스 = charm.Item.InstanceID,
                종류 = charm.GetType().Name,
                X = charm.xIdx,
                Y = charm.yIdx,
                활성 = charm.IsEffectEnabled,
                효과레벨 = charm.CurrentLevelToIdx(),
                카테고리 = charm.GetItemCategory().ToArray(),
            }), Formatting.Indented));
            var conversions = new List<object>();
            foreach (var charm in ordered)
            {
                if (charm is Charm_AddStatByAnotherStat conversion)
                    conversions.Add(new
                    {
                        인스턴스 = charm.Item.InstanceID,
                        원본능력치 = conversion.baseStatNameUnsafe,
                        원본표시능력치 = conversion.baseStatID,
                        원본값 = avatar.GetCustomStatUnsafe(conversion.baseStatNameUnsafe),
                        마지막원본값 = Read<int>(conversion, "valueForFlag"),
                        타이머 = TimerState(Read<global::Timer>(conversion, "refreshTimer")),
                        단위 = conversion.perBaseStatByLevel,
                        대상 = conversion.targetStats,
                        적용값 = Read<int[]>(conversion, "addedValues"),
                        적용여부 = Read<bool[]>(conversion, "added"),
                    });
                if (charm is Charm_AddStatByDefense defense)
                    conversions.Add(new
                    {
                        인스턴스 = charm.Item.InstanceID,
                        방어력 = avatar.GetCustomStat(ECustomStat.DamageReduction),
                        마지막원본값 = Read<int>(defense, "valueForFlag"),
                        타이머 = TimerState(Read<global::Timer>(defense, "refreshTimer")),
                        레벨별배수 = defense.addByLevel,
                        적용값 = Read<int>(defense, "addedValue"),
                        적용여부 = Read<bool>(defense, "added"),
                    });
            }
            text.AppendLine("[능력치 전환 적용 상태]");
            text.AppendLine(JsonConvert.SerializeObject(conversions, Formatting.Indented));
        }

        private static object TimerState(global::Timer timer) => new
        {
            경과 = timer.GetTimer(),
            주기 = timer.time,
            발화횟수 = Read<int>(timer, "activeCount"),
            반복초기화 = timer.resetOnTime,
            일회성 = timer.activeOnce,
        };

        private static T Read<T>(object instance, string name)
        {
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return (T)field.GetValue(instance);
            }
            throw new MissingFieldException(instance.GetType().FullName, name);
        }
    }
}
