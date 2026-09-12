using System.Diagnostics;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// <see cref="PlanRunner"/> 안에서 시간을 재는 시계. <c>Core</c> 라 유니티 없이 잰다.
    /// </summary>
    internal static class SolveClock
    {
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        public static long Now => Stopwatch.GetTimestamp();

        public static double MsSince(long startedAt) =>
            (Stopwatch.GetTimestamp() - startedAt) * MsPerTick;
    }

    /// <summary>
    /// 한 종류의 풀이가 세션 동안 쓴 시간. 평균은 평소가 어떤지를, 최악은 어디서 밀리는지를 말한다.
    ///
    /// <b>버린 풀이도 센다.</b> 뒤에 요청이 와서 취소된 풀이는 답을 못 쓰지만 CPU 와 할당은 이미
    /// 썼다. 그 수가 게시된 수와 비슷하면 요청이 결과보다 빨리 오고 있다는 뜻이다.
    /// </summary>
    public sealed class PlanSolveStat
    {
        public int Count { get; private set; }

        /// <summary>끝났지만 낡아서 버린 풀이의 수. <see cref="Count"/> 에 포함된다.</summary>
        public int Discarded { get; private set; }

        public double TotalMs { get; private set; }
        public double WorstMs { get; private set; }

        /// <summary>
        /// <see cref="WorstMs"/> 가 찍힌 것이 몇 번째 풀이였나. 세션 초반의 낮은 번호면 첫 호출
        /// JIT 이고, 번호가 세션 내내 갱신되면 판이 커진 것이다.
        /// </summary>
        public int WorstRun { get; private set; }

        public double AverageMs => Count == 0 ? 0 : TotalMs / Count;

        internal void Add(double elapsedMs, bool discarded)
        {
            Count++;
            TotalMs += elapsedMs;
            if (discarded) Discarded++;
            if (elapsedMs <= WorstMs) return;
            WorstMs = elapsedMs;
            WorstRun = Count;
        }

        internal PlanSolveStat Copy() => new PlanSolveStat
        {
            Count = Count,
            Discarded = Discarded,
            TotalMs = TotalMs,
            WorstMs = WorstMs,
            WorstRun = WorstRun,
        };
    }

    /// <summary>
    /// 풀이가 얼마나 걸리고 답이 얼마나 늦게 나오는지. <see cref="PlanRunner.Stats"/> 가 잠금 안에서
    /// 복사해 주는 한 장면이라, 읽는 쪽은 아무 때나 들여다봐도 된다.
    ///
    /// 폴링 비용(<c>[perf]</c>)만으로는 "F10 을 눌렀는데 계산이 안 끝난다" 를 설명할 수 없었다.
    /// 밀림은 풀이가 느려서가 아니라 요청이 답보다 자주 와서 생기므로, 시간과 함께
    /// <see cref="Backlog"/> 를 봐야 읽힌다.
    ///
    /// 실행기를 다시 지으면(F9 로 카탈로그를 새로 지을 때) 여기부터 다시 센다.
    /// </summary>
    public sealed class PlanRunnerStats
    {
        public PlanSolveStat Placement { get; internal set; } = new PlanSolveStat();

        /// <summary>
        /// 배치와 따로 푼 조언. 지금은 조언이 배치와 한 번에 계산되므로 0 이고,
        /// 두 단계 게시가 들어가면 여기가 찬다.
        /// </summary>
        public PlanSolveStat Advice { get; internal set; } = new PlanSolveStat();

        /// <summary>마지막으로 게시된 계획이 요청된 때로부터 게시까지 걸린 시간. 대기 시간을 포함한다.</summary>
        public double PublishDelayMs { get; internal set; }

        public double WorstPublishDelayMs { get; internal set; }

        /// <summary><see cref="WorstPublishDelayMs"/> 가 찍힌 것이 몇 번째 게시였나.</summary>
        public int WorstPublishRun { get; internal set; }

        public int Published { get; internal set; }

        /// <summary>
        /// 지금 요청 세대와 게시 세대의 차이. 0 이면 화면이 최신이고, 계속 0 이 아니면 답이
        /// 요청을 따라가지 못하고 있다는 뜻이다.
        /// </summary>
        public long Backlog { get; internal set; }

        public long WorstBacklog { get; internal set; }
    }
}
