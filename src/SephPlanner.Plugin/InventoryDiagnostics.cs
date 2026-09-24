using System.IO;
using System.Text;
using Mirror;
using Newtonsoft.Json;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 인벤토리에 실제로 무엇이 어떤 모습으로 들어 있는지 그대로 적는다.
    /// 특정 경로로 얻은 아이템이 왜 인식되지 않는지 같은 문제는 추측 대신 이 덤프로 판단한다.
    /// </summary>
    internal static class InventoryDiagnostics
    {
        private static int Severed(System.Collections.Generic.IEnumerable<StoneTablet> tablets)
        {
            var count = 0;
            foreach (var tablet in tablets)
                if (tablet == null) count++;
            return count;
        }

        private const string FileName = "inventory-dump.txt";
        private const string SnapshotFileName = "inventory-snapshot.json";

        /// <summary>
        /// 덤프와 같은 순간의 스냅샷을 그대로 남긴다. <c>DataTool</c> 의 <c>--check</c> 와
        /// <c>--replay</c> 가 먹는 것이 이 파일이라, 제보에 재생 가능한 입력이 딸려 온다.
        ///
        /// 스냅샷에는 <see cref="OfferReader"/> 가 지금 보인다고 판정한 것만 들어 있으므로,
        /// 이것을 함께 남긴다고 덤프가 아는 것이 늘지는 않는다.
        /// </summary>
        public static string WriteSnapshot(GameSnapshot snapshot, string directory = null)
        {
            directory ??= PlannerData.DataDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, SnapshotFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            return path;
        }

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

            // 상점의 사파이어 보충 칸. 창이 열려 있을 때만 적는다 - 인벤토리와 같은 규칙이다.
            var merchant = OfferReader.ShownMerchant();
            if (merchant != null && merchant.replenishments != null)
            {
                text.AppendLine(
                    $"  Replenishment count={merchant.replenishments.Count} tries={merchant.replenishmentTryCount}");
                foreach (var item in merchant.replenishments)
                {
                    if (item != null)
                        text.AppendLine($"      entity={item.entityID} purchased={item.purchased}");
                }
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

        /// <summary>
        /// 가방을 있는 그대로 적는다. <b>한 구역이 터져도 파일은 남긴다</b> - 이 덤프는 무너진
        /// 상태를 적으러 오는 것이라, 그때 터지는 구역 하나에 나머지까지 잃으면 정작 필요할 때
        /// 자료가 없다. 제보 5915982c 에서 Item 이 끊긴 아티팩트 하나에 걸려 세 번 다 저장되지
        /// 않았다. 터진 구역은 그 사실을 파일에 적고 <paramref name="log"/> 로도 알린다.
        /// </summary>
        /// <param name="log">
        /// 구역 하나가 터졌을 때 알릴 곳. 파일은 그대로 저장되므로 이것이 없으면 실패가 파일
        /// 안에만 남아, 보고서 요약에는 "저장됨" 으로만 보인다.
        /// </param>
        public static string Write(
            GridInventory inv, PlayerAvatar player, float offerRadius, string directory = null,
            System.Action<object> log = null)
        {
            var text = new StringBuilder();

            void Section(string name, System.Action write)
            {
                try
                {
                    write();
                }
                catch (System.Exception ex)
                {
                    text.AppendLine();
                    text.AppendLine($"[{name} 실패] {ex}");
                    log?.Invoke($"진단 덤프의 [{name}] 구역이 실패해 그 자리만 비워 두고 남깁니다: {ex}");
                }
            }

            Section("머리", () =>
            {
                text.AppendLine(PluginIdentity.Describe());
                text.AppendLine();
                text.AppendLine($"Width={inv.Width} Height={inv.Height} Storage={inv.CurrentInventoryStorage} " +
                                $"SubBag={inv.numberOfSubBagStorage} Potion={inv.numberOfPotionStorage}");
                text.AppendLine($"서버={NetworkServer.active} 클라이언트={NetworkClient.active} " +
                                $"석판각인권={inv.tabletEngravingCount}");
            });

            Section("inventoryMatrix", () =>
            {
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
            });

            Section("stoneTablets", () =>
            {
                text.AppendLine();
                text.AppendLine($"[stoneTablets] {inv.stoneTablets.Count}개, 끊긴 참조 {Severed(inv.stoneTablets.Values)}개");
                foreach (var pair in inv.stoneTablets)
                {
                    var tablet = pair.Value;
                    if (tablet == null)
                    {
                        text.AppendLine($"  ({pair.Key.x},{pair.Key.y}) <null>");
                        continue;
                    }
                    text.AppendLine(
                        $"  ({pair.Key.x},{pair.Key.y}) entity={tablet.entityID} instance={tablet.instanceID} " +
                        $"rot={tablet.rotation} applied={tablet.IsApplied} custom={tablet.isCustomTablet}");
                }
            });

            Section("engravings", () =>
            {
                text.AppendLine();
                text.AppendLine($"[engravings] {inv.engravings.Count}개, 끊긴 참조 {Severed(inv.engravings)}개");
                foreach (var engraving in inv.engravings)
                {
                    if (engraving == null)
                    {
                        text.AppendLine("  <null>");
                        continue;
                    }
                    text.AppendLine(
                        $"  ({engraving.xIdx},{engraving.yIdx}) entity={engraving.entityID} " +
                        $"instance={engraving.instanceID} rot={engraving.rotation} " +
                        $"applied={engraving.IsApplied} custom={engraving.isCustomTablet}");
                }
            });

            Section("levelMatrix", () =>
            {
                text.AppendLine();
                text.AppendLine("[levelMatrix]");
                foreach (var pair in inv.levelMatrix)
                    text.AppendLine($"  ({pair.Key.x},{pair.Key.y}) = {pair.Value}");
            });

            Section("고정 효과", () =>
            {
                var layer = FixedEffectLayer.Get(inv);
                text.AppendLine();
                text.AppendLine($"[고정 효과] {(NetworkServer.active ? "서버 원본" : "게임 행렬에서 되뺌")}");
                text.AppendLine("  " + FixedEffectResidual.Describe(layer.Cells, limit: 64));
                if (layer.ComboEngraving != null)
                {
                    var rule = layer.ComboEngraving;
                    inv.currentSetEffectCount.TryGetValue(rule.Category, out var count);
                    var positions = new StringBuilder();
                    foreach (var position in rule.Positions) positions.Append(' ').Append(position);
                    text.AppendLine($"  콤보 각인 {rule.Category} {count}개: " +
                                    FixedEffectResidual.Describe(layer.Engraved, limit: 64) + $" (좌표{positions})");
                }
                text.AppendLine(layer.Residual == null
                    ? "  게임 행렬에서 되뺀 값: 게임이 다시 계산하기 전이라 되빼지 않음"
                    : $"  게임 행렬에서 되뺀 값({layer.Residual.Status}): " +
                      FixedEffectResidual.Describe(layer.Residual.Cells, limit: 64) +
                      (layer.Residual.Reason.Length > 0 ? " - " + layer.Residual.Reason : ""));
                foreach (var tablet in layer.View.Stale)
                {
                    text.AppendLine(
                        $"  옛 격자로 적용된 채인 석판: ({tablet.xIdx},{tablet.yIdx}) entity={tablet.entityID} " +
                        $"적용 범위 {tablet.EffectRange.Count}칸");
                }
                if (layer.Blocker.Length > 0) text.AppendLine("  막힘: " + layer.Blocker);
                if (layer.Pending.Length > 0) text.AppendLine("  보류: " + layer.Pending);
            });

            Section("offers", () => WriteOffers(text, inv, player, offerRadius));
            Section("효과 상호작용 검증", () => EffectStateDiagnostics.Append(text, inv, player));
            Section("화면", () => UiDiagnostics.Write(text));
            Section("perf", () => FrameCost.Write(text));

            directory ??= PlannerData.DataDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, FileName);
            File.WriteAllText(path, text.ToString());
            return path;
        }
    }
}
