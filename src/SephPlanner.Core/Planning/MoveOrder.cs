using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;
using static SephPlanner.Core.Text.Localization;

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
                    ? Tr("{0} ↔ {1} 교환 ({2})", Cell(from), Cell(item.To), displaced.Name)
                    : $"{Cell(from)} → {Cell(item.To)}"));
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
                        Tr("{0} 회전 {1} → {2}", Cell(item.To), Angle(item.FromRotation), Angle(item.ToRotation))));
            }
            complete = true;
            return moves;
        }

        /// <summary>
        /// 손으로 따라 옮기는 문장이라 칸 쪽지와 같은 1부터 센 열·행으로 적는다. <see cref="GridPos.ToString"/> 은 0부터라
        /// 로그에만 쓴다.
        /// </summary>
        private static string Cell(GridPos position) => Tr("{0}열 {1}행", position.X + 1, position.Y + 1);

        private static string Angle(int rotation) => rotation * 90 + "°";
    }
}
