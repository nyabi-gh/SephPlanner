using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 능력치 표(<c>Charm_StatusInstance.stats</c>) 없이 자기 코드로 능력치를 올려 주는 아티팩트를
    /// 게임 IL 에서 찾아, 그 레벨별 배열을 같은 측정 자료에 실어 준다.
    ///
    /// 어느 배열이 어느 능력치로 가는지는 게임 코드에만 있다. 클래스마다 손으로 적어 두면 게임이
    /// 패치될 때마다 조용히 낡으므로, 짓는 자리에서 매번 읽는다. 해석은 Core 가 한다
    /// (<see cref="CharmStatScan"/>).
    /// </summary>
    internal static class CharmStatCode
    {
        private static Dictionary<string, List<CharmStatGrant>> _grants;
        private static Dictionary<string, List<CharmStatGrant>> _aligned;
        public static int MappedTypes { get; private set; }
        public static int UnresolvedTypes { get; private set; }
        public static string LastError { get; private set; } = "";

        /// <summary>아티팩트 하나가 코드로 올려 주는 능력치 표.</summary>
        public static IEnumerable<CharmStatTable> TablesOf(GameObject prefab, int entityId)
        {
            var grants = _aligned ?? new Dictionary<string, List<CharmStatGrant>>(StringComparer.Ordinal);
            if (grants.Count == 0 || prefab == null) yield break;

            foreach (var charm in prefab.GetComponents<Charm_Basic>())
            {
                if (charm == null) continue;
                if (!grants.TryGetValue(charm.GetType().Name, out var forType)) continue;

                foreach (var grant in forType)
                {
                    var values = Read(charm, grant.Field);
                    if (values == null || values.Count == 0) continue;

                    var table = new CharmStatTable
                    {
                        EntityId = entityId,
                        StatusId = grant.Stat,
                        FromCode = true,
                    };
                    table.ValuesByLevel.AddRange(values);
                    yield return table;
                }
            }
        }

        /// <summary>
        /// 코드 쪽 이름은 <c>ECustomStat</c>(<c>AttackSpeed</c>)이고 능력치 표는
        /// <c>StatusDatabase</c> 이름(<c>ATTACK_SPEED</c>)을 쓴다. 같은 능력치를 다르게 부르므로
        /// 그대로 두면 환산율이 둘로 갈린다. 글자만 남겨 맞춰 보고, <b>맞는 것만 쓴다</b> -
        /// 비슷해 보인다고 이어 붙이면 다른 능력치의 환산율로 값매김하게 된다.
        /// </summary>
        public static void Align(IEnumerable<string> knownStatusIds)
        {
            var known = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in knownStatusIds)
            {
                var key = Simplify(id);
                if (key.Length > 0 && !known.ContainsKey(key)) known[key] = id;
            }

            // 맞추기는 되풀이해 불릴 수 있으므로 원본을 깎지 않고 따로 만든다.
            _aligned = new Dictionary<string, List<CharmStatGrant>>(StringComparer.Ordinal);
            Unmatched.Clear();
            foreach (var pair in Grants())
            {
                var kept = new List<CharmStatGrant>();
                foreach (var grant in pair.Value)
                {
                    if (known.TryGetValue(Simplify(grant.Stat), out var id))
                        kept.Add(new CharmStatGrant { TypeName = grant.TypeName, Field = grant.Field, Stat = id });
                    else Unmatched.Add(grant.Stat);
                }
                if (kept.Count > 0) _aligned[pair.Key] = kept;
            }
            MappedTypes = _aligned.Count;
        }

        /// <summary>이름이 붙는 방식만 다른 것을 견주려고 글자만 남긴다.</summary>
        private static string Simplify(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (var letter in name)
                if (char.IsLetterOrDigit(letter)) builder.Append(char.ToLowerInvariant(letter));
            return builder.ToString();
        }

        /// <summary>능력치 표 쪽에 같은 이름이 없어 쓰지 못한 것. 환산율이 없으니 값도 없다.</summary>
        public static readonly SortedSet<string> Unmatched = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>무엇을 잡았고 무엇을 놓쳤는지. 조용히 비어 있으면 알아챌 수 없다.</summary>
        public static string Summary(StatMeasurement measurement)
        {
            var charms = new HashSet<int>();
            foreach (var table in measurement.CharmStats)
                if (table.FromCode) charms.Add(table.EntityId);

            var text = $"코드에서 읽은 능력치: 아티팩트 {charms.Count}종 / 클래스 {MappedTypes}개"
                       + $", 못 정한 클래스 {UnresolvedTypes}개";
            if (Unmatched.Count > 0) text += ", 이름이 안 이어진 능력치 " + string.Join(" ", Unmatched);
            if (LastError.Length > 0) text += " - " + LastError;
            return text;
        }

        private static Dictionary<string, List<CharmStatGrant>> Grants()
        {
            if (_grants != null) return _grants;

            _grants = new Dictionary<string, List<CharmStatGrant>>(StringComparer.Ordinal);
            try
            {
                var assembly = GameAssembly.Create();
                if (assembly == null)
                {
                    LastError = "Charm_Basic 또는 ECustomStat 을 찾지 못했습니다.";
                    return _grants;
                }

                var report = CharmStatScan.Run(assembly);
                foreach (var grant in report.Grants)
                {
                    if (!_grants.TryGetValue(grant.TypeName, out var list))
                        _grants[grant.TypeName] = list = new List<CharmStatGrant>();
                    list.Add(grant);
                }
                UnresolvedTypes = report.Unresolved.Count;
            }
            catch (Exception ex)
            {
                // 읽지 못해도 카탈로그는 지어져야 한다. 잃는 것은 이 아티팩트들의 값어치뿐이다.
                LastError = ex.GetType().Name + ": " + ex.Message;
            }
            return _grants;
        }

        private static List<int> Read(Charm_Basic charm, string fieldName)
        {
            var field = charm.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return null;

            var value = field.GetValue(charm);
            if (value is int[] integers) return integers.ToList();
            if (value is float[] floats) return floats.Select(item => (int)Math.Round(item)).ToList();
            return null;
        }

        private sealed class GameAssembly : ICharmAssembly
        {
            private readonly Module _module;
            private readonly Type _statEnum;
            private readonly List<Type> _types;

            private GameAssembly(Module module, Type statEnum, List<Type> types)
            {
                _module = module;
                _statEnum = statEnum;
                _types = types;
            }

            public static GameAssembly Create()
            {
                var charmBasic = typeof(Charm_Basic);
                var assembly = charmBasic.Assembly;
                var statEnum = assembly.GetType("ECustomStat");
                if (statEnum == null) return null;

                var types = assembly.GetTypes().Where(charmBasic.IsAssignableFrom).ToList();
                return new GameAssembly(assembly.Modules.First(), statEnum, types);
            }

            public IEnumerable<CharmMethodBody> CharmMethods()
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (var type in _types)
                    foreach (var method in type.GetMethods(flags))
                    {
                        byte[] il;
                        try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                        catch (InvalidOperationException) { continue; }
                        catch (NotSupportedException) { continue; }
                        if (il == null || il.Length == 0) continue;

                        yield return new CharmMethodBody { TypeName = type.Name, MethodName = method.Name, Il = il };
                    }
            }

            public string ArrayField(int token)
            {
                try
                {
                    var field = _module.ResolveField(token);
                    return field != null && field.FieldType.IsArray ? field.Name : null;
                }
                catch (ArgumentException) { return null; }
            }

            public CalledMethod Method(int token)
            {
                try
                {
                    var method = _module.ResolveMethod(token);
                    if (method == null) return null;

                    return new CalledMethod
                    {
                        Name = method.Name,
                        ArgumentCount = method.GetParameters().Length,
                        IsStatic = method.IsStatic,
                        ReturnsValue = method is MethodInfo info && info.ReturnType != typeof(void),
                    };
                }
                catch (ArgumentException) { return null; }
            }

            public string Stat(int value) => Enum.GetName(_statEnum, value);
        }
    }
}
