using System;
using System.Collections.Generic;

namespace SephPlanner.Plugin.Host
{
    /// <summary>
    /// 단축키가 이번 프레임에 발동하는지. 키 이름 여럿이 한 물리 키를 가리킬 수 있어(RightAlt·AltGr, LeftCommand·LeftWindows)
    /// 이름이 아니라 물리 키로 견준다. 이름으로 견주면 오른쪽 Alt 하나를 누른 것이 조합에 없는 AltGr 까지 누른 것으로 보여
    /// 그 키가 든 단축키는 발동하지 않는다.
    /// </summary>
    internal static class ShortcutMatch
    {
        /// <param name="physical">키 이름이 가리키는 물리 키. 없는 이름(마우스 버튼 따위)은 눌린 적이 없는 키다.</param>
        /// <param name="keys">견줄 물리 키 전부. 조합에 없는 것이 하나라도 눌려 있으면 발동하지 않는다.</param>
        public static bool IsDown<TName, TKey>(
            TName main, IReadOnlyList<TName> modifiers, IReadOnlyDictionary<TName, TKey> physical,
            IReadOnlyList<TKey> keys, Func<TKey, bool> pressed, Func<TKey, bool> held)
        {
            if (!physical.TryGetValue(main, out var mainKey) || !pressed(mainKey)) return false;
            foreach (var modifier in modifiers)
            {
                if (!physical.TryGetValue(modifier, out var key) || !held(key)) return false;
            }
            foreach (var key in keys)
            {
                if (!held(key) || Same(key, mainKey) || IsModifier(key, modifiers, physical)) continue;
                return false;
            }
            return true;
        }

        private static bool IsModifier<TName, TKey>(TKey key, IReadOnlyList<TName> modifiers, IReadOnlyDictionary<TName, TKey> physical)
        {
            foreach (var modifier in modifiers)
            {
                if (physical.TryGetValue(modifier, out var other) && Same(key, other)) return true;
            }
            return false;
        }

        private static bool Same<TKey>(TKey a, TKey b) => EqualityComparer<TKey>.Default.Equals(a, b);
    }
}
