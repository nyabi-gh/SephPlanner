using System;
using System.Collections.Generic;
using System.IO;
using SephPlanner.Plugin.Host;

namespace SephPlanner.Plugin.Workshop
{
    /// <summary>
    /// BepInEx 판에서 옮겨 온 사람의 설정을 한 번 가져온다. 창작마당 판의 저장소에 아직 없는 값만 여기서 읽는다
    /// (<see cref="ModOptionsSettingStore"/>). 파일은 고치지도 지우지도 않는다.
    /// </summary>
    internal static class BepInExConfigImport
    {
        /// <summary>
        /// 진단 전송 동의는 가져오지 않는다. BepInEx 판에서 한 동의를 창작마당 판의 동의로 넘기면 이 판에서는 무엇을
        /// 보내는지 한 번도 보지 않고 전송이 켜진다 - 창작마당 규칙 5 는 보내는 것을 이용자가 알게 하라고 한다.
        /// </summary>
        private const string ConsentSection = "Diagnostics";

        /// <summary>"섹션.열쇠" → 설정 파일에 적힌 글자 그대로. 파일이 없거나 못 읽으면 비어 있다.</summary>
        public static IReadOnlyDictionary<string, string> Read(string gameFolder)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var path = Path.Combine(gameFolder, "BepInEx", "config", PluginIdentity.Guid + ".cfg");
            if (!File.Exists(path)) return values;
            try
            {
                Parse(File.ReadAllLines(path), values);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning("BepInEx 설정 파일을 읽지 못해 옛 설정을 가져오지 않습니다: " + ex.Message);
                values.Clear();
            }
            return values;
        }

        /// <summary>
        /// BepInEx 설정 파일은 <c>[섹션]</c> 아래 <c>열쇠 = 값</c> 줄과 <c>#</c> 설명 줄이다. 글자 값은 BepInEx 가 역슬래시로
        /// 감싸 적는데, 우리 글자 설정(언어)에는 감쌀 글자가 없어 풀지 않는다.
        /// </summary>
        internal static void Parse(IEnumerable<string> lines, IDictionary<string, string> values)
        {
            var section = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                var split = line.IndexOf('=');
                if (split <= 0) continue;
                if (section == ConsentSection) continue;
                values[section + "." + line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
        }
    }
}
