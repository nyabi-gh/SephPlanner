using System.Text;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 게임 텍스트에서 TextMeshPro 서식 태그를 걷어낸다. 게임이 만들어 주는 효과 설명에는
    /// <c>&lt;color=…&gt;</c>, <c>&lt;sprite=…&gt;</c>, <c>&lt;indent&gt;</c> 같은 것이 섞여 있는데
    /// WPF 는 그 문법을 모른다. 색을 옮겨오지 않는 것은 의도이기도 하다 — 오버레이 색은 Theme 에서만
    /// 가져온다.
    /// </summary>
    public static class RichText
    {
        public static string Strip(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var builder = new StringBuilder(text!.Length);
            var depth = 0;

            foreach (var c in text)
            {
                if (c == '<') { depth++; continue; }
                if (c == '>')
                {
                    // 짝이 맞지 않는 '>'는 태그가 아니라 부등호다. 게임 문구에 실제로 나온다.
                    if (depth > 0) depth--;
                    else builder.Append(c);
                    continue;
                }
                if (depth == 0) builder.Append(c);
            }

            return Squeeze(builder.ToString());
        }

        /// <summary>태그가 빠진 자리에 남는 겹공백을 하나로 줄인다.</summary>
        private static string Squeeze(string text)
        {
            var builder = new StringBuilder(text.Length);
            var space = false;

            foreach (var c in text)
            {
                if (c == ' ' || c == '\t')
                {
                    space = true;
                    continue;
                }
                if (space && builder.Length > 0) builder.Append(' ');
                space = false;
                builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
