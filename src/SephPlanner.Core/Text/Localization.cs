using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace SephPlanner.Core.Text
{
    public enum Language
    {
        Korean,
        English,
        Japanese,
        ChineseSimplified,
        ChineseTraditional,
    }

    /// <summary>
    /// 화면 문구의 번역. 원문은 한국어이고 그 원문이 곧 열쇠다 - 코드는 한국어 문장을 그대로 들고 있고,
    /// 다른 언어는 <see cref="Strings"/> 의 표에서 찾는다. 표에 없으면 영어, 그것도 없으면 원문이다.
    ///
    /// 지금 언어는 프로세스 하나에 하나다. 계산은 백그라운드에서 문장을 짓지만, 언어가 바뀌면 계획 지문이
    /// 바뀌어(<see cref="Runtime.PlanFingerprint.PlanningContext(Planning.PlanPreferences, string)"/>) 새 언어로 다시 푼다.
    /// </summary>
    public static class Localization
    {
        private static int _current;

        public static Language Current
        {
            get => (Language)Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, (int)value);
        }

        /// <summary>게임 <c>LocalizationManager</c> 가 쓰는 언어 코드.</summary>
        public static string Code(Language language) => language switch
        {
            Language.English => "en-US",
            Language.Japanese => "ja-JP",
            Language.ChineseSimplified => "zh-CN",
            Language.ChineseTraditional => "zh-TW",
            _ => "ko-KR",
        };

        public static bool TryParse(string? code, out Language language)
        {
            foreach (Language candidate in Enum.GetValues(typeof(Language)))
                if (string.Equals(Code(candidate), code, StringComparison.OrdinalIgnoreCase))
                {
                    language = candidate;
                    return true;
                }
            language = Language.Korean;
            return false;
        }

        /// <summary>
        /// 게임 언어를 우리 언어로. 우리가 번역하지 않은 언어(독일어 등)는 영어로 보인다 - 게임도 표에 없는
        /// 문장은 en-US 로 물러선다(<c>LocalizationManager.fallbackLanguage</c>).
        /// </summary>
        public static Language FromGame(string? code) => TryParse(code, out var language) ? language : Language.English;

        /// <summary>그 언어 이름을 그 언어로. 설정 창의 선택지에 쓴다.</summary>
        public static string NativeName(Language language) => language switch
        {
            Language.English => "English",
            Language.Japanese => "日本語",
            Language.ChineseSimplified => "简体中文",
            Language.ChineseTraditional => "繁體中文",
            _ => "한국어",
        };

        /// <summary>
        /// 원문을 지금 언어로 옮기고 인자를 채운다. 원문은 문자열 상수여야 한다 - 표를 찾는 열쇠이고,
        /// 테스트가 소스에서 <c>Tr("…")</c> 을 긁어 모든 언어에 번역이 있는지 본다.
        ///
        /// 인자 자리는 <c>{0}</c>·<c>{0:F1}</c> 처럼 <see cref="string.Format(IFormatProvider, string, object[])"/> 를 따른다.
        /// 한국어 조사는 <c>{0|을/를}</c> 로 적는다 - 받침 있는 말 뒤에 앞엣것, 없으면 뒤엣것이 붙는다.
        /// 번역문은 조사 없이 <c>{0}</c> 만 쓴다.
        /// </summary>
        public static string Tr(string korean, params object?[] args)
        {
            var template = Strings.Find(korean, Current) ?? korean;
            return Format(template, args);
        }

        internal static string Format(string template, object?[] args)
        {
            if (template.Contains('|')) template = Particles(template, args);
            return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
        }

        /// <summary><c>{n|A/B}</c> 를 <c>{n}</c> 과 고른 조사로 바꾼다.</summary>
        private static string Particles(string template, object?[] args)
        {
            var text = new StringBuilder(template.Length);
            var i = 0;
            while (i < template.Length)
            {
                var open = template.IndexOf('{', i);
                if (open < 0)
                {
                    text.Append(template, i, template.Length - i);
                    break;
                }
                var close = template.IndexOf('}', open);
                var bar = close < 0 ? -1 : template.IndexOf('|', open, close - open);
                if (bar < 0 || !int.TryParse(template.AsSpan(open + 1, bar - open - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    text.Append(template, i, open - i + 1);
                    i = open + 1;
                    continue;
                }
                var pair = template.Substring(bar + 1, close - bar - 1).Split('/');
                var word = index < args.Length ? Convert.ToString(args[index], CultureInfo.CurrentCulture) ?? "" : "";
                text.Append(template, i, open - i);
                text.Append('{').Append(index).Append('}');
                text.Append(pair.Length == 2 ? Particle(word, pair[0], pair[1]) : string.Join("/", pair));
                i = close + 1;
            }
            return text.ToString();
        }

        /// <summary>
        /// 받침을 보고 조사를 고른다. 한글로 끝나지 않는 이름은 받침을 알 수 없어 둘을 함께 적는다.
        /// </summary>
        public static string Particle(string word, string afterConsonant, string afterVowel)
        {
            if (word.Length == 0) return afterConsonant + "(" + afterVowel + ")";
            var last = word[word.Length - 1];
            if (last < '가' || last > '힣') return afterConsonant + "(" + afterVowel + ")";
            return (last - '가') % 28 != 0 ? afterConsonant : afterVowel;
        }

        /// <summary>테스트가 언어를 잠시 바꿨다 되돌린다.</summary>
        public static IDisposable Use(Language language)
        {
            var previous = Current;
            Current = language;
            return new Restore(previous);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Language _previous;
            public Restore(Language previous) { _previous = previous; }
            public void Dispose() => Current = _previous;
        }

        internal static IEnumerable<Language> Translated()
        {
            yield return Language.English;
            yield return Language.Japanese;
            yield return Language.ChineseSimplified;
            yield return Language.ChineseTraditional;
        }
    }
}
