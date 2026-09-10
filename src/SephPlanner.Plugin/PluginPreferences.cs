#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        // 새 버전 전용 설정은 적용하지 않지만 버전을 오갈 때 잃지 않도록 보존한다.
        [JsonExtensionData] private IDictionary<string, JToken> OtherVersionSettings { get; set; }

        public List<string> PriorityCategories { get; set; } = new List<string>();
        public List<string> SuppressedPresetCategories { get; set; } = new List<string>();

        /// <summary>
        /// 강화 우선 지정. 엔티티 번호 → 단계(<see cref="PlanPreferences.MinPinLevel"/>~
        /// <see cref="PlanPreferences.MaxPinLevel"/>, 0 은 없음). 양수가 올리는 쪽, 음수가 양보다.
        /// </summary>
        public Dictionary<int, int> PinnedLevels { get; set; } = new Dictionary<int, int>();

        /// <summary>
        /// 0.1.2 까지의 형식(단계 없이 목록뿐). 읽어서 1단계로 옮기고 비운다. 지우면 예전 파일을
        /// 가진 사람의 지정이 조용히 사라진다.
        /// </summary>
        public List<int> PinnedCharms { get; set; } = new List<int>();

        /// <summary>제한 해제 칸 고정. 엔티티 번호 목록이다(<see cref="PlanPreferences.HeldCharms"/>).</summary>
        public List<int> HeldCharms { get; set; } = new List<int>();
        public List<int> RetainedCharms { get; set; } = new List<int>();
        public List<int> DeactivationAllowed { get; set; } = new List<int>();

        /// <summary>
        /// 가져온 빌드 프리셋 코드 원문. 해석 결과가 아니라 원문을 남긴다 - 카탈로그나 게임이
        /// 바뀌어도 다시 읽으면 되고, 어떤 코드를 넣었는지 그대로 남는다.
        /// </summary>
        public string PresetCode { get; set; }

        [JsonIgnore] private string _decodedFrom;
        [JsonIgnore] private BuildPreset _decoded;
        [JsonIgnore] private Action<string> _log;
        [JsonIgnore] private bool _saveFailureReported;
        [JsonIgnore] public string StorageMessage { get; private set; } = "";
        [JsonIgnore] private bool _preserveUnreadableFile;
        [JsonIgnore] private string _path = DefaultPath;

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

        public bool IsPinned(int entityId) => PinLevel(entityId) != 0;

        public bool IsRetained(int entityId) => entityId != 0 && RetainedCharms.Contains(entityId);

        public bool IsDeactivationAllowed(int entityId) => entityId != 0 && DeactivationAllowed.Contains(entityId);

        public void ToggleDeactivation(int entityId)
        {
            if (entityId == 0) return;
            if (!DeactivationAllowed.Remove(entityId)) DeactivationAllowed.Add(entityId);
            Changed();
        }

        public void ToggleRetain(int entityId)
        {
            if (entityId == 0) return;
            if (!RetainedCharms.Remove(entityId)) RetainedCharms.Add(entityId);
            Changed();
        }

        public bool IsHeld(int entityId) => entityId != 0 && HeldCharms.Contains(entityId);

        public void ToggleHold(int entityId)
        {
            if (entityId == 0) return;

            if (!HeldCharms.Remove(entityId)) HeldCharms.Add(entityId);
            Changed();
        }

        public bool IsPriority(string categoryId) => EffectivePriorityCategories().Contains(categoryId);

        public HashSet<string> EffectivePriorityCategories()
        {
            var result = new HashSet<string>(PriorityCategories);
            var preset = Preset();
            if (preset != null)
                foreach (var pair in preset.CategoryBias)
                    if (pair.Value > 0 && !SuppressedPresetCategories.Contains(pair.Key)) result.Add(pair.Key);
            return result;
        }

        /// <summary>
        /// 단계를 하나 올리거나(양수) 내린다(음수). 0 을 지나거나 끝을 넘으면 지정 없음이라,
        /// 어느 쪽 단추 하나로도 켜고 끌 수 있고 반대쪽 끝까지 돌아가지 않는다.
        /// </summary>
        public void StepPin(int entityId, int direction)
        {
            if (entityId == 0 || direction == 0) return;

            var next = PinLevel(entityId) + Math.Sign(direction);
            if (next == 0 || next > PlanPreferences.MaxPinLevel || next < PlanPreferences.MinPinLevel)
                PinnedLevels.Remove(entityId);
            else
                PinnedLevels[entityId] = next;
            Changed();
        }

        public void TogglePriority(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) return;

            if (IsPriority(categoryId))
            {
                PriorityCategories.RemoveAll(id => id == categoryId);
                var preset = Preset();
                if (preset != null && preset.CategoryBias.TryGetValue(categoryId, out var bias) && bias > 0 &&
                    !SuppressedPresetCategories.Contains(categoryId)) SuppressedPresetCategories.Add(categoryId);
            }
            else
            {
                SuppressedPresetCategories.RemoveAll(id => id == categoryId);
                PriorityCategories.Add(categoryId);
            }
            Changed();
        }

        /// <summary>
        /// 프리셋 코드를 받아들인다. 실패하면 무엇이 잘못인지 돌려주고 지금 것을 그대로 둔다.
        ///
        /// 프리셋의 콤보는 수동 지정과 분리하여 교체·삭제할 때 이전 빌드가 남지 않게 한다.
        /// </summary>
        public bool TryImport(string code, out string message)
        {
            if (!Core.Planning.PresetCode.TryParse(code, out var preset, out message)) return false;

            PresetCode = code.Trim();
            SuppressedPresetCategories.Clear();

            message = Summary(preset);
            Changed();
            return true;
        }

        public void ClearPreset()
        {
            PresetCode = null;
            SuppressedPresetCategories.Clear();
            Changed();
        }

        public void ResetBuild()
        {
            PresetCode = null;
            _decodedFrom = null;
            _decoded = null;
            PriorityCategories.Clear();
            SuppressedPresetCategories.Clear();
            PinnedLevels.Clear();
            PinnedCharms.Clear();
            HeldCharms.Clear();
            RetainedCharms.Clear();
            DeactivationAllowed.Clear();
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
            if (avoided > 0) text += " · 회피 콤보 " + avoided + "개는 추천에 미반영";
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
            PriorityCategories = EffectivePriorityCategories(),
            PinnedCharms = new Dictionary<int, int>(PinnedLevels),
            HeldCharms = new HashSet<int>(HeldCharms),
            RetainedCharms = new HashSet<int>(RetainedCharms),
            DeactivationAllowed = new HashSet<int>(DeactivationAllowed),
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

        /// <summary>손으로 고친 파일이나 낡은 버전이 남긴 뜻 없는 단계는 버린다. 0 은 지정 없음과 같다.</summary>
        private void DropInvalidPins()
        {
            var invalid = new List<int>();
            foreach (var pair in PinnedLevels)
            {
                if (pair.Key == 0 || pair.Value == 0 ||
                    pair.Value > PlanPreferences.MaxPinLevel || pair.Value < PlanPreferences.MinPinLevel)
                    invalid.Add(pair.Key);
            }
            foreach (var entityId in invalid) PinnedLevels.Remove(entityId);
        }

        private static string DefaultPath => Path.Combine(PlannerData.DataDirectory, FileName);

        public static PluginPreferences Load(Action<string> log, string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<PluginPreferences>(File.ReadAllText(path));
                    if (loaded == null) throw new JsonSerializationException("설정 내용이 비어 있습니다.");
                    if (loaded != null)
                    {
                        loaded.PriorityCategories = loaded.PriorityCategories ?? new List<string>();
                        loaded.SuppressedPresetCategories = loaded.SuppressedPresetCategories ?? new List<string>();
                        loaded.PinnedCharms = loaded.PinnedCharms ?? new List<int>();
                        loaded.PinnedLevels = loaded.PinnedLevels ?? new Dictionary<int, int>();
                        loaded.HeldCharms = loaded.HeldCharms ?? new List<int>();
                        loaded.RetainedCharms = loaded.RetainedCharms ?? new List<int>();
                        loaded.DeactivationAllowed = loaded.DeactivationAllowed ?? new List<int>();
                        loaded.MigrateLegacyPins();
                        loaded.DropInvalidPins();
                        loaded._log = log;
                        loaded._path = path;
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                log("빌드 설정을 읽지 못해 처음부터 시작합니다: " + ex.Message);
                return new PluginPreferences
                {
                    _log = log,
                    _path = path,
                    _preserveUnreadableFile = true,
                    StorageMessage = "빌드 설정을 읽지 못했습니다. 기본값으로 시작하며 원본은 다음 저장 전에 백업합니다.",
                };
            }
            return new PluginPreferences { _log = log, _path = path };
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
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                if (_preserveUnreadableFile && File.Exists(_path))
                {
                    File.Copy(_path, _path + ".damaged-" + Guid.NewGuid().ToString("N") + ".bak");
                    _preserveUnreadableFile = false;
                }

                // 바로 덮어쓰면 쓰는 도중 게임이 죽었을 때 잘린 JSON 이 남아 지정이 통째로 날아간다.
                // Delete 뒤 Move 도 그 사이에 죽으면 파일이 없어지므로, 카탈로그와 같은 원자적
                // 교체를 쓴다.
                CatalogBundleStore.WriteAtomic(_path, JsonConvert.SerializeObject(this));
                StorageMessage = "";
                _saveFailureReported = false;
            }
            catch (Exception ex)
            {
                StorageMessage = "빌드 설정 저장 실패 · 이번 판에만 유지됩니다. 다음 설정 변경 때 저장을 다시 시도합니다.";
                if (_saveFailureReported) return;

                _saveFailureReported = true;
                _log?.Invoke("빌드 설정을 저장하지 못했습니다. 이번 판에서는 유지되지만 다음 실행에는 " +
                             "남지 않습니다: " + ex.Message);
            }
        }
    }
}
