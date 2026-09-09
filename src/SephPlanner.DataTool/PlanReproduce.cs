using System.Text.Json;
using SephPlanner.Core.Runtime;

namespace SephPlanner.DataTool;

public static class PlanReproduce
{
    public static int Run(string path, bool allowModelChange = false)
    {
        try
        {
            var replay = JsonSerializer.Deserialize<PlanReplay>(PlanReplayFile.Read(path)) ??
                throw new InvalidDataException("재현 입력이 비었습니다.");
            Console.WriteLine($"저장: {replay.CapturedUtc}, {replay.Producer}");
            Console.WriteLine($"게시 요청 {replay.PublishedGeneration}, 마지막 요청 {replay.RequestedGeneration}");
            if (replay.RequestedGeneration != replay.PublishedGeneration || replay.LatestError.Length > 0)
                Console.WriteLine("마지막으로 게시된 계획을 재생합니다. F10 시점의 최신 상태와 다를 수 있습니다. " + replay.LatestError);
            if (replay.CoreBuild != PlanReplay.CurrentCoreBuild)
                Console.WriteLine($"계산 코드 차이: 저장={replay.CoreBuild}, 현재={PlanReplay.CurrentCoreBuild}");
            var plan = replay.Rebuild(allowModelChange);
            var differences = replay.Expected!.Differences(ReplayResult.From(plan));
            Console.WriteLine($"점수 {plan.Current.Score:0.########} → {plan.Best.Score:0.########}");
            foreach (var difference in differences) Console.WriteLine(difference);
            Console.WriteLine(differences.Count == 0
                ? "저장된 배치·점수·추천 요약과 일치합니다. 실제 게임 효과의 정확성 검증과는 별개입니다."
                : $"저장된 결과와 {differences.Count}곳이 다릅니다.");
            return differences.Count == 0 ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException ||
                                   ex is ArgumentException || ex is InvalidOperationException || ex is NullReferenceException)
        {
            Console.Error.WriteLine("계획 재현 실패: " + ex.Message);
            return 1;
        }
    }
}
