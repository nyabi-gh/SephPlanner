using SephPlanner.Core.Model;

namespace SephPlanner.DataTool.Prediction;

public static class PredictionProbe
{
    public static CategoryReplay PaperPair()
    {
        var paper = new CharmDefinition { Behavior = "Charm_WhitePaper", PaperMatch = 2 };
        return new CategoryReplay(new[]
        {
            new CategoryReplay.Item { Id = 1, Position = new GridPos(0, 0), Definition = new(), Categories = new() { "EMBER" } },
            new CategoryReplay.Item { Id = 2, Position = new GridPos(1, 0), Definition = paper, Categories = new() { "EMBER" } },
            new CategoryReplay.Item { Id = 3, Position = new GridPos(2, 0), Definition = paper, Categories = new() },
            new CategoryReplay.Item { Id = 4, Position = new GridPos(3, 0), Definition = new(), Categories = new() { "EMBER" } },
        });
    }

    public static StatReplay Feedback()
    {
        var state = new StatReplay(new() { ["A"] = 10 });
        state.Add(1, new() { Source = "A", Divisor = 1, Amounts = new() { ["B"] = 1 } });
        state.Add(2, new() { Source = "B", Divisor = 1, Amounts = new() { ["A"] = 1 } });
        return state;
    }

    public static int Run()
    {
        var leftFirst = PaperPair();
        leftFirst.Refresh(new[] { 2, 3 });
        var rightFirst = PaperPair();
        rightFirst.Refresh(new[] { 3, 2 });
        var ab = Feedback();
        ab.Refresh(1);
        ab.Refresh(2);
        var ba = Feedback();
        ba.Refresh(2);
        ba.Refresh(1);
        Console.WriteLine("게임 코드에서 옮긴 규칙의 합성 입력 실험입니다. 실제 프리팹/실기 검증 결과가 아닙니다.");
        Console.WriteLine($"동일 배치·초기 상태의 종이: 왼쪽 먼저={leftFirst.Categories(2).Count + leftFirst.Categories(3).Count}, 오른쪽 먼저={rightFirst.Categories(2).Count + rightFirst.Categories(3).Count}");
        Console.WriteLine($"상호 전환의 갱신 순서: A→B이면 A={ab.Read("A")}, B→A이면 A={ba.Read("A")}");
        Console.WriteLine("초기 상태와 이벤트 순서를 지정하면 재현할 수 있지만, 최종 배치만으로는 결과가 하나로 정해지지 않습니다.");
        return leftFirst.Categories(2).Count == 0 && rightFirst.Categories(2).Count == 1 && ab.Read("A") == 20 && ba.Read("A") == 10 ? 0 : 1;
    }
}
