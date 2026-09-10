using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Combat
{
    public sealed class CombatStatSource
    {
        public string Id { get; set; } = "";
        public Dictionary<string, int> Stats { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Amplification { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public CombatStatSource Copy() => new CombatStatSource
        {
            Id = Id,
            Stats = new Dictionary<string, int>(Stats, StringComparer.Ordinal),
            Amplification = new Dictionary<string, int>(Amplification, StringComparer.Ordinal),
        };
    }

    public sealed class CombatStatState
    {
        private readonly Dictionary<string, CombatStatSource> _sources = new Dictionary<string, CombatStatSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _stats = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _amplification = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Func<string, int> _amplified;
        private readonly Dictionary<string, int> _resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        public CombatStatState() => _amplified = Amplified;

        public int Read(string key)
        {
            if (_resolved.TryGetValue(key, out var value)) return value;
            value = CombatStatMath.Read(key, _amplified);
            _resolved[key] = value;
            return value;
        }
        public int Base(string key) => _stats.GetValueOrDefault(key);
        public int Amplification(string key) => _amplification.GetValueOrDefault(key);

        public void SetSource(CombatStatSource source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(source.Id))
                throw new ArgumentException("능력치 기여의 출처가 필요합니다.", nameof(source));
            var copy = source.Copy();
            _resolved.Clear();
            RemoveSource(copy.Id);
            _sources.Add(copy.Id, copy);
            Accumulate(_stats, copy.Stats, 1);
            Accumulate(_amplification, copy.Amplification, 1);
        }

        public bool RemoveSource(string id)
        {
            if (!_sources.TryGetValue(id, out var source)) return false;
            _resolved.Clear();
            _sources.Remove(id);
            Accumulate(_stats, source.Stats, -1);
            Accumulate(_amplification, source.Amplification, -1);
            return true;
        }

        public List<CombatStatSource> Export()
        {
            var result = new List<CombatStatSource>(_sources.Count);
            foreach (var source in _sources.Values) result.Add(source.Copy());
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            return result;
        }

        public CombatStatState Copy()
        {
            var result = new CombatStatState();
            foreach (var source in _sources.Values) result.SetSource(source);
            return result;
        }

        private int Amplified(string key) => CombatStatMath.Amplify(Base(key), Amplification(key));

        private static void Accumulate(Dictionary<string, int> total, Dictionary<string, int> values, int sign)
        {
            foreach (var pair in values)
            {
                var amount = unchecked(total.GetValueOrDefault(pair.Key) + unchecked(pair.Value * sign));
                if (amount == 0) total.Remove(pair.Key);
                else total[pair.Key] = amount;
            }
        }
    }
}
