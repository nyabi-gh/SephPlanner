using System;
using System.IO;
using UnityEngine;

namespace SephPlanner.Plugin.Host
{
    /// <summary>
    /// 플러그인을 띄운 쪽(BepInEx 또는 게임의 창작마당 모드 로더)이 알려 주는 것. 진입점이 <see cref="SephPlannerPlugin"/>
    /// 를 붙이기 전에 한 번 채운다.
    /// </summary>
    internal static class PluginHost
    {
        private static string _ownGameLogPrefix = "";

        public static ISettingStore Settings { get; private set; }

        /// <summary>로그와 진단 첫 줄에 남기는 로더 이름과 판.</summary>
        public static string Loader { get; private set; } = "";

        /// <summary>화면 안내가 가리키는 로그 파일. 번역하지 않는 파일 이름이다.</summary>
        public static string LogFile { get; private set; } = "";

        /// <param name="ownGameLogPrefix">
        /// 우리 로그가 유니티 로그로도 나가면 그 줄 머리. 진단이 게임 로그를 모을 때 우리 줄을 두 번 담지 않게 거른다.
        /// </param>
        public static void Use(ISettingStore settings, string loader, string logFile, string ownGameLogPrefix)
        {
            Settings = settings;
            Loader = loader;
            LogFile = logFile;
            _ownGameLogPrefix = ownGameLogPrefix ?? "";
        }

        public static bool IsOwnGameLogLine(string message) =>
            _ownGameLogPrefix.Length > 0 && message != null && message.StartsWith(_ownGameLogPrefix, StringComparison.Ordinal);

        /// <summary>
        /// 게임 폴더. macOS 는 <c>Application.dataPath</c> 가 <c>Sephiria.app/Contents</c> 라 앱 묶음 바깥까지 올라간다.
        /// </summary>
        public static string GameFolder
        {
            get
            {
                var folder = Path.GetDirectoryName(Application.dataPath);
                return Application.platform == RuntimePlatform.OSXPlayer ? Path.GetDirectoryName(folder) : folder;
            }
        }
    }
}
