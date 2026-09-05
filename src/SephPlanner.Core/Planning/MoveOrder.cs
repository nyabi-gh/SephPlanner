using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    internal sealed class Relocation
    {
        public string Name = "";
        public GridPos From;
        public GridPos To;
        public int FromRotation;
        public int ToRotation;
    }

    /// <summary>게임의 칸 교환 순서로 이동한 뒤 최종 자리에서 회전한다.</summary>
    internal static class MoveOrder
    {
        public static List<Move> Sequence(
            GridSpec grid, List<Relocation> pending, IEnumerable<GridPos> stationary, out bool complete)
        {
            complete = false;
            var moves = new List<Move>();
            var fixedCells = new HashSet<GridPos>(stationary);
            var targets = new HashSet<GridPos>(fixedCells);
            var occupied = new Dictionary<GridPos, Relocation>();
            var positions = new Dictionary<Relocation, GridPos>();
            foreach (var item in pending)
            {
                if (!grid.Contains(item.From) || !grid.Contains(item.To) || fixedCells.Contains(item.From) ||
                    occupied.ContainsKey(item.From) || !targets.Add(item.To)) return moves;
                occupied.Add(item.From, item);
                positions.Add(item, item.From);
            }

            foreach (var item in pending)
            {
                var from = positions[item];
                if (from == item.To) continue;
                occupied.TryGetValue(item.To, out var displaced);
                moves.Add(new Move(item.Name, from, item.To, displaced != null
                    ? $"{from} ↔ {item.To} 교환 ({displaced.Name})"
                    : $"{from} → {item.To}"));
                occupied.Remove(from);
                if (displaced != null)
                {
                    occupied[from] = displaced;
                    positions[displaced] = from;
                }
                occupied[item.To] = item;
                positions[item] = item.To;
            }

            foreach (var item in pending)
            {
                if (item.FromRotation != item.ToRotation)
                    moves.Add(new Move(item.Name, item.To, item.To,
                        $"{item.To} 회전 {item.FromRotation} → {item.ToRotation}"));
            }
            complete = true;
            return moves;
        }
    }
}
