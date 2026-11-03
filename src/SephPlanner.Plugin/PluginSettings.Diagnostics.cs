using System;
using System.Collections.Generic;
using SephPlanner.Plugin.Host;
using SephPlanner.Plugin.Net;
using SephPlanner.Plugin.Ui;
using static SephPlanner.Core.Text.Localization;

namespace SephPlanner.Plugin
{
    /// <summary>진단 전송 동의. 두 판 모두 있다.</summary>
    internal sealed partial class PluginSettings
    {
        public Setting<string> DiagnosticConsent { get; private set; }
        public Setting<bool> DiagnosticChoiceMade { get; private set; }
        public Setting<string> AutomaticDiagnosticConsent { get; private set; }
        public Setting<string> AutomaticDiagnosticChoice { get; private set; }

        /// <summary>F3 에서 전송을 켜려 할 때 동의 창을 띄운다. 플러그인이 창을 만든 뒤 채운다.</summary>
        public Action ReviewDiagnostics { get; set; } = () => { };

        public Action ReviewAutomaticDiagnostics { get; set; } = () => { };

        public static string AutomaticDiagnosticKey => AutomaticDiagnosticPolicy.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint));

        public bool AutomaticDiagnosticAllowed => AutomaticDiagnosticConsent.Value == AutomaticDiagnosticKey;

        public bool DiagnosticUploadAllowed => DiagnosticConsent.Value ==
            DiagnosticUploadClient.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint));

        private void BindDiagnostics(ISettingStore config)
        {
            AutomaticDiagnosticConsent = config.Bind("Diagnostics", "AutomaticUploadConsent", "",
                "오류 자동 전송에 별도로 동의한 항목과 수신처. F3에서 철회할 수 있습니다.");
            AutomaticDiagnosticChoice = config.Bind("Diagnostics", "AutomaticUploadChoice", "",
                "자동 전송 안내에 답한 버전. 플러그인 버전만 바뀌면 다시 묻지 않습니다.");
            DiagnosticConsent = config.Bind("Diagnostics", "UploadConsent", "",
                "F10 진단을 비공개 서버로 전송하는 데 동의한 대상과 항목 버전. F3에서 변경합니다.");
            DiagnosticChoiceMade = config.Bind("Diagnostics", "UploadChoiceMade", false,
                "F10 진단 전송 여부를 사용자가 선택했는지 기록합니다.");
        }

        private void AddDiagnosticRows(List<OptionRow> rows)
        {
            var updates = new List<OptionRow>();
            AddUpdateRows(updates);
            rows.Add(OptionRow.Heading(updates.Count > 0 ? Tr("진단·업데이트") : Tr("진단")));
            rows.Add(new OptionRow
            {
                Label = Tr("진단 서버 전송"),
                Choices = new[] { Tr("꺼짐"), Tr("켜짐") },
                Read = () => DiagnosticUploadAllowed ? 1 : 0,
                Write = i => { if (i == 0) SetDiagnosticConsent(false); else ReviewDiagnostics(); },
            });
            rows.Add(new OptionRow
            {
                Label = Tr("오류 진단 자동 전송"),
                Choices = new[] { Tr("꺼짐"), Tr("켜짐") },
                Read = () => AutomaticDiagnosticAllowed ? 1 : 0,
                Write = i => { if (i == 0) SetAutomaticDiagnosticConsent(false); else ReviewAutomaticDiagnostics(); },
            });
            rows.AddRange(updates);
        }

        public void SetAutomaticDiagnosticConsent(bool allowed)
        {
            var previousChoice = AutomaticDiagnosticChoice.Value;
            try
            {
                AutomaticDiagnosticConsent.Value = allowed ? AutomaticDiagnosticKey : "";
                AutomaticDiagnosticChoice.Value = AutomaticDiagnosticKey;
                _config.Commit();
            }
            catch
            {
                AutomaticDiagnosticConsent.Value = "";
                AutomaticDiagnosticChoice.Value = previousChoice;
                throw;
            }
        }

        public void SetDiagnosticConsent(bool allowed)
        {
            var previousChoice = DiagnosticChoiceMade.Value;
            try
            {
                DiagnosticConsent.Value = allowed ? DiagnosticUploadClient.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint)) : "";
                DiagnosticChoiceMade.Value = true;
                _config.Commit();
            }
            catch
            {
                DiagnosticConsent.Value = "";
                DiagnosticChoiceMade.Value = previousChoice;
                throw;
            }
        }
    }
}
