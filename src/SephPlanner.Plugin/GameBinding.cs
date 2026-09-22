using System;
using System.Collections.Generic;
using System.Reflection;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임의 비공개 멤버를 리플렉션으로 잡는 자리.
    ///
    /// 못 잡으면 부르는 쪽은 어림값으로 물러서는데, 그 물러섬이 조용하면 게임 패치로 이름이
    /// 바뀐 날 인챈트 남은 횟수가 틀리고 금고·시체 후보가 소리 없이 사라진다. 그래서 자리마다
    /// 한 번 알린다.
    ///
    /// 잡는 자리가 정적 초기화라 로그가 아직 없을 수 있다. 그때는 들고 있다가
    /// <see cref="LogTo"/> 가 오면 내보낸다 - 진단 묶음에 들어가는 것은 플러그인 로그뿐이라
    /// (<c>DiagnosticCapture.Finish</c>) 유니티 쪽으로 보내면 제보에서 보이지 않는다.
    /// </summary>
    internal static class GameBinding
    {
        private static readonly List<string> Pending = new List<string>();
        private static Action<object> _log;

        public static void LogTo(Action<object> log)
        {
            _log = log;
            foreach (var message in Pending) log(message);
            Pending.Clear();
        }

        public static FieldInfo Field(Type owner, string name) =>
            Found(owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic), owner, name);

        public static PropertyInfo Property(Type owner, string name) =>
            Found(owner.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic), owner, name);

        private static T Found<T>(T member, Type owner, string name) where T : class
        {
            if (member != null) return member;

            var message = "게임의 " + owner.Name + "." + name +
                " 을 찾지 못했습니다. 게임이 바뀌었을 수 있습니다 - 그 자리는 어림값으로 물러섭니다.";
            if (_log != null) _log(message);
            else Pending.Add(message);
            return null;
        }
    }
}
