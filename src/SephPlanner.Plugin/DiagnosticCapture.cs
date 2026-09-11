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
                _status[name] = "저장됨";
                if (legacy) File.Copy(path, Path.Combine(_dataDirectory, name), true);
            }
            catch (Exception ex)
            {
                HasFailures = true;
                _status[name] = _text.Redact("저장 실패: " + ex.Message);
                _log("진단 " + name + " 저장 실패: " + ex);
            }
        }

        public void Finish(string producer, ReplayPreferences preferences, DiagnosticNote? note = null)
        {
            note ??= DiagnosticNote.None;
            Files["sephplanner.log"] = _text.Snapshot();
            Files["report.json"] = JsonConvert.SerializeObject(new
            {
                Version = DiagnosticArchive.SchemaVersion,
                ReportId = Id.ToString("N"),
                CapturedUtc = DateTime.UtcNow.ToString("O"),
                Producer = producer,
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
