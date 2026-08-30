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

        public static List<QueryCell> Parse(string query, GridSpec grid, GridPos origin, int rotation)
        {
            var cells = new List<QueryCell>();
            if (string.IsNullOrEmpty(query)) return cells;

            rotation = ((rotation % 4) + 4) % 4;

            foreach (var line in query.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ');
                if (parts.Length < 2) continue;
                Emit(cells, parts, grid, origin, rotation);
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
