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
        public static List<Move> Sequence(
            GridSpec grid, List<Relocation> pending, IEnumerable<GridPos> stationary, out bool complete)
        {
            complete = true;
            var moves = new List<Move>();
            if (pending.Count == 0) return moves;

            // 지금 차 있는 칸. 제자리에 두는 것도 자리를 막으므로 함께 센다.
            var occupied = new HashSet<GridPos>(stationary);
            var remaining = new List<Relocation>();
            foreach (var item in pending)
            {
                occupied.Add(item.From);

                // 위치는 그대로고 회전만 바뀌는 것은 자리 경쟁과 무관하니 바로 내보낸다.
                // 아래 루프에 넣으면 자기 From 에 막힌 것으로 보여 불필요한 대피 두 걸음이 된다.
                if (item.From == item.To) moves.Add(Describe(item, item.To, parked: false));
                else remaining.Add(item);
            }

            // 대피는 고리 하나에 한 번이면 충분하므로 남은 개수를 넘을 수 없다. 넘었다는 것은
            // 입력이 순열이 아니라는 뜻이고(어떤 목표 칸이 정지 칸이거나 둘이 같은 칸을 원한다),
            // 그대로 두면 같은 물건을 빈 칸으로 옮겼다 되돌리기를 무한히 반복한다.
            var parks = 0;

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
                var shelter = Shelter(grid, occupied);
                if (shelter is null || ++parks > pending.Count)
                {
                    complete = false;
                    moves.Clear();
                    return moves;
                }

                var victim = remaining[0];
                moves.Add(Describe(victim, shelter.Value, parked: true));
                occupied.Remove(victim.From);
                occupied.Add(shelter.Value);
                victim.From = shelter.Value;
            }

            return moves;
        }

        /// <summary>
        /// 잠시 비켜둘 빈 칸. 이 지점에 왔다는 것은 남은 모든 목표 칸이 차 있다는 뜻이라,
        /// 빈 칸은 어떤 것의 목표도 아니어서 아무 칸이나 골라도 다른 물건을 막지 않는다.
        /// </summary>
        private static GridPos? Shelter(GridSpec grid, HashSet<GridPos> occupied)
        {
            for (var index = 0; index < grid.Storage; index++)
            {
                var cell = grid.ToPosition(index);
                if (!occupied.Contains(cell)) return cell;
            }
            return null;
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
