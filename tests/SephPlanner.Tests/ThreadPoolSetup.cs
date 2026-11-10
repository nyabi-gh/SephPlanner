using System.Runtime.CompilerServices;

namespace SephPlanner.Tests;

/// <summary>
/// 실행기 시험은 풀이가 스레드 풀에서 시작되기를 5초까지 기다린다. 그런데 같은 시간에 도는
/// 솔버 시험이 풀 작업자를 CPU 로 붙잡으면, 풀은 작업자를 천천히만 늘려서 큐에 들어간 풀이가
/// 그 뒤에야 시작한다. 논리 코어 4개를 CPU 로 채운 채 잰 대기가 7.8초였고, 최소 작업자 수를
/// 올리면 0ms 였다. 이 어셈블리에서만 최소 수를 올린다 - 게임에는 이런 경합이 없다.
/// </summary>
internal static class ThreadPoolSetup
{
    // 동시에 도는 시험 클래스와 그 안의 병렬 풀이를 넉넉히 덮는 값이다(정한 값, 잰 최적값이 아니다).
    private const int MinimumWorkers = 64;

    [ModuleInitializer]
    internal static void RaiseMinimumWorkers()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        if (workers < MinimumWorkers) ThreadPool.SetMinThreads(MinimumWorkers, io);
    }
}
