namespace SephPlanner.DataTool.Prediction;

// 원본/대상은 StatusInstance 이름이 아닌 실제 customStats 키다. 일반 가산형 전환만 재현한다.
public sealed class StatReplay
{
    public sealed class Conversion
    {
        public required string Source { get; init; }
        public required int Divisor { get; init; }
        public required Dictionary<string, int> Amounts { get; init; }
        public Dictionary<string, int> Applied { get; set; } = new();
        public int LastSource { get; set; }
        public bool Enabled { get; set; } = true;
    }

    private static readonly string[] Elements = { "PHYSICAL", "FIRE", "ICE", "LIGHTNING" };
    private readonly Dictionary<string, int> _base;
    private readonly Dictionary<string, int> _amplification;
    private readonly Dictionary<int, Conversion> _conversions = new();

    public StatReplay(Dictionary<string, int> baseWithoutConversions, Dictionary<string, int>? amplification = null)
    {
        _base = new(baseWithoutConversions, StringComparer.Ordinal);
        _amplification = new(amplification ?? new(), StringComparer.Ordinal);
    }

    public void Add(int id, Conversion conversion)
    {
        if (conversion.Divisor <= 0) throw new ArgumentOutOfRangeException(nameof(conversion), "전환 단위는 양수여야 합니다.");
        _conversions.Add(id, new Conversion
        {
            Source = conversion.Source,
            Divisor = conversion.Divisor,
            Amounts = new(conversion.Amounts),
            Applied = new(conversion.Applied),
            LastSource = conversion.LastSource,
            Enabled = conversion.Enabled,
        });
    }

    public void ChangeBase(string key, int delta) => _base[key] = unchecked(_base.GetValueOrDefault(key) + delta);

    public int Read(string key)
    {
        var element = Array.FindIndex(Elements, name => name + "DAMAGE" == key);
        if (element < 0) return Raw(key);
        var value = Raw(key);
        if (value > 20 && Destination(element).Index >= 0) value = 20;
        for (var from = 0; from < Elements.Length; from++)
        {
            if (from == element) continue;
            var destination = Destination(from);
            if (destination.Index != element) continue;
            var excess = Raw(Elements[from] + "DAMAGE") - 20;
            if (excess > 0) value = unchecked(value + Truncate((float)unchecked(excess * destination.Percent) / 100f));
        }
        return value;
    }

    public IReadOnlyDictionary<string, int> Applied(int id) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(_conversions[id].Applied);

    public void Refresh(int id)
    {
        var conversion = _conversions[id];
        Remove(conversion);
        if (conversion.Enabled) Apply(conversion, Read(conversion.Source));
    }

    // Timer가 실제로 발화했고 아바타가 살아 있는 이벤트만 호출자가 전달한다.
    public void Tick(int id)
    {
        var conversion = _conversions[id];
        if (!conversion.Enabled) return;
        var source = Read(conversion.Source);
        if (source == conversion.LastSource) return;
        Remove(conversion);
        Apply(conversion, source);
    }

    private static void Remove(Conversion conversion)
    {
        conversion.Applied.Clear();
        conversion.LastSource = 0;
    }

    private static void Apply(Conversion conversion, int source)
    {
        var count = Truncate(Math.Floor((float)source / conversion.Divisor));
        conversion.LastSource = source;
        foreach (var target in conversion.Amounts)
            conversion.Applied[target.Key] = unchecked(target.Value * count);
    }

    private int Raw(string key)
    {
        var total = _base.GetValueOrDefault(key);
        foreach (var conversion in _conversions.Values)
            total = unchecked(total + conversion.Applied.GetValueOrDefault(key));
        return total == 0 ? 0 : Truncate((float)unchecked(total * unchecked(100 + _amplification.GetValueOrDefault(key))) / 100f);
    }

    private (int Index, int Percent) Destination(int from)
    {
        var best = (Index: -1, Percent: 0);
        for (var to = 0; to < Elements.Length; to++)
        {
            if (to == from) continue;
            var percent = Raw(Elements[from] + "TO" + Elements[to]);
            if (percent > best.Percent) best = (to, percent);
        }
        return best;
    }

    private static int Truncate(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            throw new OverflowException("정수 범위를 벗어나는 게임 런타임 변환은 아직 검증하지 않았습니다.");
        return (int)value;
    }
}
