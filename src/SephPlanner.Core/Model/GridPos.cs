using System;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 인벤토리 격자 좌표. 게임 내부 <c>ItemPosition</c>에 대응한다.
    /// </summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public GridPos Offset(int dx, int dy) => new GridPos(X + dx, Y + dy);

        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is GridPos p && Equals(p);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);
        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);
    }
}
