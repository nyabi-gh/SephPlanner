using System.Text.Encodings.Web;
using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 아티팩트 가치가 어디까지 측정됐고 어디부터 손으로 채워야 하는지 보여 주고, 채워야 할
/// 몫을 초안 파일로 뽑는다.
///
/// 빈 표를 앞에 두면 아무도 채우지 않는다. 그래서 잴 수 있는 것은 미리 재어 두고, 남은 것만
/// 식별자와 효과 설명까지 붙여 내놓는다.
/// </summary>
public static class CharmValueDraft
{
    /// <summary>이 번호 위쪽은 일반 아이템 풀이 아니라 기적 보상 같은 특수 경로로만 들어온다.</summary>
    private const int SpecialPathFrom = 3000;

    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions Write = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Run(string repoRoot)
    {
        var charmPath = Path.Combine(PlannerData.DataDirectory, PlannerData.CharmDbFile);
        var statPath = Path.Combine(PlannerData.DataDirectory, PlannerData.StatMeasurementFile);
        if (!File.Exists(charmPath) || !File.Exists(statPath))
        {
            Console.Error.WriteLine($"카탈로그 덤프가 없습니다: {PlannerData.DataDirectory}");
            Console.Error.WriteLine("게임을 한 번 켜서 카탈로그를 다시 덤프하세요(F9).");
            return 1;
        }

        var charms = JsonSerializer.Deserialize<List<CharmDefinition>>(File.ReadAllText(charmPath), Read);
        var measurement = JsonSerializer.Deserialize<StatMeasurement>(File.ReadAllText(statPath), Read);
        if (charms is null || measurement is null)
        {
            Console.Error.WriteLine("덤프를 읽지 못했습니다.");
            return 1;
        }

        var report = CharmStatWorth.Apply(charms, measurement);
        var curated = LoadCurated(repoRoot);

        Console.WriteLine($"아티팩트 {charms.Count}종, 능력치 표가 있는 것 {report.ByEntity.Count}종");
        Console.WriteLine();

        var measured = new List<CharmDefinition>();
        var floors = new List<CharmDefinition>();
        var blank = new List<CharmDefinition>();
        var handWritten = 0;
        foreach (var charm in charms)
        {
            switch (CharmWorth.Resolve(charm, curated.Of(charm)).Source)
            {
                case CharmWorthSource.Curated: handWritten++; break;
                case CharmWorthSource.Measured: measured.Add(charm); break;
                case CharmWorthSource.MeasuredFloor: floors.Add(charm); break;
                default: blank.Add(charm); break;
            }
        }

        Console.WriteLine($"  손으로 채운 것       {handWritten,4}종");
        Console.WriteLine($"  잰 값이 값어치 전부  {measured.Count,4}종  (능력치만 주는 아티팩트)");
        Console.WriteLine($"  잰 값이 아래 한계    {floors.Count,4}종  (능력치 밖에 고유 효과가 더 있다)");
        Console.WriteLine($"  레어도 어림값뿐      {blank.Count,4}종  <- 손으로 채울 몫");
        Console.WriteLine();

        Quantiles(measured);
        PriceProxy.Report(charms);

        var lowConfidence = measured.Where(c => c.StatWorthConfidence < 0.5).ToList();
        if (lowConfidence.Count > 0)
        {
            Console.WriteLine($"환산이 동어반복에 가까운 것 {lowConfidence.Count}종 "
                              + "(그 아티팩트만 주는 능력치라 환산율이 자기 자신에서 나왔다)");
            foreach (var charm in lowConfidence.OrderBy(c => c.StatWorthConfidence).Take(8))
                Console.WriteLine($"  {Name(charm),-22} 신뢰 {charm.StatWorthConfidence:0.00}");
            Console.WriteLine();
        }

        // 142종을 순서 없이 늘어놓으면 어디부터 손대야 할지 알 수 없다. 실제로 자주 만나는
        // 것부터 오도록, 도감 밖 경로로만 들어오는 아티팩트를 뒤로 미루고 레어도 높은 순으로 둔다.
        var ordered = blank
            .OrderBy(c => c.EntityId >= SpecialPathFrom ? 1 : 0)
            .ThenByDescending(c => (int)c.Rarity)
            .ThenBy(c => c.EntityId)
            .ToList();

        var mainstream = ordered.Count(c => c.EntityId < SpecialPathFrom);
        Console.WriteLine($"채울 몫 {ordered.Count}종의 차림 - 앞에서부터 채우면 된다");
        Console.WriteLine($"  자주 만나는 것        {mainstream,4}종");
        Console.WriteLine($"  도감 밖 경로로만 오는 것 {ordered.Count - mainstream,4}종  (기적 보상 등. 뒤로 미뤄 두었다)");
        Console.WriteLine();

        var draft = new CharmValueFile
        {
            Version = 1,
            Charms = ordered.Select(charm => new CharmValueEntry
            {
                Id = charm.Id,
                EntityId = charm.EntityId,
                Note = string.Join(" / ", charm.EffectLines),
            }).ToList(),
        };

        var outPath = Path.Combine(repoRoot, "data", "values", "charms.draft.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(draft, Write));

        Console.WriteLine($"초안 저장: {outPath}");
        Console.WriteLine("등급(tier)을 채운 항목만 골라 charms.json 으로 옮기세요.");
        Console.WriteLine("note 에는 지금 게임 효과 설명이 들어 있습니다. 판단 근거로 바꿔 적으면 됩니다.");
        return 0;
    }

    /// <summary>
    /// 등급 다섯 칸의 기준값이 나온 자리. 게임이 패치되면 여기 값이 움직이므로 그때
    /// <c>CharmWorth.Tiers</c>를 다시 맞춘다.
    /// </summary>
    private static void Quantiles(List<CharmDefinition> measured)
    {
        var bases = new List<double>();
        var perLevels = new List<double>();
        foreach (var charm in measured)
        {
            var table = charm.StatWorthByLevel;
            if (table.Count == 0) continue;

            bases.Add(table[0]);
            if (table.Count > 1) perLevels.Add((table[^1] - table[0]) / (table.Count - 1));
        }
        if (bases.Count == 0) return;

        bases.Sort();
        perLevels.Sort();

        Console.WriteLine($"잰 값의 분포 ({bases.Count}종) - 등급 다섯 칸의 기준값이 여기서 나왔다");
        Console.WriteLine("  분위      10%    25%    50%    75%    90%");
        Console.WriteLine($"  base   {Row(bases)}");
        Console.WriteLine($"  레벨당  {Row(perLevels)}");
        Console.WriteLine();
    }

    private static string Row(List<double> sorted) =>
        string.Join("", new[] { 0.10, 0.25, 0.50, 0.75, 0.90 }.Select(p => $"{Quantile(sorted, p),7:0.00}"));

    private static double Quantile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;

        var index = (sorted.Count - 1) * p;
        var low = (int)Math.Floor(index);
        var high = (int)Math.Ceiling(index);
        return sorted[low] + (sorted[high] - sorted[low]) * (index - low);
    }

    private static CharmValueBook LoadCurated(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "data", "values", "charms.json");
        if (!File.Exists(path)) return CharmValueBook.Empty;

        return new CharmValueBook(JsonSerializer.Deserialize<CharmValueFile>(File.ReadAllText(path), Read));
    }

    private static string Name(CharmDefinition charm) =>
        charm.Names.TryGetValue("current", out var name) ? name : charm.Id;
}
