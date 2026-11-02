#nullable disable
using System;
using System.Collections.Generic;

namespace SephPlanner.Plugin.Host
{
    /// <summary>
    /// 설정을 어디에 두는지. BepInEx 판은 설정 파일, 창작마당 판은 게임의 모드 옵션이다. 둘 다 값을 고친 즉시
    /// 저장한다.
    /// </summary>
    internal interface ISettingStore
    {
        /// <summary><typeparamref name="T"/> 는 bool·float·string·열거형·<see cref="Shortcut"/> 만 받는다.</summary>
        Setting<T> Bind<T>(string section, string key, T defaultValue, string description);

        /// <summary>범위 밖의 값은 읽을 때도 쓸 때도 범위 안으로 자른다.</summary>
        Setting<float> Bind(string section, string key, float defaultValue, string description, float min, float max);

        void Save();
    }

    internal static class SettingRange
    {
        /// <summary>NaN 은 아래 끝으로 간다. BepInEx 의 <c>AcceptableValueRange</c> 와 같은 결과다.</summary>
        public static float Clamp(float value, float min, float max) =>
            value.CompareTo(min) < 0 ? min : value.CompareTo(max) > 0 ? max : value;
    }

    /// <summary>설정 값 하나. 읽기는 메모리에서 하고, 쓰면 저장소에 바로 적는다.</summary>
    internal sealed class Setting<T>
    {
        private readonly Func<T, T> _normalize;
        private readonly Action<T> _store;
        private T _value;

        public Setting(string key, T defaultValue, T stored, Func<T, T> normalize, Action<T> store)
        {
            Key = key;
            DefaultValue = defaultValue;
            _normalize = normalize ?? (value => value);
            _store = store;
            _value = _normalize(stored);
        }

        public string Key { get; }

        public T DefaultValue { get; }

        public event EventHandler SettingChanged;

        public T Value
        {
            get => _value;
            set
            {
                value = _normalize(value);
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                _store(value);
                SettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>저장소 쪽에서 값이 바뀌었다(다른 모드가 설정 파일을 고침). 다시 쓰지 않고 알리기만 한다.</summary>
        public void Reloaded(T value)
        {
            value = _normalize(value);
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
