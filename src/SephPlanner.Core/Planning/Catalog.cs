using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 엔티티 번호로 석판과 아티팩트 정의를 찾는다. 오버레이는 플러그인이 덤프한 파일을 읽어
    /// 채우고, 테스트와 진단 도구는 손으로 만든 목록을 넣는다.
    /// </summary>
    public interface ICatalog
    {
        TabletDefinition? Tablet(int entityId);
        CharmDefinition? Charm(int entityId);

        /// <summary>카테고리 식별자로 콤보 정의를 찾는다. 콤보가 없는 카테고리는 null.</summary>
        ComboDefinition? Combo(string categoryId);
    }

    /// <summary>이미 메모리에 있는 정의 목록으로 만든 카탈로그.</summary>
    public sealed class Catalog : ICatalog
    {
        private readonly Dictionary<int, TabletDefinition> _tablets;
        private readonly Dictionary<int, CharmDefinition> _charms;
        private readonly Dictionary<string, ComboDefinition> _combos;

        public Catalog(
            IEnumerable<TabletDefinition> tablets,
            IEnumerable<CharmDefinition> charms,
            IEnumerable<ComboDefinition>? combos = null)
        {
            _tablets = ToMap(tablets, definition => definition.EntityId);
            _charms = ToMap(charms, definition => definition.EntityId);
            _combos = new Dictionary<string, ComboDefinition>();
            foreach (var combo in combos ?? Array.Empty<ComboDefinition>())
            {
                if (!_combos.ContainsKey(combo.Id)) _combos[combo.Id] = combo;
            }
        }

        public TabletDefinition? Tablet(int entityId) =>
            _tablets.TryGetValue(entityId, out var definition) ? definition : null;

        public CharmDefinition? Charm(int entityId) =>
            _charms.TryGetValue(entityId, out var definition) ? definition : null;

        public ComboDefinition? Combo(string categoryId) =>
            _combos.TryGetValue(categoryId, out var definition) ? definition : null;

        // 같은 번호가 두 번 나오면 먼저 나온 것을 쓴다. 덤프에 중복이 있어도 터지지 않아야 한다.
        private static Dictionary<int, T> ToMap<T>(IEnumerable<T> items, Func<T, int> key)
        {
            var map = new Dictionary<int, T>();
            foreach (var item in items)
            {
                var id = key(item);
                if (!map.ContainsKey(id)) map[id] = item;
            }
            return map;
        }
    }
}
