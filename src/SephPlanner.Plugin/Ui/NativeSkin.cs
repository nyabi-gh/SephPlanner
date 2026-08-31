using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 게임에서 빌려 온 글꼴·판때기와, 오버레이가 쓰던 것과 같은 색.
    ///
    /// 글꼴은 게임이 지금 쓰고 있는 것을 그대로 가리킨다. 씬에 떠 있는 것을 참조할 뿐이라
    /// 추출도 배포도 하지 않는다(docs/LEGAL.md).
    ///
    /// <b>크기도 게임에서 온다.</b> HUD 캔버스는 픽셀 아트라 크게 확대돼 있어서 화면 픽셀을
    /// 생각하고 숫자를 넣으면 글자가 네 배로 나온다. 그래서 기준 크기를 우리가 정하지 않고
    /// HUD 글자 크기의 중앙값에서 가져오고, 나머지는 전부 그 비율로 잡는다.
    ///
    /// <b>판때기는 빌리지 않는다.</b> HUD 에서 9-slice 를 골라 쓰게 했더니 전체 화면짜리 선택
    /// 테두리를 물어 왔고, 속이 비어 있어 글자가 게임 위에 그대로 떴다. 어느 스프라이트가
    /// "창틀"인지 게임 데이터만으로는 가릴 수가 없다. 색은 오버레이가 게임 패널에서 채집해 둔
    /// 값이 이미 있으므로 그것으로 직접 그린다.
    /// </summary>
    /// <summary>
    /// 우리가 만든 화면임을 알리는 표시. 게임에서 크기를 빌릴 때 우리 글자를 걸러내는 데 쓴다.
    /// 붙이는 것 말고는 하는 일이 없다.
    /// </summary>
    internal sealed class SephPlannerWidget : MonoBehaviour
    {
    }

    internal sealed class NativeSkin
    {
        public TMP_FontAsset Font { get; private set; }
        public Material FontMaterial { get; private set; }

        /// <summary>게임 HUD 글자 크기의 중앙값. 우리 크기는 전부 여기에 대한 비율이다.</summary>
        public float BaseSize { get; private set; } = 12f;

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
