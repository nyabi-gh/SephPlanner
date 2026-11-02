using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SephPlanner.Plugin.Host
{
    /// <summary>
    /// 키 조합 하나. 주 키를 누르는 순간 조합키가 모두 눌려 있고, 조합에 없는 키는 마우스 버튼 말고 하나도 눌려 있지
    /// 않아야 발동한다. 0.5.x 까지 쓰던 BepInEx <c>KeyboardShortcut</c> 과 같은 규칙이다 - 걸으면서(WASD) F8 을 눌러도
    /// 안 되던 것까지 그대로 두어 손에 익은 동작을 바꾸지 않는다.
    ///
    /// 글자 모양("F9", "P + LeftControl + LeftAlt", 빈 값은 해제)도 BepInEx 설정 파일과 같아서 그 값을 그대로 읽는다.
    /// </summary>
    internal readonly struct Shortcut : IEquatable<Shortcut>
    {
        public static Shortcut Empty => default;
        private static readonly char[] Separators = { ' ', '+', ',', ';', '|' };
        private readonly KeyCode[] _modifiers;

        public Shortcut(KeyCode mainKey, params KeyCode[] modifiers)
        {
            MainKey = mainKey;
            _modifiers = mainKey == KeyCode.None
                ? Array.Empty<KeyCode>()
                : modifiers.Where(key => key != KeyCode.None && key != mainKey).Distinct().OrderBy(key => (int)key).ToArray();
        }

        public KeyCode MainKey { get; }

        public IReadOnlyList<KeyCode> Modifiers => _modifiers ?? Array.Empty<KeyCode>();

        public bool IsDown()
        {
            if (MainKey == KeyCode.None || !KeyInput.Pressed(MainKey)) return false;
            var modifiers = Modifiers;
            foreach (var modifier in modifiers)
            {
                if (!KeyInput.Held(modifier)) return false;
            }
            foreach (var key in KeyInput.Keys)
            {
                if (key != MainKey && KeyInput.Held(key) && !modifiers.Contains(key)) return false;
            }
            return true;
        }

        public string Serialize() =>
            MainKey == KeyCode.None ? "" : string.Join(" + ", new[] { MainKey }.Concat(Modifiers).Select(key => key.ToString()));

        /// <summary>빈 글자와 <c>None</c> 은 해제다. 모르는 키 이름이 하나라도 있으면 거짓.</summary>
        public static bool TryParse(string text, out Shortcut shortcut)
        {
            shortcut = Empty;
            var keys = new List<KeyCode>();
            foreach (var name in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse(name, false, out KeyCode key)) return false;
                keys.Add(key);
            }
            if (keys.Count > 0 && keys[0] != KeyCode.None) shortcut = new Shortcut(keys[0], keys.Skip(1).ToArray());
            return true;
        }

        public bool Equals(Shortcut other) => MainKey == other.MainKey && Modifiers.SequenceEqual(other.Modifiers);

        public override bool Equals(object obj) => obj is Shortcut other && Equals(other);

        public override int GetHashCode()
        {
            var hash = (int)MainKey;
            foreach (var modifier in Modifiers) hash = hash * 397 ^ (int)modifier;
            return hash;
        }

        public static bool operator ==(Shortcut a, Shortcut b) => a.Equals(b);

        public static bool operator !=(Shortcut a, Shortcut b) => !a.Equals(b);

        public override string ToString() => MainKey == KeyCode.None ? "Not set" : Serialize();
    }


    /// <summary>
    /// 키보드를 새 입력 시스템으로 읽는다. 이 게임은 플레이어 설정에서 구식 <c>Input</c> 을 꺼 두어 부르면
    /// <c>InvalidOperationException</c> 이 난다(1.1.1 실기, 2026-11-02). BepInEx 도 그래서 새 입력 시스템 쪽을 골랐다.
    ///
    /// 단축키는 설정 파일 형식을 지키려고 <see cref="KeyCode"/> 로 들고, 읽을 때만 <see cref="Key"/> 로 옮긴다. 이름이 같은
    /// 것은 이름으로 잇고 다른 것만 따로 적는다. 대응이 없는 것(마우스·조이스틱 버튼)은 눌린 적이 없는 키로 본다.
    /// </summary>
    internal static class KeyInput
    {
        private static readonly Dictionary<KeyCode, Key> Map = BuildMap();

        public static readonly KeyCode[] Keys = Map.Keys.ToArray();

        public static bool Held(KeyCode code) => Control(code)?.isPressed == true;

        public static bool Pressed(KeyCode code) => Control(code)?.wasPressedThisFrame == true;

        private static KeyControl Control(KeyCode code)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && Map.TryGetValue(code, out var key) ? keyboard[key] : null;
        }

        private static Dictionary<KeyCode, Key> BuildMap()
        {
            var map = new Dictionary<KeyCode, Key>();
            foreach (var code in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
            {
                if (Enum.TryParse(code.ToString(), false, out Key key) && key != Key.None) map[code] = key;
            }
            for (var digit = 0; digit <= 9; digit++)
            {
                map[KeyCode.Alpha0 + digit] = Enum.Parse<Key>("Digit" + digit);
                map[KeyCode.Keypad0 + digit] = Enum.Parse<Key>("Numpad" + digit);
            }
            map[KeyCode.Return] = Key.Enter;
            map[KeyCode.BackQuote] = Key.Backquote;
            map[KeyCode.LeftControl] = Key.LeftCtrl;
            map[KeyCode.RightControl] = Key.RightCtrl;
            map[KeyCode.LeftCommand] = Key.LeftMeta;
            map[KeyCode.LeftWindows] = Key.LeftMeta;
            map[KeyCode.RightCommand] = Key.RightMeta;
            map[KeyCode.RightWindows] = Key.RightMeta;
            map[KeyCode.AltGr] = Key.RightAlt;
            map[KeyCode.Menu] = Key.ContextMenu;
            map[KeyCode.Numlock] = Key.NumLock;
            map[KeyCode.Print] = Key.PrintScreen;
            map[KeyCode.KeypadEnter] = Key.NumpadEnter;
            map[KeyCode.KeypadDivide] = Key.NumpadDivide;
            map[KeyCode.KeypadMultiply] = Key.NumpadMultiply;
            map[KeyCode.KeypadPlus] = Key.NumpadPlus;
            map[KeyCode.KeypadMinus] = Key.NumpadMinus;
            map[KeyCode.KeypadPeriod] = Key.NumpadPeriod;
            map[KeyCode.KeypadEquals] = Key.NumpadEquals;
            return map;
        }
    }
}
