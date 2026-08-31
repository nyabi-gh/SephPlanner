using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 게임에서 빌려 온 글꼴·판때기와, 오버레이가 쓰던 것과 같은 색.
    ///
    /// 오버레이의 <c>Theme</c>은 게임 화면을 보고 색을 손으로 채집하고 Galmuri 를 임베드해서
    /// "게임처럼 보이게" 맞춘 것이었다. 여기서는 맞출 것이 없다 - 글꼴도 판때기도 게임이 지금
    /// 쓰고 있는 그 물건을 그대로 가리킨다. 씬에 떠 있는 것을 참조할 뿐이라 추출도 배포도
    /// 하지 않는다(docs/LEGAL.md).
    ///
    /// 색만은 여전히 우리 값이다. 게임 UI 에서 색만 뽑아낼 방법이 없고, 채집해 둔 값이 이미
    /// 게임 패널에서 온 것이라 그대로 옮겼다.
    /// </summary>
    internal sealed class NativeSkin
    {
        public TMP_FontAsset Font { get; private set; }
        public Material FontMaterial { get; private set; }

        /// <summary>게임 패널의 9-slice 판때기. 못 찾으면 null 이고 그때는 색으로만 그린다.</summary>
        public Sprite Panel { get; private set; }
        public Image.Type PanelType { get; private set; } = Image.Type.Sliced;
        public Color PanelTint { get; private set; } = Color.white;

        /// <summary>무엇을 어디서 빌려 왔는지. 스파이크의 판단 근거라 로그로 남긴다.</summary>
        public string Origin { get; private set; } = "";

        // 오버레이 Theme 과 같은 값. 한쪽만 고치면 두 화면이 갈라지므로 여기를 옮길 때 저쪽도 본다.
        public static readonly Color Outline = Rgb(0x10, 0x0A, 0x0C);
        public static readonly Color Frame = Rgb(0xA9, 0x7C, 0x74);
        public static readonly Color PanelFill = Rgb(0x24, 0x1A, 0x1D);
        public static readonly Color TextBright = Rgb(0xF2, 0xE8, 0xD2);
        public static readonly Color Text = Rgb(0xD9, 0xCB, 0xB2);
        public static readonly Color TextDim = Rgb(0x9C, 0x8B, 0x7C);
        public static readonly Color Good = Rgb(0x66, 0xD9, 0x6E);
        public static readonly Color Bad = Rgb(0xE8, 0x60, 0x52);
        public static readonly Color Amber = Rgb(0xE0, 0xA6, 0x3C);
        public static readonly Color GoldEdge = Rgb(0xF2, 0xC1, 0x4E);
        public static readonly Color Orange = Rgb(0xE0, 0x88, 0x40);
        public static readonly Color SlotFill = Rgb(0x4A, 0x33, 0x3A);
        public static readonly Color SlotEdge = Rgb(0x2A, 0x1D, 0x21);
        public static readonly Color EmptyFill = Rgb(0x1C, 0x14, 0x17);
        public static readonly Color ClosedFill = Rgb(0x16, 0x10, 0x13);
        public static readonly Color TabletFill = Rgb(0x2C, 0x39, 0x46);
        public static readonly Color TabletEdge = Rgb(0x4E, 0x6B, 0x85);
        public static readonly Color TabletText = Rgb(0xA8, 0xC6, 0xDF);
        public static readonly Color Mint = Rgb(0x9B, 0xD6, 0xAD);

        public static NativeSkin Borrow(UIRoot root)
        {
            var skin = new NativeSkin();
            var font = FindFont(root, out var material);
            skin.Font = font;
            skin.FontMaterial = material;

            var template = FindPanelImage(root);
            if (template != null)
            {
                skin.Panel = template.sprite;
                skin.PanelType = template.type;
                skin.PanelTint = template.color;
            }

            skin.Origin =
                "글꼴=" + (font != null ? font.name : "없음(기본)") +
                " 판때기=" + (skin.Panel != null ? skin.Panel.name : "없음(색으로 대체)");
            return skin;
        }

        /// <summary>
        /// 같은 HUD 안의 글자를 먼저 찾는다. 씬 아무 데서나 집으면 월드 말풍선처럼 다른 크기·재질로
        /// 꾸며 둔 글자를 물어 와서, 정작 HUD 옆에 놓았을 때 혼자 튄다.
        /// </summary>
        private static TMP_FontAsset FindFont(UIRoot root, out Material material)
        {
            var inHud = Pick(root.GetComponentsInChildren<TMP_Text>(true), out material);
            if (inHud != null) return inHud;

            return Pick(
                Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                out material);
        }

        private static TMP_FontAsset Pick(TMP_Text[] texts, out Material material)
        {
            material = null;
            foreach (var text in texts)
            {
                if (text == null || text.font == null) continue;

                material = text.fontSharedMaterial;
                return text.font;
            }
            return null;
        }

        /// <summary>
        /// 테두리(9-slice)가 있는 것만 고른다. 테두리 없는 그림을 늘리면 뭉개져서 오히려 게임과
        /// 달라 보인다. 큰 것일수록 창틀일 가능성이 높고 작은 것은 버튼이나 칸 테두리다.
        /// </summary>
        private static Image FindPanelImage(UIRoot root)
        {
            Image best = null;
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite == null) continue;
                if (image.sprite.border == Vector4.zero) continue;
                if (best == null || Area(image) > Area(best)) best = image;
            }
            return best;
        }

        private static float Area(Image image)
        {
            var size = image.rectTransform.rect.size;
            return size.x * size.y;
        }

        private static Color Rgb(byte r, byte g, byte b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
