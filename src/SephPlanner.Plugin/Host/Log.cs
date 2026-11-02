using System;

namespace SephPlanner.Plugin.Host
{
    internal enum LogLevel { Info, Warning, Error }

    /// <summary>
    /// 플러그인의 로그. 어디에 남는지(BepInEx 로그, 게임의 Player.log)는 진입점이 정하고, 진단은 <see cref="Written"/>
    /// 으로 같은 줄을 받아 둔다.
    /// </summary>
    internal static class Log
    {
        private static Action<LogLevel, object> _sink = (_, message) => UnityEngine.Debug.Log(message);

        public static event Action<LogLevel, string> Written;

        public static void Use(Action<LogLevel, object> sink) => _sink = sink;

        public static void Info(object message) => Write(LogLevel.Info, message);

        public static void Warning(object message) => Write(LogLevel.Warning, message);

        public static void Error(object message) => Write(LogLevel.Error, message);

        private static void Write(LogLevel level, object message)
        {
            _sink(level, message);
            Written?.Invoke(level, message?.ToString() ?? "");
        }
    }
}
