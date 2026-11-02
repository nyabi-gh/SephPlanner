using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SephPlanner.Core.Runtime
{
    /// <summary>유니티 <c>LogType</c> 과 같은 뜻이다. Core 는 유니티를 참조하지 않는다.</summary>
    public enum GameLogKind
    {
        Error = 0,
        Assert = 1,
        Warning = 2,
        Log = 3,
        Exception = 4,
    }

    public sealed class DiagnosticText
    {
        private readonly string[] _privateRoots;
        private static readonly Regex UserDirectory = new Regex(
            @"(?i)\b[A-Z]:[\\/](?:Users|Documents and Settings)[\\/][^\\/\r\n<>"":|?*]+|/home/[^/\s]+|/Users/[^/\s]+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex Credentials = new Regex(
            @"(?im)(authorization\s*[:=]\s*|(?:access[_-]?token|api[_-]?key|password|secret)\s*[:=]\s*)[^\r\n]+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        // 게임이 경고·오류 줄에 남기는 것 중 남을 가리킬 수 있는 것들이다(1.0.33·1.1.1 디컴파일).
        // 로비 비밀번호는 "locked pw='…'", "password='…', saved='…'" 처럼 형식이 제각각이라 단어 뒤를 줄 끝까지 지운다.
        private static readonly Regex GameSecrets = new Regex(
            @"(?im)\b(pw|passwd|password|ticket|token)\b[^\r\n]*",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // 닉네임에는 공백이 들 수 있어 "member " 뒤는 쉼표까지 지운다.
        private static readonly Regex GameIdentities = new Regex(
            @"(?i)(\bmember\s+)[^,\r\n]+|(\bguid\s*[=:]\s*)[^\s,:()]+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // 로비 참가 코드는 숫자만이 아니다. 스팀 LobbyData 는 16진 HexId 로, EOS 는 lobbyId 문자열로 찍혀
        // 앞뒤 문구로만 알아본다.
        private static readonly Regex LobbyCodes = new Regex(
            @"(?i)(\bjoining lobby\s+|\blobby data unavailable:\s*|\blobby join timed out:\s*|\[SteamInvitation\]\s+Join\s+)[^\s,:()]+" +
            @"|[^\s:]+(?=\s*:\s*DIFF VERSION LOBBY\b)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        // 스팀 ID·로비 ID 는 17자리 안팎이다. 게임 수치가 이만큼 길어질 일은 없다.
        private static readonly Regex LongNumber = new Regex(
            @"\b\d{15,}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        // 잰 값이 아니라 추정이다. 게임 쪽 호출 지점과 그 위 두세 단계를 보기에 넉넉하다고 본 길이다.
        private const int StackLines = 12;

        private readonly object _gate = new object();
        private readonly Queue<string> _lines = new Queue<string>();
        private int _bytes;
        private readonly Dictionary<string, GameEntry> _game = new Dictionary<string, GameEntry>(StringComparer.Ordinal);
        private readonly Queue<string> _gameOrder = new Queue<string>();
        private int _gameBytes;

        public DiagnosticText(params string[] privateRoots) { _privateRoots = privateRoots; }

        public string Redact(string text)
        {
            foreach (var root in _privateRoots)
                if (!string.IsNullOrEmpty(root))
                {
                    text = text.Replace(root, "<로컬 경로>", StringComparison.OrdinalIgnoreCase);
                    text = text.Replace(root.Replace('\\', '/'), "<로컬 경로>", StringComparison.OrdinalIgnoreCase);
                }
            return Credentials.Replace(UserDirectory.Replace(text, "<사용자 폴더>"), "$1<가림>");
        }

        public string RedactGame(string text) =>
            LongNumber.Replace(
                LobbyCodes.Replace(GameIdentities.Replace(GameSecrets.Replace(Redact(text), "$1 <가림>"), "$1$2<가림>"), "$1<가림>"),
                "<번호>");

        public void Append(string line)
        {
            if (line.Length > DiagnosticArchive.MaximumLogBytes / 4)
                line = line.Substring(0, DiagnosticArchive.MaximumLogBytes / 4) + "\n[긴 로그 일부 생략]";
            lock (_gate)
            {
                _lines.Enqueue(line);
                _bytes += Encoding.UTF8.GetByteCount(line) + 1;
                while (_bytes > DiagnosticArchive.MaximumLogBytes && _lines.Count > 0)
                    _bytes -= Encoding.UTF8.GetByteCount(_lines.Dequeue()) + 1;
            }
        }

        /// <summary>
        /// 게임 로그(Player.log 에 가는 것)에서 경고·오류만 남긴다. 일반 줄은 닉네임·로비 이름을 싣는 것이 많아
        /// 처음부터 받지 않는다. 같은 문장이 매 프레임 되풀이되기도 하므로 문장마다 한 항목에 횟수만 센다.
        /// 유니티가 아무 스레드에서나 부른다.
        /// </summary>
        public void AppendGame(GameLogKind kind, string message, string? stackTrace, DateTime utc)
        {
            if (kind == GameLogKind.Log) return;
            message ??= "";
            // 문장 하나가 몫을 다 먹지 않게 하는 추정 상한이다(전체의 1/8). 실제 문장 길이 분포는 재지 않았다.
            if (message.Length > DiagnosticArchive.MaximumGameLogBytes / 8)
                message = message.Substring(0, DiagnosticArchive.MaximumGameLogBytes / 8) + " [일부 생략]";
            var key = kind + "\u0001" + message;
            lock (_gate)
            {
                if (_game.TryGetValue(key, out var seen))
                {
                    seen.Count++;
                    seen.LastUtc = utc;
                    return;
                }
                var entry = new GameEntry(kind, message, ShortStack(kind, stackTrace), utc);
                _game.Add(key, entry);
                _gameOrder.Enqueue(key);
                _gameBytes += entry.Bytes;
                while (_gameBytes > DiagnosticArchive.MaximumGameLogBytes && _gameOrder.Count > 0)
                {
                    var oldest = _gameOrder.Dequeue();
                    _gameBytes -= _game[oldest].Bytes;
                    _game.Remove(oldest);
                }
            }
        }

        public string Snapshot()
        {
            lock (_gate)
            {
                var own = Redact(string.Join("\n", _lines));
                if (_gameOrder.Count == 0) return own;
                var game = new StringBuilder();
                game.Append("\n\n[게임 경고·오류] 처음 본 순서, 같은 문장은 한 번만\n");
                foreach (var key in _gameOrder) _game[key].WriteTo(game);
                return own + RedactGame(game.ToString());
            }
        }

        private static string? ShortStack(GameLogKind kind, string? stackTrace)
        {
            if (kind == GameLogKind.Warning || string.IsNullOrWhiteSpace(stackTrace)) return null;
            var lines = stackTrace!.Split(new[] { '\n' }, StackLines + 1, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > StackLines
                ? string.Join("\n", lines, 0, StackLines) + "\n…"
                : string.Join("\n", lines);
        }

        private sealed class GameEntry
        {
            private readonly GameLogKind _kind;
            private readonly string _message;
            private readonly string? _stack;
            private readonly DateTime _firstUtc;
            public int Count = 1;
            public DateTime LastUtc;
            public int Bytes { get; }

            public GameEntry(GameLogKind kind, string message, string? stack, DateTime utc)
            {
                _kind = kind;
                _message = message;
                _stack = stack;
                _firstUtc = utc;
                LastUtc = utc;
                Bytes = Encoding.UTF8.GetByteCount(message) + (stack == null ? 0 : Encoding.UTF8.GetByteCount(stack)) + 64;
            }

            public void WriteTo(StringBuilder text)
            {
                text.Append(_firstUtc.ToString("O")).Append(" [").Append(_kind).Append("] ").Append(_message);
                if (Count > 1) text.Append(" (").Append(Count).Append("회, 마지막 ").Append(LastUtc.ToString("O")).Append(')');
                text.Append('\n');
                if (_stack != null)
                    foreach (var line in _stack.Split('\n')) text.Append("    ").Append(line.TrimEnd('\r')).Append('\n');
            }
        }
    }
}
