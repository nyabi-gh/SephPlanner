using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Text
{
    /// <summary>원문 한 줄과 그 번역들. 한 줄에 모든 언어를 함께 두어 하나만 고치고 잊는 일을 막는다.</summary>
    public sealed class Entry
    {
        public Entry(string korean, string en, string ja, string zhHans, string zhHant)
        {
            Korean = korean;
            English = en;
            Japanese = ja;
            ChineseSimplified = zhHans;
            ChineseTraditional = zhHant;
        }

        public string Korean { get; }
        public string English { get; }
        public string Japanese { get; }
        public string ChineseSimplified { get; }
        public string ChineseTraditional { get; }

        public string In(Language language) => language switch
        {
            Language.English => English,
            Language.Japanese => Japanese,
            Language.ChineseSimplified => ChineseSimplified,
            Language.ChineseTraditional => ChineseTraditional,
            _ => Korean,
        };
    }

    /// <summary>
    /// 번역 표. 영역마다 <c>Strings.영역.cs</c> 에 나눠 두고 여기서 모은다 - 한 파일이면 손대는 사람마다 같은
    /// 자리에서 부딪힌다.
    /// </summary>
    public static partial class Strings
    {
        private static Dictionary<string, Entry>? _table;

        public static IReadOnlyList<Entry> All
        {
            get
            {
                var all = new List<Entry>();
                foreach (var area in Areas()) all.AddRange(area);
                return all;
            }
        }

        internal static string? Find(string korean, Language language)
        {
            if (language == Language.Korean) return null;
            var table = _table ??= Build();
            if (!table.TryGetValue(korean, out var entry)) return null;
            var text = entry.In(language);
            if (text.Length > 0) return text;
            return entry.English.Length > 0 ? entry.English : null;
        }

        private static Dictionary<string, Entry> Build()
        {
            var table = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in All)
                table.TryAdd(entry.Korean, entry);
            return table;
        }

        private static IEnumerable<Entry[]> Areas()
        {
            yield return Common;
            yield return Planning;
            yield return Runtime;
            yield return Plugin;
            yield return Settings;
            yield return Hud;
        }
    }
}
