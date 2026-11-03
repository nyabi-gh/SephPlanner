using System;
using System.Collections.Generic;
using System.Globalization;
using SephPlanner.Plugin.Host;

namespace SephPlanner.Plugin.Workshop
{
    /// <summary>
    /// 게임의 모드 옵션. 게임의 <c>DeviceOptions.xml</c> 에 <c>ModOptions:nyabi.sephplanner:섹션.열쇠</c> 로 들어가고, 게임의
    /// <c>SaveData</c> 가 값의 형식을 함께 적어 bool·float·string 이 그대로 돌아온다(1.1.1 디컴파일). 열거형은 정수로,
    /// 단축키는 BepInEx 와 같은 글자로 둔다.
    ///
    /// 아직 적힌 값이 없는 설정은 BepInEx 판의 설정 파일 값을 가져온다. 가져온 값은 <see cref="Save"/> 에서 한 번에 적는다.
    /// </summary>
    internal sealed class ModOptionsSettingStore : ISettingStore
    {
        private readonly ModOptions _options;
        private readonly IReadOnlyDictionary<string, string> _legacy;
        private bool _unavailableReported;

        public ModOptionsSettingStore(ModOptions options, IReadOnlyDictionary<string, string> legacy)
        {
            _options = options;
            _legacy = legacy;
        }

        public Setting<T> Bind<T>(string section, string key, T defaultValue, string description) =>
            Bind(section, key, defaultValue, (Func<T, T>)null);

        public Setting<float> Bind(string section, string key, float defaultValue, string description, float min, float max) =>
            Bind(section, key, defaultValue, value => SettingRange.Clamp(value, min, max));

        public void Save()
        {
            if (!Available()) return;
            if (!_options.Save()) Log.Warning("게임 옵션 파일에 SephPlanner 설정을 저장하지 못했습니다.");
        }

        public void Commit()
        {
            if (!_options.IsAvailable)
                throw new InvalidOperationException("게임 옵션 저장소가 아직 준비되지 않아 SephPlanner 설정을 저장하지 못했습니다.");
            if (!_options.Save())
                throw new InvalidOperationException("게임 옵션 파일에 SephPlanner 설정을 저장하지 못했습니다.");
        }

        private Setting<T> Bind<T>(string section, string key, T defaultValue, Func<T, T> normalize)
        {
            var storageKey = section + "." + key;
            var imported = false;
            var stored = defaultValue;
            if (_options.HasKey(storageKey)) stored = Get(storageKey, defaultValue);
            else if (_legacy.TryGetValue(storageKey, out var text) && TryParse(text, out stored)) imported = true;
            else stored = defaultValue;

            var setting = new Setting<T>(key, defaultValue, stored, normalize, value =>
            {
                if (!Set(storageKey, value)) return;
                if (!_options.Save()) Log.Warning("게임 옵션 파일에 SephPlanner 설정을 저장하지 못했습니다: " + storageKey);
            });
            if (imported) Set(storageKey, setting.Value);
            return setting;
        }

        private T Get<T>(string key, T fallback)
        {
            object value;
            if (typeof(T) == typeof(bool)) value = _options.GetBool(key, (bool)(object)fallback);
            else if (typeof(T) == typeof(float)) value = _options.GetFloat(key, (float)(object)fallback);
            else if (typeof(T) == typeof(string)) value = _options.GetString(key, (string)(object)fallback);
            else if (typeof(T) == typeof(Shortcut))
                value = Shortcut.TryParse(_options.GetString(key, ((Shortcut)(object)fallback).Serialize()), out var shortcut) ? shortcut : fallback;
            else if (typeof(T).IsEnum)
            {
                var number = _options.GetInt(key, Convert.ToInt32(fallback, CultureInfo.InvariantCulture));
                value = Enum.IsDefined(typeof(T), number) ? Enum.ToObject(typeof(T), number) : fallback;
            }
            else throw Unsupported<T>();
            return (T)value;
        }

        private static bool TryParse<T>(string text, out T value)
        {
            object parsed = null;
            var ok = false;
            if (typeof(T) == typeof(bool)) { ok = bool.TryParse(text, out var flag); parsed = flag; }
            else if (typeof(T) == typeof(float))
            {
                ok = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
                parsed = number;
            }
            else if (typeof(T) == typeof(string)) { ok = true; parsed = text; }
            else if (typeof(T) == typeof(Shortcut)) { ok = Shortcut.TryParse(text, out var shortcut); parsed = shortcut; }
            else if (typeof(T).IsEnum) ok = Enum.TryParse(typeof(T), text, out parsed) && Enum.IsDefined(typeof(T), parsed);
            else throw Unsupported<T>();
            value = ok ? (T)parsed : default;
            return ok;
        }

        /// <summary>메모리에 적는다. 저장소가 아직 없으면 거짓 - 값은 이번 실행에만 남는다.</summary>
        private bool Set<T>(string key, T value)
        {
            if (!Available()) return false;
            switch (value)
            {
                case bool flag: _options.SetBool(key, flag); break;
                case float number: _options.SetFloat(key, number); break;
                case string text: _options.SetString(key, text); break;
                case Shortcut shortcut: _options.SetString(key, shortcut.Serialize()); break;
                case Enum choice: _options.SetInt(key, Convert.ToInt32(choice, CultureInfo.InvariantCulture)); break;
                default: throw Unsupported<T>();
            }
            return true;
        }

        private bool Available()
        {
            if (_options.IsAvailable) return true;
            if (!_unavailableReported)
            {
                _unavailableReported = true;
                Log.Warning("게임 옵션 저장소가 아직 준비되지 않아 SephPlanner 설정을 저장하지 못했습니다. 바꾼 값은 이번 실행에만 남습니다.");
            }
            return false;
        }

        private static NotSupportedException Unsupported<T>() =>
            new NotSupportedException("모드 옵션에 담을 수 없는 설정 형식입니다: " + typeof(T).Name);
    }
}
