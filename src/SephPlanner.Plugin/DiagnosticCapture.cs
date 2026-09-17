#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Plugin
{
    internal sealed class DiagnosticCapture
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string DirectoryPath { get; }
        public bool HasFailures { get; private set; }
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _status = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _partial = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly DiagnosticText _text;
        private readonly Action<object> _log;
        private readonly string _dataDirectory;

        public DiagnosticCapture(DiagnosticText text, Action<object> log, string? dataDirectory = null)
        {
            _text = text;
            _log = log;
            _dataDirectory = dataDirectory ?? PlannerData.DataDirectory;
            DirectoryPath = Path.Combine(_dataDirectory, "reports", Id.ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Collect(string name, Func<string?> write, bool legacy = false)
        {
            try
            {
                var path = write();
                if (path == null)
                {
                    _status[name] = "이번 수집에 자료가 없습니다.";
                    return;
                }
                var content = File.ReadAllText(path);
                Files.Add(name, name == "inventory-dump.txt" ? _text.Redact(content) : content);
                _status[name] = _partial.TryGetValue(name, out var partial)
                    ? "저장됨 (일부 실패: " + partial + ")"
                    : "저장됨";
                if (legacy) File.Copy(path, Path.Combine(_dataDirectory, name), true);
            }
            catch (Exception ex)
            {
                HasFailures = true;
                _status[name] = _text.Redact("저장 실패: " + ex.Message);
                _log("진단 " + name + " 저장 실패: " + ex);
            }
        }

        /// <summary>
        /// 파일은 저장됐지만 일부가 빠졌다. <see cref="Collect"/> 가 "저장됨" 으로 덮어쓰지 않도록
        /// 따로 들고 있다가 합친다 - 요약만 보는 사람에게 부분 실패가 보이지 않으면 없는 일이 된다.
        /// </summary>
        public void Warn(string name, string message)
        {
            HasFailures = true;
            var text = _text.Redact(message);
            _partial[name] = _partial.TryGetValue(name, out var existing) ? existing + " / " + text : text;
        }

        public void Finish(string producer, ReplayPreferences preferences, DiagnosticNote? note = null, object? incident = null, string? incidentLog = null)
        {
            note ??= DiagnosticNote.None;
            Files["sephplanner.log"] = incidentLog ?? _text.Snapshot();
            Files["report.json"] = JsonConvert.SerializeObject(new
            {
                Version = DiagnosticArchive.SchemaVersion,
                ReportId = Id.ToString("N"),
                CapturedUtc = DateTime.UtcNow.ToString("O"),
                Producer = producer,
                Incident = incident,
                CoreBuild = PlanReplay.CurrentCoreBuild,
                // 사용자가 직접 적은 것이다. 가리지 않고 그대로 보낸다.
                Note = note.IsEmpty ? null : new { note.Category, note.CategoryLabel, note.Text },
                CurrentPreferences = preferences,
                Collection = _status,
            }, Formatting.Indented);
            File.WriteAllText(Path.Combine(DirectoryPath, "report.json"), Files["report.json"]);
            File.WriteAllText(Path.Combine(DirectoryPath, "sephplanner.log"), Files["sephplanner.log"]);
        }
    }
}
