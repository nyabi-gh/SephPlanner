using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 플레이어가 고른 빌드 방향. 빌드 창(<see cref="Ui.BuildWindow"/>)에서 고르고 여기에 남는다.
    ///
    /// <b>BepInEx 설정 파일이 아니라 제 파일을 쓴다.</b> 담는 것이 목록(콤보 카테고리, 아티팩트
    /// 번호)과 긴 문자열(프리셋 코드)이라 한 줄짜리 설정 항목으로는 담기지 않는다. 표시 설정은
    /// 그대로 BepInEx 쪽에 있다.
    /// </summary>
    internal sealed class PluginPreferences
    {
        private const string FileName = "plugin-settings.json";

        public List<string> PriorityCategories { get; set; } = new List<string>();

        /// <summary>
        /// 강화 우선 지정. 엔티티 번호 → 단계(1~<see cref="PlanPreferences.MaxPinLevel"/>).
        /// </summary>
        public Dictionary<int, int> PinnedLevels { get; set; } = new Dictionary<int, int>();

        /// <summary>
        /// 0.1.3 까지의 형식(단계 없이 목록뿐). 읽어서 1단계로 옮기고 비운다. 지우면 예전 파일을
        /// 가진 사람의 지정이 조용히 사라진다.
        /// </summary>
        public List<int> PinnedCharms { get; set; } = new List<int>();

        /// <summary>
        /// 가져온 빌드 프리셋 코드 원문. 해석 결과가 아니라 원문을 남긴다 - 카탈로그나 게임이
        /// 바뀌어도 다시 읽으면 되고, 어떤 코드를 넣었는지 그대로 남는다.
        /// </summary>
        public string PresetCode { get; set; }

        [JsonIgnore] private string _decodedFrom;
        [JsonIgnore] private BuildPreset _decoded;

        /// <summary>바뀔 때마다 오른다. 스냅샷이 그대로여도 다시 풀어야 하는지를 이걸로 안다.</summary>
        [JsonIgnore] public int Revision { get; private set; }

        /// <summary>해석에 실패했거나 코드가 없으면 null. 같은 코드를 두 번 풀지 않는다.</summary>
        public BuildPreset Preset()
        {
            if (string.IsNullOrWhiteSpace(PresetCode)) return null;
            if (_decodedFrom != PresetCode)
            {
                _decodedFrom = PresetCode;
                _decoded = Core.Planning.PresetCode.TryParse(PresetCode, out var preset, out _) ? preset : null;
            }
            return _decoded;
        }

        /// <summary>지정 단계. 지정하지 않았으면 0.</summary>
        public int PinLevel(int entityId) =>
            entityId != 0 && PinnedLevels.TryGetValue(entityId, out var level) ? level : 0;

        public bool IsPinned(int entityId) => PinLevel(entityId) > 0;

        public bool IsPriority(string categoryId) => PriorityCategories.Contains(categoryId);

        /// <summary>
        /// 다음 단계로 돌린다. 마지막 다음은 지정 없음이라 한 손가락으로 켜고 끌 수 있다.
        /// </summary>
        public void CyclePin(int entityId)
        {
            if (entityId == 0) return;

            var next = PinLevel(entityId) + 1;
            if (next > PlanPreferences.MaxPinLevel) PinnedLevels.Remove(entityId);
            else PinnedLevels[entityId] = next;
            Changed();
        }

        public void TogglePriority(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return;

            if (!PriorityCategories.Remove(categoryId)) PriorityCategories.Add(categoryId);
            Changed();
        }

        /// <summary>
        /// 프리셋 코드를 받아들인다. 실패하면 무엇이 잘못인지 돌려주고 지금 것을 그대로 둔다.
        ///
        /// 프리셋이 노리는 콤보는 칩을 대신 눌러 주는 것으로 잇는다. 그래야 가져온 뒤에도 손으로
        /// 켠 것과 똑같이 끄고 켤 수 있다. 음수(피하는 카테고리)는 켜지 않는다 - 점수를 깎는
        /// 방향이 맞는지 아직 확인되지 않았다(docs/ROADMAP.md).
        /// </summary>
        public bool TryImport(string code, out string message)
        {
            if (!Core.Planning.PresetCode.TryParse(code, out var preset, out message)) return false;

            PresetCode = code.Trim();
            foreach (var pair in preset.CategoryBias)
            {
                if (pair.Value <= 0) continue;
                if (!PriorityCategories.Contains(pair.Key)) PriorityCategories.Add(pair.Key);
            }

            message = Summary(preset);
            Changed();
            return true;
        }

        public void ClearPreset()
        {
            PresetCode = null;
            Changed();
        }

        /// <summary>가져온 빌드가 무엇을 알려 주는지 한 줄로.</summary>
        public static string Summary(BuildPreset preset)
        {
            var text = "가져온 빌드 · 아티팩트 " + preset.FavoriteCharms.Count + "개";

            var avoided = 0;
            foreach (var pair in preset.CategoryBias)
            {
                if (pair.Value < 0) avoided++;
            }
            if (avoided > 0) text += " · 피하는 콤보 " + avoided + "개";
            return text;
        }

        /// <summary>
        /// 지금 고른 것으로 계산 설정을 만든다. 후보 추천을 끄면 프리셋이 알려 주는 것도 함께
        /// 쉰다 - 그쪽은 "무엇을 집을지"에 대한 조언이기 때문이다. 강화 우선은 "어디에 놓을지"라
        /// 남는다.
        /// </summary>
        public PlanPreferences ToPreferences(bool recommendations) => new PlanPreferences
        {
            Recommendations = recommendations,
            PriorityCategories = new HashSet<string>(PriorityCategories),
            PinnedCharms = new Dictionary<int, int>(PinnedLevels),
            CharmValues = CharmValueSource.Book,
            PresetCharms = recommendations && Preset() is BuildPreset preset
                ? new HashSet<int>(preset.FavoriteCharms)
                : new HashSet<int>(),
        };

        /// <summary>예전 형식의 지정을 1단계로 옮긴다. 옮긴 뒤에는 새 형식만 남는다.</summary>
        private void MigrateLegacyPins()
        {
            if (PinnedCharms.Count == 0) return;

            foreach (var entityId in PinnedCharms)
            {
                if (entityId != 0 && !PinnedLevels.ContainsKey(entityId)) PinnedLevels[entityId] = 1;
            }
            PinnedCharms.Clear();
        }

        private static string Path_ => Path.Combine(PlannerData.DataDirectory, FileName);

        public static PluginPreferences Load(Action<string> log)
        {
            try
            {
                if (File.Exists(Path_))
                {
                    var loaded = JsonConvert.DeserializeObject<PluginPreferences>(File.ReadAllText(Path_));
                    if (loaded != null)
                    {
                        loaded.PriorityCategories = loaded.PriorityCategories ?? new List<string>();
                        loaded.PinnedCharms = loaded.PinnedCharms ?? new List<int>();
                        loaded.PinnedLevels = loaded.PinnedLevels ?? new Dictionary<int, int>();
                        loaded.MigrateLegacyPins();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                // 빌드 지정이 깨졌다고 플러그인이 안 뜨면 안 된다. 기본값으로 시작하되 조용히
                // 넘어가지는 않는다 - 다음 저장에서 덮어써지므로 여기가 알릴 마지막 기회다.
                log("빌드 설정을 읽지 못해 처음부터 시작합니다: " + ex.Message);
            }
            return new PluginPreferences();
        }

        private void Changed()
        {
            Revision++;
            Save();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(PlannerData.DataDirectory);

                // 바로 덮어쓰면 쓰는 도중 게임이 죽었을 때 잘린 JSON 이 남아 지정이 통째로 날아간다.
                // Delete 뒤 Move 도 그 사이에 죽으면 파일이 없어지므로, 카탈로그와 같은 원자적
                // 교체를 쓴다.
                CatalogBundleStore.WriteAtomic(Path_, JsonConvert.SerializeObject(this));
            }
            catch (Exception)
            {
                // 저장에 실패해도 이번 판에서는 지정이 살아 있다. 다음 조작 때 다시 시도된다.
            }
        }
    }
}
