using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SephPlanner.DataTool;

// 게임의 StreamingAssets/Localization 에서 석판·아티팩트의 이름과 설명을 뽑아
// data/generated/text.json 으로 정리한다.
//
// 뽑는 것은 텍스트뿐이다. 스프라이트/아이콘 같은 저작물은 건드리지 않으며,
// 결과 파일도 사용자 PC에서 생성될 뿐 배포물에 포함하지 않는다(README 참고).

if (args.Contains("--solve"))
    return SolverSmokeTest.Run(tabletCount: 3, charmCount: 12);

// 저장해 둔 스냅샷으로 우리 레벨 계산을 게임 값과 견준다. 게임을 다시 켜지 않고 확인할 수 있다.
var checkIndex = Array.IndexOf(args, "--check");
if (checkIndex >= 0)
{
    if (checkIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("사용법: --check <스냅샷.json>");
        return 1;
    }
    return SnapshotCheck.Run(args[checkIndex + 1]);
}

var gameDir = args.FirstOrDefault(a => !a.StartsWith('-')) ?? GameLocator.Find();
if (gameDir is null)
{
    Console.Error.WriteLine("세피리아 설치 경로를 찾지 못했습니다. 경로를 인자로 넘기거나 SEPHIRIA_DIR 환경변수를 설정하세요.");
    return 1;
}

var locDir = GameLocator.LocalizationDir(gameDir);
if (!Directory.Exists(locDir))
{
    Console.Error.WriteLine($"로컬라이제이션 폴더가 없습니다: {locDir}");
    return 1;
}

Console.WriteLine($"게임 경로: {gameDir}");

// id -> 언어 -> 필드 -> 값
var tablets = new SortedDictionary<string, Dictionary<string, Dictionary<string, string>>>();
var charms = new SortedDictionary<string, Dictionary<string, Dictionary<string, string>>>();

foreach (var file in Directory.GetFiles(locDir, "*.json"))
{
    var lang = Path.GetFileNameWithoutExtension(file);
    var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
    if (root is null) continue;

    // 1차: Charm_ 접두 키로 아티팩트 id 집합을 먼저 확정한다.
    //      (Item_{id}_Name 만 보면 아티팩트가 아닌 소모품까지 딸려온다)
    var charmIds = new HashSet<string>();
    foreach (var kv in root)
    {
        if (!kv.Key.StartsWith("Charm_", StringComparison.Ordinal)) continue;
        var rest = kv.Key["Charm_".Length..];
        var us = rest.LastIndexOf('_');
        if (us > 0) charmIds.Add(rest[..us]);
    }

    foreach (var kv in root)
    {
        var key = kv.Key;
        var value = kv.Value?.GetValue<string>();
        if (value is null) continue;

        if (key.StartsWith("Item_StoneTablet_", StringComparison.Ordinal))
        {
            var rest = key["Item_StoneTablet_".Length..];
            var us = rest.LastIndexOf('_');
            if (us <= 0) continue;
            Put(tablets, rest[..us], lang, rest[(us + 1)..], value);
        }
        else if (key.StartsWith("Item_", StringComparison.Ordinal))
        {
            var rest = key["Item_".Length..];
            var us = rest.LastIndexOf('_');
            if (us <= 0) continue;
            var id = rest[..us];
            if (charmIds.Contains(id)) Put(charms, id, lang, rest[(us + 1)..], value);
        }
        else if (key.StartsWith("Charm_", StringComparison.Ordinal))
        {
            var rest = key["Charm_".Length..];
            var us = rest.LastIndexOf('_');
            if (us <= 0) continue;
            Put(charms, rest[..us], lang, rest[(us + 1)..], value);
        }
    }
}

var outDir = Path.Combine(FindRepoRoot(), "data", "generated");
Directory.CreateDirectory(outDir);

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    // 한글이 \uXXXX 로 이스케이프되면 사람이 읽을 수 없어 진단이 어려워진다.
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var outPath = Path.Combine(outDir, "text.json");
File.WriteAllText(outPath, JsonSerializer.Serialize(new { tablets, charms }, opts));

Console.WriteLine($"석판 {tablets.Count}종, 아티팩트 {charms.Count}종 추출");
Console.WriteLine($"저장: {outPath}");

var koTablets = tablets.Where(t => t.Value.TryGetValue("ko-KR", out var f) && f.ContainsKey("Name")).Take(5);
foreach (var t in koTablets)
    Console.WriteLine($"  예시: {t.Key} = {t.Value["ko-KR"]["Name"]}");

return 0;

static void Put(
    IDictionary<string, Dictionary<string, Dictionary<string, string>>> target,
    string id, string lang, string field, string value)
{
    if (!target.TryGetValue(id, out var byLang))
        target[id] = byLang = new Dictionary<string, Dictionary<string, string>>();
    if (!byLang.TryGetValue(lang, out var fields))
        byLang[lang] = fields = new Dictionary<string, string>();
    fields[field] = value;
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "SephPlanner.slnx")))
        dir = Path.GetDirectoryName(dir);
    return dir ?? Directory.GetCurrentDirectory();
}
