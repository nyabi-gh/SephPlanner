using System;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 인벤토리 격자 좌표. 게임 내부 <c>ItemPosition</c>에 대응한다.
    ///
    /// 설정 가능한 프로퍼티인 이유는 직렬화다. System.Text.Json 은 구조체에서 매개변수 생성자를
    /// 쓰지 않고 기본 생성자로 만든 뒤 프로퍼티를 채운다. 읽기 전용으로 두면 좌표가 조용히
    /// (0,0)이 되어버린다. 값을 제자리에서 바꾸지 않고 항상 새로 만들어 쓴다.
    /// </summary>
    public struct GridPos : IEquatable<GridPos>
    {
        public int X { get; set; }
        public int Y { get; set; }

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
