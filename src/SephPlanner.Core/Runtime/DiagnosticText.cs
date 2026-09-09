using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SephPlanner.Core.Runtime
{
    public sealed class DiagnosticText
    {
        private readonly string[] _privateRoots;
        private static readonly Regex UserDirectory = new Regex(
            @"(?i)\b[A-Z]:[\\/](?:Users|Documents and Settings)[\\/][^\\/\r\n<>"":|?*]+|/home/[^/\s]+|/Users/[^/\s]+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex Credentials = new Regex(
            @"(?im)(authorization\s*[:=]\s*|(?:access[_-]?token|api[_-]?key|password|secret)\s*[:=]\s*)[^\r\n]+",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private readonly object _gate = new object();
        private readonly Queue<string> _lines = new Queue<string>();
        private int _bytes;

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

        public string Snapshot()
        {
            lock (_gate) return Redact(string.Join("\n", _lines));
        }
    }
}
