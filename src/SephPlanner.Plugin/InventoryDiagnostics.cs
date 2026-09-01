using System.IO;
using System.Text;
using SephPlanner.Core.Runtime;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 인벤토리에 실제로 무엇이 어떤 모습으로 들어 있는지 그대로 적는다.
    /// 특정 경로로 얻은 아이템이 왜 인식되지 않는지 같은 문제는 추측 대신 이 덤프로 판단한다.
    /// </summary>
    internal static class InventoryDiagnostics
    {
        private const string FileName = "inventory-dump.txt";

        /// <summary>
        /// 왜 어떤 선택지가 추천에 안 들어왔는지 보려면 후보가 될 뻔한 것들의 상태를 알아야 한다.
        /// 거리, 생성 여부, 이미 가져갔는지가 전부 여기서 갈린다.
        /// </summary>
        private static void WriteOffers(StringBuilder text, GridInventory own, PlayerAvatar player, float radius)
        {
            var origin = player.transform.position;

            text.AppendLine();
            text.AppendLine($"[offers] radius={radius}");

            foreach (var sephirite in Object.FindObjectsByType<Sephirite>(FindObjectsSortMode.None))
            {
                if (sephirite == null) continue;

                var distance = Vector3.Distance(origin, sephirite.transform.position);
                text.AppendLine(
                    $"  Sephirite type={sephirite.type} dist={distance:0.0} " +
                    $"generated={sephirite.isGenerated} acquired={sephirite.isAcquired} " +
                    $"rewards={sephirite.Rewards.Count} " +
                    $"inRange={distance <= radius}");

                foreach (var reward in sephirite.Rewards)
                    text.AppendLine($"      reward entity={reward.entityID} instance={reward.instanceID}");
            }

            foreach (var inventory in Object.FindObjectsByType<GridInventory>(FindObjectsSortMode.None))
            {
                if (inventory == null || inventory == own) continue;

                var distance = Vector3.Distance(origin, inventory.transform.position);
                var owner = inventory.UnitAvatar == null ? "none" : inventory.UnitAvatar.GetType().Name;

                var count = 0;
                foreach (var pair in inventory.inventoryMatrix)
                    if (pair.Value != null) count++;

                text.AppendLine(
                    $"  GridInventory owner={owner} dist={distance:0.0} cells={count} " +
                    $"inRange={distance <= radius}");
            }
        }

        public static string Write(GridInventory inv, PlayerAvatar player, float offerRadius)
        {
            var text = new StringBuilder();
            text.AppendLine($"Width={inv.Width} Height={inv.Height} Storage={inv.CurrentInventoryStorage} " +
                            $"SubBag={inv.numberOfSubBagStorage} Potion={inv.numberOfPotionStorage}");

            text.AppendLine();
            text.AppendLine("[inventoryMatrix]");
            foreach (var pair in inv.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null)
                {
                    text.AppendLine($"  ({pair.Key.x},{pair.Key.y}) <null>");
                    continue;
                }

                var entity = instance.Entity;
                text.AppendLine(
                    $"  ({pair.Key.x},{pair.Key.y}) idx=({instance.XIdx},{instance.YIdx}) " +
                    $"entity={instance.EntityID} instance={instance.InstanceID} qty={instance.Quantity} " +
                    $"type={(entity == null ? "?" : entity.type.ToString())} " +
                    $"active={(entity == null ? "?" : entity.activeType.ToString())} " +
                    $"key={(entity?.aName == null ? "?" : entity.aName.key)} " +
                    $"charm={(instance.Charm != null)} tablet={(instance.StoneTablet != null)}");
            }

            text.AppendLine();
            text.AppendLine("[stoneTablets]");
            foreach (var pair in inv.stoneTablets)
            {
                var tablet = pair.Value;
                if (tablet == null) continue;
                text.AppendLine(
                    $"  ({pair.Key.x},{pair.Key.y}) entity={tablet.entityID} instance={tablet.instanceID} " +
                    $"rot={tablet.rotation} applied={tablet.IsApplied} custom={tablet.isCustomTablet}");
            }

            text.AppendLine();
            text.AppendLine("[engravings]");
            foreach (var engraving in inv.engravings)
            {
                if (engraving == null) continue;
                text.AppendLine(
                    $"  ({engraving.xIdx},{engraving.yIdx}) entity={engraving.entityID} " +
                    $"instance={engraving.instanceID} rot={engraving.rotation} " +
                    $"applied={engraving.IsApplied} custom={engraving.isCustomTablet}");
            }

            text.AppendLine();
            text.AppendLine("[levelMatrix]");
            foreach (var pair in inv.levelMatrix)
                text.AppendLine($"  ({pair.Key.x},{pair.Key.y}) = {pair.Value}");

            WriteOffers(text, inv, player, offerRadius);
            UiDiagnostics.Write(text);

            Directory.CreateDirectory(PlannerData.DataDirectory);
            var path = Path.Combine(PlannerData.DataDirectory, FileName);
            File.WriteAllText(path, text.ToString());
            return path;
        }
    }
}
