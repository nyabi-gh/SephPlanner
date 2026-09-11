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
        var charmPath = PlannerData.ActiveDataFile(PlannerData.CharmDbFile);
        var statPath = PlannerData.ActiveDataFile(PlannerData.StatMeasurementFile);
        if (charmPath is null || statPath is null || !File.Exists(charmPath) || !File.Exists(statPath))
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

        var fromCode = measurement.CharmStats.Where(table => table.FromCode).ToList();
        Console.WriteLine($"아티팩트 {charms.Count}종, 능력치 표가 있는 것 {report.ByEntity.Count}종");
        if (fromCode.Count > 0)
            Console.WriteLine($"  그중 {fromCode.Select(t => t.EntityId).Distinct().Count()}종은 "
                              + "능력치 표가 아니라 아티팩트 코드에서 읽었다");
        Console.WriteLine();

        var measured = new List<CharmDefinition>();
        var floors = new List<CharmDefinition>();
        var blank = new List<CharmDefinition>();
        var handWritten = 0;
        var noEffect = 0;
        foreach (var charm in charms)
        {
            // 자체 활성 효과가 없는 것은 값어치 0 이 답이라 채울 몫이 아니다. 측정한 것과 같은
            // 칸에 세면 "잰 것"이 한 종 부풀고 아래 분포의 표본 수와도 어긋난다.
            if (charm.HasNoActivationEffect)
            {
                noEffect++;
                continue;
            }
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
        Console.WriteLine($"  측정과 어림값 병용   {floors.Count,4}종  (미환산 능력치 또는 고유 효과가 있다)");
        Console.WriteLine($"  레어도 어림값뿐      {blank.Count,4}종  <- 손으로 채울 몫");
        Console.WriteLine($"  자체 활성 효과 없음  {noEffect,4}종  (값어치 0 이 답이다)");
        Console.WriteLine();

        Quantiles(measured);
        foreach (var charm in charms.Where(c => c.StatWorthUnconverted.Count > 0).OrderBy(c => c.EntityId))
            Console.WriteLine($"  미환산 {charm.EntityId} {Name(charm)}: {string.Join(", ", charm.StatWorthUnconverted)}");
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

        Curves(charms, curated);

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

        var draft = new DraftFile
        {
            Version = 1,
            Charms = ordered.Select(charm => new DraftEntry
            {
                Id = charm.Id,
                EntityId = charm.EntityId,
                Effect = string.Join(" / ", charm.EffectLines),
            }).ToList(),
        };

        var outPath = Path.Combine(repoRoot, "data", "values", "charms.draft.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(draft, Write));

        Console.WriteLine($"초안 저장: {outPath}");
        Console.WriteLine("등급(tier)을 채운 항목만 골라 charms.json 으로 옮기세요.");
        Console.WriteLine("effect 는 무엇에 등급을 매기는지 보라고 붙인 것입니다. 옮기지 말고,");
        Console.WriteLine("note 에는 왜 그 등급인지를 우리 표현으로 적으세요.");
        return 0;
    }

    /// <summary>
    /// 초안 항목. 게임의 효과 문장은 <see cref="Effect"/> 로 나간다 - <c>CharmValueEntry</c> 에
    /// 없는 필드라 플러그인(Newtonsoft)도 도구(STJ)도 읽지 않는다.
    ///
    /// <c>note</c> 에 미리 채우면 등급만 매기고 넘긴 항목이 게임 문장을 단 채 <c>charms.json</c>
    /// 으로 옮겨지고, 그 파일은 플러그인 DLL 에 임베드되어 배포물에 실려 나간다
    /// (docs/LEGAL.md - 게임 저작물 미배포).
    /// </summary>
    private sealed class DraftEntry
    {
        public string Id { get; set; } = "";
        public int EntityId { get; set; }
        public int Tier { get; set; }
        public string Effect { get; set; } = "";
        public string Note { get; set; } = "";
    }

    private sealed class DraftFile
    {
        public int Version { get; set; }
        public List<DraftEntry> Charms { get; set; } = new();
    }

    /// <summary>
    /// 레벨이 올라도 값어치가 내려가는 아티팩트. 솔버는 값어치가 큰 배치를 고르므로 그런 구간이
    /// 있으면 낮은 레벨 칸이 정답이 되고, 사용자에게는 "왜 낮은 자리에 박아 두느냐"로 보인다.
    /// 여기 오르는 것이 같은 제보가 올 자리의 목록이다.
    /// </summary>
    private static void Curves(List<CharmDefinition> charms, CharmValueBook curated)
    {
        var found = charms
            .Select(charm => (Charm: charm, Drops: WorthCurveReview.Of(charm, curated.Of(charm))))
            .Where(row => row.Drops.Count > 0)
            .OrderBy(row => row.Drops.Min(drop => drop.Delta))
            .ToList();
        if (found.Count == 0) return;

        Console.WriteLine($"레벨이 올라도 값어치가 내려가는 것 {found.Count}종 - 낮은 칸이 정답이 되는 자리다");
        foreach (var (charm, drops) in found)
            foreach (var drop in drops)
            {
                var cause = drop.Cause.Length == 0
                    ? ""
                    : $"  {drop.Cause} {drop.CauseFromAmount} -> {drop.CauseToAmount}";
                Console.WriteLine($"  {Name(charm),-22} 레벨 {drop.FromLevel} -> {drop.ToLevel}  "
                                  + $"{drop.Delta,6:0.00}{cause}");
            }
        Console.WriteLine("  고칠 곳은 솔버가 아니라 능력치 환산율이나 charms.json 이다.");
        Console.WriteLine("  비단조 자체는 금지하지 않는다(docs/PLACEMENT-OBJECTIVE.md 의 \"탐색과 최적성\").");
        Console.WriteLine();
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
