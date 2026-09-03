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
        ///
        /// <b>내용물은 지금 화면에 보이는 것만 적는다.</b> 이 파일은 플레이 중에도 열어 볼 수
        /// 있으므로, 열지 않은 상자의 개수나 보상 창을 열기 전 세피라이트의 보상 번호를 적으면
        /// 손으로는 알 수 없는 것을 알려주는 셈이 된다(레벨업 보상은 창을 열기 전에 채워진다).
        /// 보임 판정은 <see cref="OfferReader"/> 의 것을 그대로 부른다.
        /// </summary>
        private static void WriteOffers(StringBuilder text, GridInventory own, PlayerAvatar player, float radius)
        {
            var origin = player.transform.position;
            var showing = OfferReader.ShownSephirite();

            text.AppendLine();
            text.AppendLine($"[offers] radius={radius}");

            foreach (var sephirite in Object.FindObjectsByType<Sephirite>(FindObjectsSortMode.None))
            {
                if (sephirite == null) continue;

                var distance = Vector3.Distance(origin, sephirite.transform.position);
                var shown = sephirite == showing;
                text.AppendLine(
                    $"  Sephirite type={sephirite.type} dist={distance:0.0} " +
                    $"generated={sephirite.isGenerated} acquired={sephirite.isAcquired} " +
                    $"owned={sephirite.isOwned} shown={shown} " +
                    (shown ? $"rewards={sephirite.Rewards.Count} " : "") +
                    $"inRange={distance <= radius}");

                if (!shown) continue;

                foreach (var reward in sephirite.Rewards)
                    text.AppendLine($"      reward entity={reward.entityID} instance={reward.instanceID}");
            }

            foreach (var inventory in Object.FindObjectsByType<GridInventory>(FindObjectsSortMode.None))
            {
                if (inventory == null || inventory == own) continue;

                var distance = Vector3.Distance(origin, inventory.transform.position);
                var owner = inventory.UnitAvatar == null ? "none" : inventory.UnitAvatar.GetType().Name;
                var shown = OfferReader.IsShown(inventory);

                text.AppendLine(
                    $"  GridInventory owner={owner} dist={distance:0.0} shown={shown} " +
                    (shown ? $"cells={Occupied(inventory)} " : "") +
                    $"inRange={distance <= radius}");
            }
        }

        private static int Occupied(GridInventory inventory)
        {
            var count = 0;
            foreach (var pair in inventory.inventoryMatrix)
                if (pair.Value != null) count++;
            return count;
        }

        public static string Write(GridInventory inv, PlayerAvatar player, float offerRadius)
        {
            var text = new StringBuilder();
            text.AppendLine(PluginIdentity.Describe());
            text.AppendLine();
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
            FrameCost.Write(text);

            Directory.CreateDirectory(PlannerData.DataDirectory);
            var path = Path.Combine(PlannerData.DataDirectory, FileName);
            File.WriteAllText(path, text.ToString());
            return path;
        }
    }
}
