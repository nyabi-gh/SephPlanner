namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 질의 해석에 필요한 격자 정보. Storage 는 실제로 열려 있는 칸 수라 런 도중 늘어난다.
    /// </summary>
    public readonly struct GridSpec
    {
        public const int DefaultWidth = 6;
        public const int DefaultHeight = 7;

        public readonly int Width;
        public readonly int Height;
        public readonly int Storage;

        public GridSpec(int width, int height, int storage)
        {
            Width = width;
            Height = height;
            Storage = storage;
        }

        public static GridSpec WithStorage(int storage) => new GridSpec(DefaultWidth, DefaultHeight, storage);

        /// <summary>
        /// 본 격자 안이면서 열려 있는 칸인가.
        ///
        /// 격자 밖 좌표는 포션 벨트(게임이 y=100 줄에 둔다) 같은 다른 보관함이고,
        /// <see cref="Storage"/> 밖은 아직 잠긴 칸이다. 이 판정이 여러 벌로 흩어져 있으면
        /// 그중 하나만 잠긴 칸을 빠뜨려도 솔버와 자동 배치가 서로 다른 격자를 보게 된다.
        /// </summary>
        public bool Contains(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height && ToIndex(x, y) < Storage;

        public bool Contains(Model.GridPos position) => Contains(position.X, position.Y);

        public int ToIndex(int x, int y) => y * Width + x;
        public Model.GridPos ToPosition(int index) => new Model.GridPos(index % Width, index / Width);
    }
}
