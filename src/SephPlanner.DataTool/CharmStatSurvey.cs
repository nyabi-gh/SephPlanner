using System.Reflection;
using SephPlanner.Core.Runtime;

namespace SephPlanner.DataTool;

/// <summary>
/// 게임 어셈블리에서 "능력치 표 없이 자기 코드로 능력치를 올려 주는" 아티팩트를 찾아 보여 준다.
/// 플러그인이 카탈로그를 지을 때 하는 것과 같은 검사를 게임을 켜지 않고 돌려, 패치 뒤에 무엇이
/// 새로 잡히고 무엇을 놓쳤는지 먼저 본다.
/// </summary>
public static class CharmStatSurvey
{
    public static int Run(string gameDirectory)
    {
        var path = Path.Combine(GameLocator.ManagedDir(gameDirectory), "Assembly-CSharp.dll");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"게임 어셈블리를 찾지 못했습니다: {path}");
            return 1;
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(path);
        }
        catch (Exception ex) when (ex is BadImageFormatException || ex is FileLoadException || ex is IOException)
        {
            Console.Error.WriteLine("게임 어셈블리를 읽지 못했습니다: " + ex.Message);
            return 1;
        }

        var reader = GameAssembly.Create(assembly);
        if (reader is null)
        {
            Console.Error.WriteLine("Charm_Basic 또는 ECustomStat 을 찾지 못했습니다. 게임 구조가 바뀌었습니다.");
            return 1;
        }

        var report = CharmStatScan.Run(reader);

        Console.WriteLine($"아티팩트 클래스 {reader.TypeCount}개를 읽었습니다.");
        Console.WriteLine();
        Console.WriteLine($"능력치를 그대로 올려 주는 클래스 {report.Grants.Select(g => g.TypeName).Distinct().Count()}개");
        foreach (var group in report.Grants.GroupBy(grant => grant.TypeName).OrderBy(group => group.Key, StringComparer.Ordinal))
            Console.WriteLine($"  {group.Key,-42} {string.Join(", ", group.Select(g => $"{g.Field} -> {g.Stat}"))}");
        Console.WriteLine();

        Console.WriteLine($"능력치를 올리기는 하는데 어느 배열인지 정하지 못한 클래스 {report.Unresolved.Count}개");
        Console.WriteLine("  (애매하면 버린다 - 틀린 능력치로 값매김하는 것이 값을 안 매기는 것보다 나쁘다)");
        foreach (var name in report.Unresolved) Console.WriteLine($"  {name}");
        return 0;
    }

    /// <summary>
    /// <see cref="ICharmAssembly"/>를 게임 어셈블리에 붙인다. 리플렉션만 쓰므로 게임을 켜지 않고
    /// 돌아가며, 플러그인도 같은 구현을 쓴다.
    /// </summary>
    private sealed class GameAssembly : ICharmAssembly
    {
        private readonly Module _module;
        private readonly Type _charmBasic;
        private readonly Type _statEnum;
        private readonly List<Type> _types;

        private GameAssembly(Module module, Type charmBasic, Type statEnum, List<Type> types)
        {
            _module = module;
            _charmBasic = charmBasic;
            _statEnum = statEnum;
            _types = types;
        }

        public int TypeCount => _types.Count;

        public static GameAssembly? Create(Assembly assembly)
        {
            var charmBasic = assembly.GetType("Charm_Basic");
            var statEnum = assembly.GetType("ECustomStat");
            if (charmBasic is null || statEnum is null) return null;

            var types = assembly.GetTypes().Where(charmBasic.IsAssignableFrom).ToList();
            return new GameAssembly(assembly.Modules.First(), charmBasic, statEnum, types);
        }

        public IEnumerable<CharmMethodBody> CharmMethods()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (var type in _types)
                foreach (var method in type.GetMethods(flags))
                {
                    byte[]? il;
                    try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                    catch (InvalidOperationException) { continue; }
                    if (il is null || il.Length == 0) continue;

                    yield return new CharmMethodBody { TypeName = type.Name, MethodName = method.Name, Il = il };
                }
        }

        public string? ArrayField(int token)
        {
            try
            {
                var field = _module.ResolveField(token);
                return field is not null && field.FieldType.IsArray ? field.Name : null;
            }
            catch (ArgumentException) { return null; }
        }

        public CalledMethod? Method(int token)
        {
            try
            {
                var method = _module.ResolveMethod(token);
                if (method is null) return null;

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

        public string? Stat(int value) => Enum.GetName(_statEnum, value);
    }
}
