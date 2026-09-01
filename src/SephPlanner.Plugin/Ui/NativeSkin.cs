using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 우리가 만든 화면임을 알리는 표시. 게임에서 크기를 빌릴 때 우리 글자를 걸러내는 데 쓴다.
    /// 붙이는 것 말고는 하는 일이 없다.
    /// </summary>
    internal sealed class SephPlannerWidget : MonoBehaviour
    {
    }

    /// <summary>
    /// 게임이 로드한 글꼴과 HUD 글자 크기를 빌리고, 게임 패널에서 채집한 색으로 화면을 그린다.
    /// 씬의 글꼴을 참조할 뿐 추출하거나 배포하지 않는다.
    /// </summary>
    internal sealed class NativeSkin
    {
        public TMP_FontAsset Font { get; private set; }
        public Material FontMaterial { get; private set; }

        /// <summary>게임 HUD 글자 크기의 중앙값. 우리 크기는 전부 여기에 대한 비율이다.</summary>
        public float BaseSize { get; private set; } = 12f;

        /// <summary>무엇을 어디서 빌려 왔는지. 스파이크의 판단 근거라 로그로 남긴다.</summary>
        public string Origin { get; private set; } = "";

        // 게임 패널에서 채집한 팔레트.
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
            var sizes = new List<float>();

            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text.font == null || text.fontSize <= 0) continue;

                // 우리 글자는 세지 않는다. 우리 것은 기준 크기의 배수(0.65~1.2배)로 만들어지므로,
                // 다시 세면 중앙값이 우리 쪽으로 끌려 내려가고 그 값으로 또 만들게 된다. 화면을
                // 다시 지을 때마다 조금씩 작아지는 버그가 실제로 이것이었다 - 폭을 바꿀 때마다
                // 다시 짓기 때문에 눈에 띄게 줄어들었다.
                if (text.GetComponentInParent<SephPlannerWidget>(true) != null) continue;

                sizes.Add(text.fontSize);
                if (skin.Font != null) continue;

                skin.Font = text.font;
                skin.FontMaterial = text.fontSharedMaterial;
            }

            if (skin.Font == null)
            {
                skin.Font = Pick(
                    Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                    out var material);
                skin.FontMaterial = material;
            }

            if (sizes.Count > 0)
            {
                sizes.Sort();
                skin.BaseSize = sizes[sizes.Count / 2];
            }

            skin.Origin =
                "글꼴=" + (skin.Font != null ? skin.Font.name : "없음(기본)") +
                " 기준크기=" + skin.BaseSize.ToString("0.#") +
                " (HUD 글자 " + sizes.Count + "개)";
            return skin;
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

        private static Color Rgb(byte r, byte g, byte b) => new Color(r / 255f, g / 255f, b / 255f);
    }
}
