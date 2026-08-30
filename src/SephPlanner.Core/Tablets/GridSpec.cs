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

        public int ToIndex(int x, int y) => y * Width + x;
        public Model.GridPos ToPosition(int index) => new Model.GridPos(index % Width, index / Width);
    }
}
