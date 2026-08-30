using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    /// <summary>옮겨야 할 물건 하나. 순서를 정하는 동안 <see cref="From"/>이 바뀔 수 있다.</summary>
    internal sealed class Relocation
    {
        public string Name = "";
        public GridPos From;
        public GridPos To;
        public int FromRotation;
        public int ToRotation;
    }

    /// <summary>
    /// 이동을 실제로 따라 할 수 있는 순서로 세운다.
    ///
    /// 목록만 주면 그대로 실행할 수 없다. 두 물건이 자리를 맞바꾸는 경우 목표 칸이 이미 차 있어서,
    /// 한쪽을 빈 칸으로 잠시 빼두지 않으면 첫 걸음부터 막힌다. 그래서 목표 칸이 비어 있는 것을
    /// 먼저 옮기고, 서로 물고 물린 고리가 남으면 하나를 빈 칸으로 대피시켜 고리를 끊는다.
    /// </summary>
    internal static class MoveOrder
    {
        public static List<Move> Sequence(GridSpec grid, List<Relocation> pending, IEnumerable<GridPos> stationary)
        {
            var moves = new List<Move>();
            if (pending.Count == 0) return moves;

            // 지금 차 있는 칸. 제자리에 두는 것도 자리를 막으므로 함께 센다.
            var occupied = new HashSet<GridPos>(stationary);
            foreach (var item in pending) occupied.Add(item.From);

            var remaining = new List<Relocation>(pending);

            while (remaining.Count > 0)
            {
                var movedSomething = false;

                for (var i = remaining.Count - 1; i >= 0; i--)
                {
                    var item = remaining[i];
                    if (occupied.Contains(item.To)) continue;

                    moves.Add(Describe(item, item.To, parked: false));
                    occupied.Remove(item.From);
                    occupied.Add(item.To);
                    remaining.RemoveAt(i);
                    movedSomething = true;
                }

                if (movedSomething) continue;

                // 남은 것들이 서로의 자리를 물고 있다. 하나를 빈 칸으로 빼서 고리를 끊는다.
                var shelter = Shelter(grid, occupied, remaining);
                if (shelter is null) break;

                var victim = remaining[0];
                moves.Add(Describe(victim, shelter.Value, parked: true));
                occupied.Remove(victim.From);
                occupied.Add(shelter.Value);
                victim.From = shelter.Value;
            }

            // 대피할 칸조차 없으면 남은 것은 그대로 알린다. 조용히 빠뜨리는 것보다 낫다.
            foreach (var item in remaining) moves.Add(Describe(item, item.To, parked: false));

            return moves;
        }

        /// <summary>
        /// 잠시 비켜둘 칸. 다른 물건이 가야 할 자리를 쓰면 그 물건이 다시 막히므로 피한다.
        /// </summary>
        private static GridPos? Shelter(GridSpec grid, HashSet<GridPos> occupied, List<Relocation> remaining)
        {
            var wanted = new HashSet<GridPos>(remaining.Select(item => item.To));

            GridPos? fallback = null;
            for (var index = 0; index < grid.Storage; index++)
            {
                var cell = grid.ToPosition(index);
                if (occupied.Contains(cell)) continue;

                if (!wanted.Contains(cell)) return cell;
                fallback ??= cell;
            }
            return fallback;
        }

        private static Move Describe(Relocation item, GridPos destination, bool parked)
        {
            var turned = item.FromRotation != item.ToRotation && destination == item.To;

            string detail;
            if (parked) detail = $"{item.From} → {destination} 잠시 비켜두기";
            else if (item.From == destination && turned) detail = $"{item.From} 회전 {item.FromRotation} → {item.ToRotation}";
            else if (turned) detail = $"{item.From} → {destination} 회전 {item.ToRotation}";
            else detail = $"{item.From} → {destination}";

            return new Move(item.Name, item.From, destination, detail);
        }
    }
}
