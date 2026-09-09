using SephPlanner.Core.Model;

namespace SephPlanner.DataTool.Prediction;

// 실행 순서가 제공된 경우만 재현한다. 최종 배치로 호출 순서나 초기 카테고리를 만들어내지 않는다.
public sealed class CategoryReplay
{
    public sealed class Item
    {
        public required int Id { get; init; }
        public required GridPos Position { get; set; }
        public required CharmDefinition Definition { get; init; }
        public required List<string> Categories { get; init; }
        public bool Attackable { get; init; }
    }

    private readonly Dictionary<int, Item> _items;
    private readonly Dictionary<GridPos, Item> _cells;

    public CategoryReplay(IEnumerable<Item> items)
    {
        var copies = items.Select(item => new Item
        {
            Id = item.Id,
            Position = item.Position,
            Definition = item.Definition,
            Categories = new List<string>(item.Categories),
            Attackable = item.Attackable,
        }).ToList();
        _items = copies.ToDictionary(item => item.Id);
        _cells = copies.ToDictionary(item => item.Position);
    }

    public IReadOnlyList<string> Categories(int id) => _items[id].Categories.AsReadOnly();

    public void Swap(GridPos from, GridPos to)
    {
        _cells.TryGetValue(from, out var left);
        _cells.TryGetValue(to, out var right);
        _cells.Remove(from);
        _cells.Remove(to);
        if (left is not null) { left.Position = to; _cells[to] = left; }
        if (right is not null) { right.Position = from; _cells[from] = right; }
    }

    public void Refresh(IEnumerable<int> actualOrder)
    {
        foreach (var id in actualOrder) Refresh(id);
    }

    public void Refresh(int id)
    {
        var item = _items[id];
        var definition = item.Definition;
        if (definition.Behavior == "Charm_WhitePaper")
        {
            item.Categories.Clear();
            var matches = new Dictionary<string, int>();
            foreach (var dx in new[] { 1, -1 })
                if (_cells.TryGetValue(item.Position.Offset(dx, 0), out var neighbor))
                    foreach (var category in neighbor.Categories)
                        matches[category] = matches.GetValueOrDefault(category) + 1;
            item.Categories.AddRange(matches.Where(pair => pair.Value >= definition.PaperMatch).Select(pair => pair.Key));
        }
        else if (definition.DependencyBonusByLevel.Count > 0)
        {
            item.Categories.Clear();
            var current = item;
            var seen = new HashSet<int> { id };
            while (current.Definition.DependencyBonusByLevel.Count > 0)
            {
                var next = current.Position.Offset(current.Definition.DependencyOffsetX, current.Definition.DependencyOffsetY);
                if (!_cells.TryGetValue(next, out current!) || !seen.Add(current.Id)) return;
            }
            if (current.Attackable) item.Categories.AddRange(current.Categories.Distinct());
        }
        else if (definition.LineCategories.Count > 0)
        {
            if (item.Position.Y < 0) throw new InvalidOperationException("음수 행은 재현 대상이 아닙니다.");
            item.Categories.Clear();
            item.Categories.Add(definition.LineCategories[item.Position.Y % definition.LineCategories.Count]);
        }
    }
}
