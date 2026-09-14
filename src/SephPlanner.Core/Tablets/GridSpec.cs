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

        /// <summary>
        /// 게임이 한 번에 여는 칸 수. 레벨업이 여는 것은 <c>LevelController.LevelUpOnServer</c>의
        /// <c>Inventory.AddStorage(1)</c> 한 칸이다. 다른 출처(농장 능력·설치물·상태이상)는
        /// 프리팹에 적힌 값만큼 열기 때문에 코드에서는 확정할 수 없다. 그래서 <b>가장 작고 가장
        /// 흔한 한 칸</b>만 확정된 사실로 삼는다.
        /// </summary>
        public const int OpeningStep = 1;

        /// <summary>
        /// 다음에 열릴 칸까지 열렸다고 친 격자.
        ///
        /// 칸은 인덱스 순서로 열리므로 <b>어디가 열릴지는 추정이 아니라 게임 사실이다</b>. 몇 번
        /// 열릴지는 사실이 아니므로 세지 않는다 - 되돌릴 수 없는 선택의 조언이 한 단계만
        /// 내다보는 것은 그 때문이다(docs/notes/LOOKAHEAD-2026-09-14.md).
        ///
        /// 더 열 칸이 없으면 자기 자신을 돌려준다. 부르는 쪽은 그것으로 "앞을 볼 것이 없다"를 안다.
        /// </summary>
        public GridSpec Grown(int cells)
        {
            var rows = Height > DefaultHeight ? Height : DefaultHeight;
            var storage = Storage + (cells > 0 ? cells : 0);
            if (storage > Width * rows) storage = Width * rows;
            if (storage <= Storage) return this;

            var height = (storage + Width - 1) / Width;
            return new GridSpec(Width, height > Height ? height : Height, storage);
        }

        public int ToIndex(int x, int y) => y * Width + x;
        public Model.GridPos ToPosition(int index) => new Model.GridPos(index % Width, index / Width);
    }
}
