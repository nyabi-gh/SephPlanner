using System.Windows;
using System.Windows.Media;

namespace SephPlanner.Overlay;

/// <summary>
/// 오버레이의 색과 글꼴. 세피리아 자체 UI(가방·콤보 효과·스킬 패널)에서 채집한 값이라,
/// 여기서 벗어난 색을 쓰면 오버레이만 게임과 따로 놀게 된다.
/// </summary>
public static class Theme
{
    // 글꼴: Galmuri (OFL-1.1, Fonts/LICENSE.txt). 픽셀 원본 크기가 있어서
    // 본문은 12(Galmuri11), 작은 글씨는 10(Galmuri9), 점수는 30(Galmuri14의 2배)으로 맞춘다.
    public static readonly FontFamily Body = new(new Uri("pack://application:,,,/"), "./Fonts/#Galmuri11");
    public static readonly FontFamily Small = new(new Uri("pack://application:,,,/"), "./Fonts/#Galmuri9");
    public static readonly FontFamily Score = new(new Uri("pack://application:,,,/"), "./Fonts/#Galmuri14");

    // 패널 골격: 어두운 윤곽선 - 장미빛 프레임 - 어두운 윤곽선 - 자주빛 갈색 속.
    public static readonly Brush Outline = Frozen(0x10, 0x0A, 0x0C);
    public static readonly Brush Frame = Frozen(0xA9, 0x7C, 0x74);
    public static readonly Brush Panel = Frozen(0x24, 0x1A, 0x1D);

    // 크림색 텍스트 3단계. 보라끼 도는 회색은 게임에 없는 색이라 전부 걷어냈다.
    public static readonly Brush TextBright = Frozen(0xF2, 0xE8, 0xD2);
    public static readonly Brush Text = Frozen(0xD9, 0xCB, 0xB2);
    public static readonly Brush TextDim = Frozen(0x9C, 0x8B, 0x7C);

    // 상태색. 게임의 카운터(초록 2/2, 빨강 -1)와 같은 의미로만 쓴다.
    public static readonly Brush Good = Frozen(0x66, 0xD9, 0x6E);
    public static readonly Brush Bad = Frozen(0xE8, 0x60, 0x52);
    public static readonly Brush Amber = Frozen(0xE0, 0xA6, 0x3C);

    /// <summary>옮길 자리 테두리 전용. 경고(Amber)·낭비(Orange)와 뜻이 섞이지 않게 분리한다.</summary>
    public static readonly Brush GoldEdge = Frozen(0xF2, 0xC1, 0x4E);

    /// <summary>상한에 걸려 낭비되는 레벨 전용.</summary>
    public static readonly Brush Orange = Frozen(0xE0, 0x88, 0x40);

    // 격자 칸: 가방 슬롯의 벽돌색, 석판 타일의 슬레이트 블루.
    public static readonly Brush SlotFill = Frozen(0x4A, 0x33, 0x3A);
    public static readonly Brush SlotEdge = Frozen(0x2A, 0x1D, 0x21);
    public static readonly Brush EmptyFill = Frozen(0x1C, 0x14, 0x17);
    public static readonly Brush ClosedFill = Frozen(0x16, 0x10, 0x13);
    public static readonly Brush TabletFill = Frozen(0x2C, 0x39, 0x46);
    public static readonly Brush TabletEdge = Frozen(0x4E, 0x6B, 0x85);
    public static readonly Brush TabletText = Frozen(0xA8, 0xC6, 0xDF);

    /// <summary>스킬 패널 프레임의 민트. 후보 목록 머리글에만 쓴다.</summary>
    public static readonly Brush Mint = Frozen(0x9B, 0xD6, 0xAD);

    /// <summary>
    /// 색이 아니라 마우스를 받기 위한 배경. WPF 는 배경이 없는 영역에서는 클릭이 통과해 버려
    /// 줄 전체를 누를 수 없다.
    /// </summary>
    public static readonly Brush Hit = Transparent();

    /// <summary>미리보기로 골라 둔 줄. 가방 슬롯과 같은 벽돌색이라 새 색을 들이지 않는다.</summary>
    public static readonly Brush RowPicked = SlotFill;

    private static SolidColorBrush Transparent()
    {
        var brush = new SolidColorBrush(Colors.Transparent);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
