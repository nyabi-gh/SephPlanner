using System;
using System.Collections.Generic;
using System.Globalization;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 석판의 질의 문자열을 칸 목록으로 해석한다. 게임 <c>StoneTablet.ParseQuery</c>의 포팅이며,
    /// 결과가 게임과 한 칸이라도 어긋나면 솔버 전체가 틀어지므로 플러그인의 검증기로 대조한다.
    /// </summary>
    public static class TabletQuery
    {
        /// <summary>회전 0 기준 오프셋. 나머지 회전은 (x,y) -> (y,-x)를 반복 적용해 얻는다.</summary>
        private static readonly Dictionary<string, (int X, int Y)> Offsets =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal)
            {
                ["LEFT"] = (-1, 0),
                ["LEFTLEFT"] = (-2, 0),
                ["LEFTLEFTLEFT"] = (-3, 0),
                ["LEFTLEFTLEFTLEFT"] = (-4, 0),
                ["RIGHT"] = (1, 0),
                ["RIGHTRIGHT"] = (2, 0),
                ["RIGHTRIGHTRIGHT"] = (3, 0),
                ["RIGHTRIGHTRIGHTRIGHT"] = (4, 0),
                ["UP"] = (0, -1),
                ["UPUP"] = (0, -2),
                ["UPUPUP"] = (0, -3),
                ["UPUPUPUP"] = (0, -4),
                ["DOWN"] = (0, 1),
                ["DOWNDOWN"] = (0, 2),
                ["DOWNDOWNDOWN"] = (0, 3),
                ["DOWNDOWNDOWNDOWN"] = (0, 4),
                ["DIAUPLEFT"] = (-1, -1),
                ["DIAUPRIGHT"] = (1, -1),
                ["DIADOWNLEFT"] = (-1, 1),
                ["DIADOWNRIGHT"] = (1, 1),
                ["KNIGHTUPLEFT"] = (-1, -2),
                ["KNIGHTUPRIGHT"] = (2, -1),
                ["KNIGHTDOWNLEFT"] = (-2, 1),
                ["KNIGHTDOWNRIGHT"] = (1, 2),
                ["KNIGHTUPLEFT_INVERT"] = (-2, -1),
                ["KNIGHTUPRIGHT_INVERT"] = (1, -2),
                ["KNIGHTDOWNLEFT_INVERT"] = (-1, 2),
                ["KNIGHTDOWNRIGHT_INVERT"] = (2, 1),
            };

        /// <summary>격자 전체를 훑는 토큰은 오프셋 회전이 아니라 토큰 이름 자체가 회전한다.</summary>
        private static readonly Dictionary<string, string[]> RotatedNames =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["TOP"] = new[] { "TOP", "LEFTEND", "BOTTOM", "RIGHTEND" },
                ["BOTTOM"] = new[] { "BOTTOM", "RIGHTEND", "TOP", "LEFTEND" },
                ["LEFTEND"] = new[] { "LEFTEND", "BOTTOM", "RIGHTEND", "TOP" },
                ["RIGHTEND"] = new[] { "RIGHTEND", "TOP", "LEFTEND", "BOTTOM" },
                ["X_PLUS"] = new[] { "X_PLUS", "Y_MINUS", "X_MINUS", "Y_PLUS" },
                ["X_MINUS"] = new[] { "X_MINUS", "Y_PLUS", "X_PLUS", "Y_MINUS" },
                ["Y_PLUS"] = new[] { "Y_PLUS", "X_PLUS", "Y_MINUS", "X_MINUS" },
                ["Y_MINUS"] = new[] { "Y_MINUS", "X_MINUS", "Y_PLUS", "X_PLUS" },
                ["RIGHT_RISING"] = new[] { "RIGHT_RISING", "LEFT_RISING", "LEFT_FALLING", "RIGHT_FALLING" },
                ["RIGHT_FALLING"] = new[] { "RIGHT_FALLING", "RIGHT_RISING", "LEFT_RISING", "LEFT_FALLING" },
                ["LEFT_RISING"] = new[] { "LEFT_RISING", "LEFT_FALLING", "RIGHT_FALLING", "RIGHT_RISING" },
                ["LEFT_FALLING"] = new[] { "LEFT_FALLING", "RIGHT_FALLING", "RIGHT_RISING", "LEFT_RISING" },
                ["HORIZONTAL"] = new[] { "HORIZONTAL", "VERTICAL", "HORIZONTAL", "VERTICAL" },
                ["VERTICAL"] = new[] { "VERTICAL", "HORIZONTAL", "VERTICAL", "HORIZONTAL" },
            };

        private static readonly char[] LineSeparators = { '\r', '\n' };

        /// <summary>회전한 오프셋으로 토큰 이름을 되찾는다. 28종이 좌표와 일대일이라 늘 찾아진다.</summary>
        private static readonly Dictionary<(int X, int Y), string> OffsetNames = BuildOffsetNames();

        private static Dictionary<(int X, int Y), string> BuildOffsetNames()
        {
            var map = new Dictionary<(int, int), string>();
            foreach (var pair in Offsets) map[pair.Value] = pair.Key;
            return map;
        }

        /// <summary>
        /// 질의를 돌려 새 질의 문자열로 만든다. 게임의 <c>StoneTablet.GetRotatedQuery</c>와 같은
        /// 일이며, 석판 합성이 재료의 회전을 결과 질의에 구워 넣기 때문에 필요하다.
        ///
        /// <see cref="Parse"/>가 읽을 때 돌리는 것과 결과가 같아야 한다. 즉 a 만큼 돌린 질의를
        /// b 로 읽은 것이 원본을 a+b 로 읽은 것과 같다(TabletQueryTests 가 고정해 둔다).
        /// </summary>
        public static string Rotated(string query, int rotation)
        {
            if (string.IsNullOrEmpty(query)) return query;

            rotation = ((rotation % 4) + 4) % 4;
            if (rotation == 0) return query;

            var lines = new List<string>();
            foreach (var line in query.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 1) parts[0] = RotatedToken(parts[0], rotation);
                lines.Add(string.Join(" ", parts));
            }
            return string.Join("\n", lines);
        }

        /// <summary>토큰 하나의 회전. 아는 토큰이 아니면 그대로 둔다 - 읽는 쪽도 그렇게 한다.</summary>
        private static string RotatedToken(string token, int rotation)
        {
            if (Offsets.TryGetValue(token, out var offset))
            {
                var (dx, dy) = Rotate(offset, rotation);
                return OffsetNames.TryGetValue((dx, dy), out var name) ? name : token;
            }

            return RotatedNames.TryGetValue(token, out var names) ? names[rotation] : token;
        }

        /// <summary>
        /// 파싱 결과 캐시. 빔 확장이 같은 질의를 같은 자리·회전으로 수백만 번 다시 읽는 구조라
        /// (BeamWidth x 열린 칸 x 회전) 여기가 풀이 시간의 대부분이었다. 키 공간이 유한하므로
        /// (질의 종류 x 칸 x 회전 4) 상한만 두고 넘치면 비운다.
        /// 반환 리스트는 공유되므로 받은 쪽이 고치면 안 된다(QueryCell 은 구조체라 원소는 복사된다).
        /// </summary>
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<(string, int, int, int, int, int, int), List<QueryCell>> Cache =
            new Dictionary<(string, int, int, int, int, int, int), List<QueryCell>>();
        private const int CacheLimit = 100000;
        private static readonly List<QueryCell> NoCells = new List<QueryCell>();

        public static List<QueryCell> Parse(string query, GridSpec grid, GridPos origin, int rotation)
        {
            if (string.IsNullOrEmpty(query)) return NoCells;

            rotation = ((rotation % 4) + 4) % 4;

            var key = (query, grid.Width, grid.Height, grid.Storage, origin.X, origin.Y, rotation);
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            var cells = new List<QueryCell>();
            foreach (var line in query.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                // 게임 ParseQuery 와 같은 분할이다. 빈 토큰을 거르지 않는 것까지 동일해서,
                // 연속 공백의 처리(빈 값 토큰)도 게임과 같은 결과가 된다.
                var parts = line.Split(' ');
                if (parts.Length < 2) continue;
                Emit(cells, parts, grid, origin, rotation);
            }

            lock (CacheLock)
            {
                if (Cache.Count >= CacheLimit) Cache.Clear();
                Cache[key] = cells;
            }
            return cells;
        }

        private static void Emit(List<QueryCell> cells, string[] parts, GridSpec grid, GridPos origin, int rotation)
        {
            var token = parts[0];

            if (Offsets.TryGetValue(token, out var offset))
            {
                var (dx, dy) = Rotate(offset, rotation);
                cells.Add(new QueryCell(origin.Offset(dx, dy), parts[1]));
                return;
            }

            if (RotatedNames.TryGetValue(token, out var names)) token = names[rotation];

            switch (token)
            {
                case "O":
                    cells.Add(new QueryCell(origin, parts[1]));
                    break;

                case "IDX":
                    if (parts.Length >= 3 && TryParseIndex(parts[1], out var idx))
                        cells.Add(new QueryCell(grid.ToPosition(idx), parts[2]));
                    break;

                case "RIDX":
                    if (parts.Length >= 3 && TryParseIndex(parts[1], out var ridx))
                        cells.Add(new QueryCell(grid.ToPosition(grid.Storage - 1 - ridx), parts[2]));
                    break;

                case "HORIZONTAL": EmitHorizontal(cells, parts[1], grid, origin); break;
                case "VERTICAL": EmitVertical(cells, parts[1], grid, origin); break;

                case "X_PLUS": EmitXPlus(cells, parts[1], grid, origin); break;
                case "X_MINUS": EmitXMinus(cells, parts[1], origin); break;
                case "Y_PLUS": EmitYPlus(cells, parts[1], grid, origin); break;
                case "Y_MINUS": EmitYMinus(cells, parts[1], origin); break;

                case "TOP": EmitTop(cells, parts[1], grid); break;
                case "BOTTOM": EmitBottom(cells, parts[1], grid); break;
                case "LEFTEND": EmitLeftEnd(cells, parts[1], grid); break;
                case "RIGHTEND": EmitRightEnd(cells, parts[1], grid); break;

                case "RIGHT_RISING": EmitRay(cells, parts[1], grid, origin, 1, -1); break;
                case "RIGHT_FALLING": EmitRay(cells, parts[1], grid, origin, 1, 1); break;
                case "LEFT_RISING": EmitRay(cells, parts[1], grid, origin, -1, -1); break;
                case "LEFT_FALLING": EmitRay(cells, parts[1], grid, origin, -1, 1); break;

                case "CHECKERBOARD": EmitCheckerboard(cells, parts[1], grid, origin, 0); break;
                case "CHECKERBOARD2": EmitCheckerboard(cells, parts[1], grid, origin, 1); break;
            }
        }

        private static (int X, int Y) Rotate((int X, int Y) offset, int rotation)
        {
            var (x, y) = offset;
            for (var i = 0; i < rotation; i++) (x, y) = (y, -x);
            return (x, y);
        }

        private static bool TryParseIndex(string text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static void EmitHorizontal(List<QueryCell> cells, string value, GridSpec grid, GridPos origin)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (x == origin.X) continue;
                var cell = new QueryCell(new GridPos(x, origin.Y), value);
                cell.BorderLeft = x == 0;
                cell.BorderRight = x == grid.Width - 1;
                cells.Add(cell);
            }
        }

        private static void EmitVertical(List<QueryCell> cells, string value, GridSpec grid, GridPos origin)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                if (y == origin.Y) continue;
                var cell = new QueryCell(new GridPos(origin.X, y), value);
                cell.BorderTop = y == 0;
                cell.BorderBottom = y == grid.Height - 1;
                cells.Add(cell);
            }
        }

        private static void EmitXPlus(List<QueryCell> cells, string value, GridSpec grid, GridPos origin)
        {
            for (var x = origin.X + 1; x < grid.Width; x++)
            {
                var cell = new QueryCell(new GridPos(x, origin.Y), value);
                cell.BorderRight = x == grid.Width - 1;
                cells.Add(cell);
            }
        }

        private static void EmitXMinus(List<QueryCell> cells, string value, GridPos origin)
        {
            for (var x = 0; x < origin.X; x++)
            {
                var cell = new QueryCell(new GridPos(x, origin.Y), value);
                cell.BorderLeft = x == 0;
                cells.Add(cell);
            }
        }

        // 게임 구현이 origin.y 부터 시작해 자기 칸을 포함한다. X_PLUS 와 비대칭이지만 그대로 따른다.
        private static void EmitYPlus(List<QueryCell> cells, string value, GridSpec grid, GridPos origin)
        {
            for (var y = origin.Y; y < grid.Height; y++)
            {
                var cell = new QueryCell(new GridPos(origin.X, y), value);
                cell.BorderBottom = y == grid.Height - 1;
                cells.Add(cell);
            }
        }

        private static void EmitYMinus(List<QueryCell> cells, string value, GridPos origin)
        {
            for (var y = 0; y < origin.Y; y++)
            {
                var cell = new QueryCell(new GridPos(origin.X, y), value);
                cell.BorderTop = y == 0;
                cells.Add(cell);
            }
        }

        private static void EmitTop(List<QueryCell> cells, string value, GridSpec grid)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = new QueryCell(new GridPos(x, 0), value);
                cell.IsYWorldPosition = true;
                cell.BorderTop = true;
                cell.BorderLeft = x == 0;
                cell.BorderRight = x == grid.Width - 1;
                cells.Add(cell);
            }
        }

        private static void EmitBottom(List<QueryCell> cells, string value, GridSpec grid)
        {
            for (var i = 0; i < grid.Width; i++)
            {
                var pos = grid.ToPosition(grid.Storage - i - 1);
                var cell = new QueryCell(pos, value);
                cell.IsYWorldPosition = true;
                cell.BorderBottom = true;
                cell.BorderLeft = pos.X == 0;
                cell.BorderRight = pos.X == grid.Width - 1;
                cells.Add(cell);
            }
        }

        private static void EmitLeftEnd(List<QueryCell> cells, string value, GridSpec grid)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                var cell = new QueryCell(new GridPos(0, y), value);
                cell.IsXWorldPosition = true;
                cell.BorderLeft = true;
                cell.BorderTop = y == 0;
                cell.BorderBottom = y == grid.Height - 1;
                cells.Add(cell);
            }
        }

        private static void EmitRightEnd(List<QueryCell> cells, string value, GridSpec grid)
        {
            var lastColumn = grid.Width - 1;
            for (var y = 0; y < grid.Height; y++)
            {
                if (grid.ToIndex(lastColumn, y) > grid.Storage - 1) continue;

                var cell = new QueryCell(new GridPos(lastColumn, y), value);
                cell.IsXWorldPosition = true;
                cell.BorderRight = true;
                cell.BorderTop = y == 0;
                cell.BorderBottom = y == grid.Height - 1 || grid.ToIndex(lastColumn, y + 1) > grid.Storage - 1;
                cells.Add(cell);
            }
        }

        private static void EmitRay(List<QueryCell> cells, string value, GridSpec grid, GridPos origin, int stepX, int stepY)
        {
            var x = origin.X + stepX;
            var y = origin.Y + stepY;
            while (x >= 0 && x < grid.Width && y >= 0 && y < grid.Height)
            {
                // 진행 방향 쪽 테두리만 표시한다. 반대편은 게임 구현에서도 건드리지 않는다.
                var cell = new QueryCell(new GridPos(x, y), value);
                if (stepX > 0) cell.BorderRight = x == grid.Width - 1;
                else cell.BorderLeft = x == 0;
                if (stepY > 0) cell.BorderBottom = y == grid.Height - 1;
                else cell.BorderTop = y == 0;
                cells.Add(cell);
                x += stepX;
                y += stepY;
            }
        }

        private static void EmitCheckerboard(List<QueryCell> cells, string value, GridSpec grid, GridPos origin, int parity)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                for (var y = 0; y < grid.Height; y++)
                {
                    if (x == origin.X && y == origin.Y) continue;
                    if (grid.ToIndex(x, y) > grid.Storage - 1) continue;
                    if ((x + y + origin.X + origin.Y) % 2 != parity) continue;
                    cells.Add(new QueryCell(new GridPos(x, y), value));
                }
            }
        }
    }
}
