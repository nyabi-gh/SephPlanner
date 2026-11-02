using System;
using System.Globalization;
using System.Text;

namespace SephPlanner.Core.Text
{
    /// <summary>
    /// 화면과 로그·진단에 함께 가는 문장. 화면은 지금 언어로, 로그·진단은 한국어로 남아야 해서 지을 때 옮기지
    /// 않고 원문과 인자를 들고 있다가 쓸 때 옮긴다 - <see cref="ToString"/> 은 지금 언어, <see cref="Korean"/> 은
    /// 로그·진단에 쓴다. 인자로 든 문장은 같은 언어로 함께 옮긴다. 화면에만 가는 문장은 그대로
    /// <see cref="Localization.Tr"/> 로 짓는다.
    ///
    /// 원문은 <see cref="Localization.Tr"/> 와 같이 문자열 상수여야 한다. 테스트가 소스에서 <c>Phrase("…")</c> 를
    /// 긁어 번역이 있는지 본다.
    /// </summary>
    public sealed class Phrase
    {
        private const string ExceptionKey = "SephPlanner.Phrase";

        // 원문이 null 이면 옮기지 않는다 - _parts 를 그대로 잇는다.
        private readonly string? _korean;
        private readonly object?[] _parts;
        private readonly string _separator;

        public Phrase(string korean, params object?[] args)
        {
            _korean = korean;
            _parts = args;
            _separator = "";
        }

        private Phrase(object?[] parts, string separator)
        {
            _korean = null;
            _parts = parts;
            _separator = separator;
        }

        /// <summary>옮기지 않는 글. 예외 메시지·경로·게임 이름처럼 원문이 없는 것.</summary>
        public static Phrase Verbatim(string text) => new Phrase(new object?[] { text }, "");

        /// <summary>조각을 그대로 잇는다. 문장인 조각은 같은 언어로 옮긴다.</summary>
        public static Phrase Concat(params object?[] parts) => new Phrase(parts, "");

        /// <summary>비지 않은 조각만 사이에 <paramref name="separator"/> 를 두고 잇는다.</summary>
        public static Phrase Join(string separator, params object?[] parts) => new Phrase(parts, separator);

        /// <summary>
        /// 예외가 든 문장. <see cref="As{T}"/> 로 지은 예외가 아니면 메시지를 그대로 쓴다 - 시스템 예외의 메시지는
        /// 원문이 없다. <paramref name="plain"/> 은 그 메시지에만 건다. 우리가 지은 문장은 원문과 인자가 정해져
        /// 있지만, 시스템 예외의 메시지에는 사용자 경로가 들 수 있다.
        /// </summary>
        public static Phrase Of(Exception exception, Func<string, string>? plain = null) =>
            exception.Data[ExceptionKey] as Phrase ??
            Verbatim(plain == null ? exception.Message : plain(exception.Message));

        public string Korean => In(Language.Korean);

        public string In(Language language)
        {
            if (_korean != null)
            {
                var template = Strings.Find(_korean, language) ?? _korean;
                return Localization.Format(template, Rendered(language));
            }

            var text = new StringBuilder();
            foreach (var part in _parts)
            {
                var piece = Render(part, language);
                if (piece.Length == 0) continue;
                if (text.Length > 0) text.Append(_separator);
                text.Append(piece);
            }
            return text.ToString();
        }

        public override string ToString() => In(Localization.Current);

        /// <summary>
        /// 이 문장을 든 예외를 짓는다. 메시지는 로그·진단에 남으므로 한국어이고, 화면은 <see cref="Of"/> 로
        /// 지금 언어를 꺼낸다. 예외의 형식은 부르는 쪽이 정한다 - 잡는 쪽이 형식으로 가른다.
        /// </summary>
        public T As<T>(Func<string, T> create) where T : Exception
        {
            var exception = create(Korean);
            AttachTo(exception);
            return exception;
        }

        /// <summary>제 생성자에서 메시지를 짓는 예외가 문장을 단다. 메시지는 <see cref="Korean"/> 이어야 한다.</summary>
        public void AttachTo(Exception exception) => exception.Data[ExceptionKey] = this;

        private object?[] Rendered(Language language)
        {
            if (Array.TrueForAll(_parts, part => part is not Phrase)) return _parts;
            var rendered = new object?[_parts.Length];
            for (var i = 0; i < _parts.Length; i++)
                rendered[i] = _parts[i] is Phrase phrase ? phrase.In(language) : _parts[i];
            return rendered;
        }

        private static string Render(object? part, Language language) => part switch
        {
            null => "",
            Phrase phrase => phrase.In(language),
            string text => text,
            _ => Convert.ToString(part, CultureInfo.CurrentCulture) ?? "",
        };
    }
}
