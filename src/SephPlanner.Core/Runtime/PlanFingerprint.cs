using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.Core.Runtime
{
    public static class PlanFingerprint
    {
        public static string Full(
            GameSnapshot snapshot, PlanPreferences preferences, string catalogGeneration,
            string? placementFingerprint = null)
        {
            var builder = new StringBuilder();
            Add(builder, "placement", placementFingerprint ?? Placement(snapshot, preferences, catalogGeneration));
            Add(builder, "recommendations", preferences.Recommendations);
            if (!preferences.Recommendations) return Hash(builder);

            // 소지금 자체는 넣지 않는다. 계획이 소지금을 보는 곳은 "지금 살 수 있는가" 하나뿐인데,
            // 값을 그대로 넣으면 동전 한 닢을 주울 때마다 판 전체가 다시 풀린다. 살 수 있는지가
            // 실제로 뒤집힐 때만 다시 푼다.
            var gold = snapshot.Run?.Gold ?? 0;

            Add(builder, "mixer", snapshot.Mixer is not null);

            // 가까워지면 합성 추천이 새로 도므로 지문이 그 순간을 잡아야 한다. 재지 않은 옛
            // 자료에서는 이 줄 자체가 없어야 그때의 지문과 같다.
            if (snapshot.Mixer?.Near is bool near) Add(builder, "mixerNear", near);
            Add(builder, "mixerCost", snapshot.Mixer?.Cost ?? 0);
            Add(builder, "mixerUsed", snapshot.Mixer?.Used ?? false);
            Add(builder, "mixerAffordable", (snapshot.Mixer?.Cost ?? 0) <= gold);

            foreach (var category in preferences.PriorityCategories.OrderBy(value => value, StringComparer.Ordinal))
                Add(builder, "priority", category);
            foreach (var charm in preferences.PresetCharms.OrderBy(value => value))
                Add(builder, "preset", charm);
            foreach (var offer in snapshot.Offers
                         .OrderBy(value => value.Kind, StringComparer.Ordinal)
                         .ThenBy(value => value.DefinitionId)
                         .ThenBy(value => value.SlotIndex)
                         .ThenBy(value => value.Price))
            {
                Add(builder, "offerKind", offer.Kind);
                Add(builder, "offerDefinition", offer.DefinitionId);
                Add(builder, "offerSlot", offer.SlotIndex);
                Add(builder, "offerPrice", offer.Price);
                Add(builder, "offerAffordable", offer.Price <= gold);
            }
            return Hash(builder);
        }

        public static string Placement(
            GameSnapshot snapshot, PlanPreferences preferences, string catalogGeneration) =>
            Placement(snapshot, PlanningContext(preferences, catalogGeneration));

        public static string Placement(GameSnapshot snapshot, string planningContextFingerprint)
        {
            var builder = new StringBuilder();
            Add(builder, "context", planningContextFingerprint);
            Add(builder, "gameVersion", snapshot.GameVersion);
            Add(builder, "weapon", snapshot.Run?.WeaponId ?? "");

            var inventory = snapshot.Inventory;
            Add(builder, "inventory", inventory is not null);
            if (inventory is null) return Hash(builder);

            Add(builder, "width", inventory.Width);
            Add(builder, "height", inventory.Height);
            Add(builder, "storage", inventory.Storage);

            foreach (var item in inventory.Items
                         .OrderBy(value => value.InstanceId).ThenBy(value => value.DefinitionId)
                         .ThenBy(value => value.Position.Y).ThenBy(value => value.Position.X)
                         .ThenBy(value => value.EffectiveLevel).ThenBy(value => value.IsActive)
                         .ThenBy(value => value.Enchant))
            {
                Add(builder, "itemDefinition", item.DefinitionId);
                Add(builder, "itemInstance", item.InstanceId);
                Add(builder, "itemX", item.Position.X);
                Add(builder, "itemY", item.Position.Y);
                Add(builder, "itemLevel", item.EffectiveLevel);
                Add(builder, "itemActive", item.IsActive);
                Add(builder, "itemEnchant", item.Enchant);

                // 진행도를 그대로 넣으면 가드 한 번마다 계획이 낡는다. 점수가 보는 것과 같은
                // 칸만 넣어, 값어치가 실제로 달라질 때만 다시 풀게 한다.
                //
                // 성장하는 아이템이 있을 때만 더한다. 무조건 넣으면 이 항목이 없던 시절의 F10
                // 재현 자료가 전부 지문 불일치로 거부된다 - 그 가방에는 성장 아이템이 없었으므로
                // 넣지 않는 것이 그때의 사실과도 같다.
                if (item.GrowthGoal > 0)
                    Add(builder, "itemGrowth", Solver.GrowthWorth.Bucket(item.GrowthProgress, item.GrowthGoal));
                Add(builder, "itemAttackable", item.IsAttackable.HasValue ? (item.IsAttackable.Value ? "1" : "0") : "unknown");
                Add(builder, "categoriesKnown", item.ObservedCategories is not null);
                foreach (var category in item.ObservedCategories?.OrderBy(value => value, StringComparer.Ordinal) ?? Enumerable.Empty<string>())
                    Add(builder, "observedCategory", category);
            }

            foreach (var tablet in inventory.Tablets
                         .OrderBy(value => value.InstanceId).ThenBy(value => value.DefinitionId)
                         .ThenBy(value => value.Position.Y).ThenBy(value => value.Position.X)
                         .ThenBy(value => value.Rotation).ThenBy(value => value.IsApplied)
                         .ThenBy(value => value.IsRotatable)
                         .ThenBy(value => value.Query, StringComparer.Ordinal)
                         .ThenBy(value => value.ConditionQuery, StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
                AddTablet(builder, "tablet", tablet);
            foreach (var engraving in inventory.Engravings
                         .OrderBy(value => value.InstanceId).ThenBy(value => value.DefinitionId)
                         .ThenBy(value => value.Position.Y).ThenBy(value => value.Position.X)
                         .ThenBy(value => value.Rotation).ThenBy(value => value.IsApplied)
                         .ThenBy(value => value.IsRotatable)
                         .ThenBy(value => value.Query, StringComparer.Ordinal)
                         .ThenBy(value => value.ConditionQuery, StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
                AddTablet(builder, "engraving", engraving);

            foreach (var effect in inventory.FixedEffects
                         .OrderBy(value => value.Position.Y).ThenBy(value => value.Position.X)
                         .ThenBy(value => value.Level).ThenBy(value => value.Disable)
                         .ThenBy(value => value.IgnoreCriteria).ThenBy(value => value.Multiply))
            {
                Add(builder, "fixedX", effect.Position.X);
                Add(builder, "fixedY", effect.Position.Y);
                Add(builder, "fixedLevel", effect.Level);
                Add(builder, "fixedDisable", effect.Disable);
                Add(builder, "fixedIgnore", effect.IgnoreCriteria);
                Add(builder, "fixedMultiply", effect.Multiply);
            }

            foreach (var pair in inventory.LevelMatrix.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                Add(builder, "levelCell", pair.Key);
                Add(builder, "levelValue", pair.Value);
            }
            foreach (var cell in inventory.DisabledCells.OrderBy(value => value, StringComparer.Ordinal))
                Add(builder, "disabled", cell);
            foreach (var pair in inventory.ComboCounts.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                Add(builder, "combo", pair.Key);
                Add(builder, "comboCount", pair.Value);
            }
            return Hash(builder);
        }

        public static string PlanningContext(PlanPreferences preferences, string catalogGeneration) =>
            PlanningContext(preferences, catalogGeneration, FingerprintFormat.Canonical);

        internal static bool MatchesLegacyReplay(GameSnapshot snapshot, PlanPreferences preferences, string generation, string fingerprint)
        {
            foreach (var format in new[] { FingerprintFormat.LegacyMono, FingerprintFormat.LegacyDotNet })
            {
                var placement = Placement(snapshot, PlanningContext(preferences, generation, format));
                if (Full(snapshot, preferences, generation, placement) == fingerprint) return true;
            }
            return false;
        }

        internal static string PlanningContext(PlanPreferences preferences, string catalogGeneration, FingerprintFormat format)
        {
            var builder = new StringBuilder();
            Add(builder, "catalog", catalogGeneration);
            foreach (var category in preferences.PriorityCategories.OrderBy(value => value, StringComparer.Ordinal))
                Add(builder, "priority", category);
            foreach (var pin in preferences.PinnedCharms.OrderBy(pair => pair.Key))
            {
                Add(builder, "pinned", pin.Key);
                Add(builder, "pinLevel", pin.Value);
            }
            // 제한을 건 것이 하나도 없으면 이 줄 자체가 없어야 한다. 무조건 넣으면 이 기능이
            // 없던 시절의 F10 재현 자료가 전부 지문 불일치로 거부된다(성장 항목이 같은 이유로 조건부다).
            foreach (var cap in preferences.LevelCaps.OrderBy(pair => pair.Key))
            {
                Add(builder, "levelCap", cap.Key);
                Add(builder, "levelCapLevel", cap.Value);
            }
            foreach (var retained in preferences.RetainedCharms.OrderBy(value => value))
                Add(builder, "retained", retained);
            foreach (var allowed in preferences.DeactivationAllowed.OrderBy(value => value))
                Add(builder, "deactivationAllowed", allowed);
            foreach (var held in preferences.HeldCharms.OrderBy(value => value))
                Add(builder, "held", held);
            foreach (var pair in preferences.CharmValues.EntityValues.OrderBy(value => value.Key))
                AddCharmValue(builder, "valueEntity", pair.Key.ToString(CultureInfo.InvariantCulture), pair.Value, format);
            foreach (var pair in preferences.CharmValues.IdValues.OrderBy(value => value.Key, StringComparer.Ordinal))
                AddCharmValue(builder, "valueId", pair.Key, pair.Value, format);
            return Hash(builder);
        }

        private static void AddCharmValue(
            StringBuilder builder, string prefix, string key, CharmValueEntry entry, FingerprintFormat format)
        {
            Add(builder, prefix + "Key", key);
            Add(builder, prefix + "Entity", entry.EntityId);
            Add(builder, prefix + "Id", entry.Id);
            Add(builder, prefix + "Tier", entry.Tier);
            Add(builder, prefix + "Base", entry.Base.HasValue ? FingerprintNumber.Of(entry.Base.Value, format, true) : "unset");
            Add(builder, prefix + "PerLevel", entry.PerLevel.HasValue ? FingerprintNumber.Of(entry.PerLevel.Value, format, true) : "unset");
        }

        private static void AddTablet(StringBuilder builder, string prefix, PlacedTablet tablet)
        {
            Add(builder, prefix + "Definition", tablet.DefinitionId);
            Add(builder, prefix + "Instance", tablet.InstanceId);
            Add(builder, prefix + "X", tablet.Position.X);
            Add(builder, prefix + "Y", tablet.Position.Y);
            Add(builder, prefix + "Rotation", tablet.Rotation);
            Add(builder, prefix + "Applied", tablet.IsApplied);
            Add(builder, prefix + "Rotatable", tablet.IsRotatable?.ToString() ?? "unknown");
            Add(builder, prefix + "Query", tablet.Query ?? "");
            Add(builder, prefix + "Condition", tablet.ConditionQuery ?? "");
            Add(builder, prefix + "Name", tablet.Name ?? "");
        }

        private static void Add(StringBuilder builder, string key, bool value) =>
            Add(builder, key, value ? "1" : "0");

        private static void Add(StringBuilder builder, string key, int value) =>
            Add(builder, key, value.ToString(CultureInfo.InvariantCulture));

        private static void Add(StringBuilder builder, string key, string value)
        {
            builder.Append(key.Length).Append(':').Append(key)
                .Append(value.Length).Append(':').Append(value).Append(';');
        }

        private static string Hash(StringBuilder builder)
        {
            using var algorithm = SHA256.Create();
            var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}
