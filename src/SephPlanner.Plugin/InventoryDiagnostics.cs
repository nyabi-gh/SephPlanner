using System.IO;
using System.Text;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 인벤토리에 실제로 무엇이 어떤 모습으로 들어 있는지 그대로 적는다.
    /// 특정 경로로 얻은 아이템이 왜 인식되지 않는지 같은 문제는 추측 대신 이 덤프로 판단한다.
    /// </summary>
    internal static class InventoryDiagnostics
    {
        private const string FileName = "inventory-dump.txt";

        public static string Write(GridInventory inv)
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

            Directory.CreateDirectory(IpcContract.DataDirectory);
            var path = Path.Combine(IpcContract.DataDirectory, FileName);
            File.WriteAllText(path, text.ToString());
            return path;
        }
    }
}
