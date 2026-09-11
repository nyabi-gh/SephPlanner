using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SephPlanner.Core.Runtime
{
    /// <summary>증상 분류 하나. 타이핑 없이 누르기만 해도 제보가 갈리도록 둔다.</summary>
    public sealed class DiagnosticCategory
    {
        public DiagnosticCategory(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
    }

    /// <summary>
    /// F10 진단에 사용자가 직접 붙이는 메모.
    ///
    /// 진단 자료만으로는 무엇이 이상했는지 알 수 없다 - 배치와 로그는 남지만 사용자가 무엇을
    /// 기대했는지는 남지 않는다. 분류 하나와 자유 입력 한 덩어리가 그 자리를 메운다.
    ///
    /// 사용자가 화면을 보며 직접 적어 보내는 것이라 자동 수집 항목이 아니다. 그래서 전송 동의를
    /// 다시 받지 않으며 <see cref="DiagnosticArchive.SchemaVersion"/>도 올리지 않는다. 대신 우리가
    /// 가리는 것도 없다 - 적은 그대로 간다.
    /// </summary>
    public sealed class DiagnosticNote
    {
        /// <summary>초기 운영 제한값이다. 화면의 입력 칸도 같은 값을 쓴다.</summary>
        public const int MaximumTextLength = 500;

        public static readonly DiagnosticNote None = new DiagnosticNote("", "");

        public static readonly IReadOnlyList<DiagnosticCategory> Categories = new[]
        {
            new DiagnosticCategory("placement", "배치가 이상함"),
            new DiagnosticCategory("score", "점수·추천이 이상함"),
            new DiagnosticCategory("autoplace", "자동 배치 실패"),
            new DiagnosticCategory("display", "화면 표시"),
            new DiagnosticCategory("other", "기타"),
        };

        private DiagnosticNote(string category, string text)
        {
            Category = category;
            Text = text;
        }

        /// <summary>고른 분류의 식별자. 고르지 않았으면 빈 문자열이다.</summary>
        public string Category { get; }

        public string Text { get; }

        public bool IsEmpty => Category.Length == 0 && Text.Length == 0;

        /// <summary>고른 분류의 사람이 읽는 이름. 모르는 식별자면 빈 문자열이다.</summary>
        public string CategoryLabel => LabelOf(Category);

        public static string LabelOf(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            foreach (var category in Categories)
                if (string.Equals(category.Id, id, StringComparison.Ordinal)) return category.Label;
            return "";
        }

        /// <summary>
        /// 화면에서 받은 값을 보관할 수 있는 모양으로 만든다. 모르는 분류와 빈 글은 없는 것으로
        /// 친다 - 빈 메모를 보내 놓고 사용자가 무언가 말했다고 읽히면 안 된다.
        /// </summary>
        public static DiagnosticNote Create(string? category, string? text)
        {
            var id = LabelOf(category).Length == 0 ? "" : category!;
            var body = Clean(text);
            return id.Length == 0 && body.Length == 0 ? None : new DiagnosticNote(id, body);
        }

        private static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text!.Length);
            var newlines = 0;
            foreach (var character in text)
            {
                if (character == '\n')
                {
                    // 빈 줄 하나까지는 문단을 가르는 뜻이 있다. 그 이상은 길이만 먹는다.
                    if (++newlines > 2) continue;
                    builder.Append(character);
                    continue;
                }
                newlines = 0;
                // 제어문자는 로그와 화면을 망가뜨린다. 줄바꿈만 남기고 공백으로 바꾼다.
                builder.Append(CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Control ? ' ' : character);
            }
            return Truncate(builder.ToString().Trim());
        }

        private static string Truncate(string text)
        {
            if (text.Length <= MaximumTextLength) return text;
            var length = MaximumTextLength;
            // 이모지는 두 칸을 차지한다. 가운데서 자르면 짝이 깨져 올바르지 않은 UTF-16 이 된다.
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length).TrimEnd();
        }
    }
}
