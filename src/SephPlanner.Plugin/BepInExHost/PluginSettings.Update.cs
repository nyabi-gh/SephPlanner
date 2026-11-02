using System.Collections.Generic;
using SephPlanner.Plugin.Host;
using SephPlanner.Plugin.Ui;
using static SephPlanner.Core.Text.Localization;

namespace SephPlanner.Plugin
{
    /// <summary>업데이트 확인. BepInEx 판에만 있다.</summary>
    internal sealed partial class PluginSettings
    {
        public Setting<bool> UpdateCheck { get; private set; }

        partial void BindUpdates(ISettingStore config)
        {
            // 진단 전송과 달리 동의 창이 없다. GitHub 에 가는 것은 "최신 판이 무엇이냐" 는 물음
            // 하나뿐이고 받는 것은 창에서 따로 묻는다. 끄는 자리는 F3 에 있다.
            UpdateCheck = config.Bind("Updates", "CheckOnStart", true,
                "게임을 켤 때 GitHub Releases 에서 새 안정판이 있는지 확인합니다. 있으면 창으로 물어본 뒤에만 받습니다.");
        }

        partial void AddUpdateRows(List<OptionRow> rows) => rows.Add(Switch(Tr("시작할 때 업데이트 확인"), UpdateCheck));
    }
}
