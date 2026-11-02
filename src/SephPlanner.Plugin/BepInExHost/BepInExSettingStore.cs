using System;
using System.Linq;
using BepInEx.Configuration;
using SephPlanner.Plugin.Host;

namespace SephPlanner.Plugin.BepInExHost
{
    /// <summary>
    /// BepInEx 설정 파일(<c>BepInEx/config/dev.nyabi.sephplanner.bridge.cfg</c>). 단축키는 BepInEx 의 형식으로 묶어
    /// 파일의 모양과 설명이 0.5.x 와 같게 남는다.
    /// </summary>
    internal sealed class BepInExSettingStore : ISettingStore
    {
        private readonly ConfigFile _config;

        public BepInExSettingStore(ConfigFile config) => _config = config;

        public Setting<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            if (typeof(T) == typeof(Shortcut))
                return (Setting<T>)(object)BindShortcut(section, key, (Shortcut)(object)defaultValue, description);
            var entry = _config.Bind(section, key, defaultValue, description);
            return Wrap(entry, defaultValue, null);
        }

        public Setting<float> Bind(string section, string key, float defaultValue, string description, float min, float max)
        {
            var entry = _config.Bind(section, key, defaultValue,
                new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
            return Wrap(entry, defaultValue, value => SettingRange.Clamp(value, min, max));
        }

        public void Save() => _config.Save();

        private static Setting<T> Wrap<T>(ConfigEntry<T> entry, T defaultValue, Func<T, T> normalize)
        {
            var setting = new Setting<T>(entry.Definition.Key, defaultValue, entry.Value, normalize, value => entry.Value = value);
            entry.SettingChanged += (_, _) => setting.Reloaded(entry.Value);
            return setting;
        }

        private Setting<Shortcut> BindShortcut(string section, string key, Shortcut defaultValue, string description)
        {
            var entry = _config.Bind(section, key, ToBepInEx(defaultValue), description);
            var setting = new Setting<Shortcut>(key, defaultValue, FromBepInEx(entry.Value), null, value => entry.Value = ToBepInEx(value));
            entry.SettingChanged += (_, _) => setting.Reloaded(FromBepInEx(entry.Value));
            return setting;
        }

        private static KeyboardShortcut ToBepInEx(Shortcut shortcut) =>
            shortcut.MainKey == UnityEngine.KeyCode.None
                ? KeyboardShortcut.Empty
                : new KeyboardShortcut(shortcut.MainKey, shortcut.Modifiers.ToArray());

        private static Shortcut FromBepInEx(KeyboardShortcut shortcut) =>
            new Shortcut(shortcut.MainKey, shortcut.Modifiers.ToArray());
    }
}
