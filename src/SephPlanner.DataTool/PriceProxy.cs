using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 게임이 매겨 둔 가격이 아티팩트의 값어치를 얼마나 예측하는지 잰다.
///
/// 값어치를 잰 110종은 답을 아는 표본이다. 거기서 가격과 잰 값어치가 얼마나 함께 움직이는지
/// 보면, 잴 수 없는 142종에 가격을 대리값으로 써도 되는지 알 수 있다. 레어도가 그 자리에
/// 있었지만 대리값으로서 나쁘다는 것이 이미 드러났으므로, 견줄 상대는 레어도다.
///
/// **쓸지 말지는 이 숫자를 보고 정한다.** 쓸 만하다고 미리 정해 두고 재는 것이 아니다.
/// </summary>
public static class PriceProxy
{
    public sealed record Sample(string Name, double Worth, int Cost, int Sapphire, Rarity Rarity);

    public static void Report(IReadOnlyList<CharmDefinition> charms)
    {
        // 자체 활성 효과가 없는 아티팩트도 값어치를 "안다"는 쪽으로 분류되지만 능력치 표가 없다.
        // 표를 요구하지 않으면 마음의 짐에서 터진다.
        var samples = charms
            .Where(c => c.StatWorthByLevel.Count > 0 &&
                        CharmWorth.Resolve(c).Source == CharmWorthSource.Measured)
            .Select(c => new Sample(
                c.Names.TryGetValue("current", out var name) ? name : c.Id,
                c.StatWorthByLevel[^1], c.Cost, c.SapphirePrice, c.Rarity))
            .ToList();

        if (samples.Count == 0 || samples.All(s => s.Cost == 0))
        {
            Console.WriteLine("가격이 덤프에 없습니다. 게임을 한 번 켜서 카탈로그를 다시 덤프하세요.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"가격이 값어치를 예측하는가 - 답을 아는 {samples.Count}종으로 잰다");
        Console.WriteLine("  (순위 상관계수. 1에 가까우면 함께 움직이고, 0이면 아무 관계가 없다)");

        var worth = samples.Select(s => s.Worth).ToList();
        Console.WriteLine($"    원가        {Spearman(worth, samples.Select(s => (double)s.Cost).ToList()),6:0.00}");
        Console.WriteLine($"    사파이어가   {Spearman(worth, samples.Select(s => (double)s.Sapphire).ToList()),6:0.00}");
        Console.WriteLine($"    레어도      {Spearman(worth, samples.Select(s => (double)(int)s.Rarity).ToList()),6:0.00}  <- 지금 쓰는 대리값");
        Console.WriteLine();

        // 가격이 레어도를 그대로 따라간다면 새로 아는 것이 없다. 레어도 안에서도 값이 갈리는지 본다.
        Console.WriteLine("레어도별 원가 - 레어도 안에서 갈리지 않으면 레어도와 같은 말이다");
        foreach (var group in samples.GroupBy(s => s.Rarity).OrderBy(g => (int)g.Key))
        {
            var costs = group.Select(s => s.Cost).OrderBy(c => c).ToList();
            var distinct = costs.Distinct().Count();
            Console.WriteLine($"  {Naming.OfRarity(group.Key),-4} n={costs.Count,3}  {costs[0],5} ~ {costs[^1],-5}  서로 다른 값 {distinct,3}가지"
                              + (distinct <= 1 ? "  <- 한 값뿐" : ""));
        }
        Console.WriteLine();
    }

    /// <summary>
    /// 순위 상관계수. 값어치와 가격은 눈금이 다르고 관계가 곧은 직선일 이유도 없으므로,
    /// 크기가 아니라 순서만 견준다.
    /// </summary>
    private static double Spearman(List<double> left, List<double> right)
    {
        var a = Ranks(left);
        var b = Ranks(right);

        double meanA = a.Average(), meanB = b.Average();
        double top = 0, leftSpread = 0, rightSpread = 0;
        for (var i = 0; i < a.Count; i++)
        {
            double da = a[i] - meanA, db = b[i] - meanB;
            top += da * db;
            leftSpread += da * da;
            rightSpread += db * db;
        }
        var bottom = Math.Sqrt(leftSpread * rightSpread);
        return bottom > 0 ? top / bottom : 0;
    }

    /// <summary>같은 값끼리는 평균 순위를 나눠 갖는다. 가격은 같은 값이 많이 겹친다.</summary>
    private static List<double> Ranks(List<double> values)
    {
        var order = values.Select((value, index) => (value, index)).OrderBy(pair => pair.value).ToList();
        var ranks = new double[values.Count];

        for (var start = 0; start < order.Count;)
        {
            var end = start;
            while (end + 1 < order.Count && order[end + 1].value == order[start].value) end++;

            var shared = (start + end) / 2.0;
            for (var i = start; i <= end; i++) ranks[order[i].index] = shared;
            start = end + 1;
        }
        return ranks.ToList();
    }
}
