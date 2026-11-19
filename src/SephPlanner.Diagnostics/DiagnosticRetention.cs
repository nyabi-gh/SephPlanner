namespace SephPlanner.Diagnostics;

public sealed partial class DiagnosticRetention(DiagnosticStore store, DiagnosticHealth health, ILogger<DiagnosticRetention> log) : BackgroundService
{
    /// <summary>
    /// 정리가 실패해도 서버를 멈추지 않는다. 예외가 여기서 나가면 호스트가 종료 코드 0 으로 끝나
    /// 재시작 정책이 다시 띄우지 않고, 수집과 14일 삭제가 함께 조용히 멎는다. 대신 <c>/health</c> 가
    /// 실패를 돌려주게 해 상태 검사가 알아채게 하고, 다음 주기에 다시 해 본다.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await store.PruneAsync(stoppingToken);
                health.Failure = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                health.Failure = "보관 기한이 지난 진단을 지우지 못했습니다.";
                PruneFailed(log, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "보관 기한이 지난 진단을 지우지 못했습니다. 다음 주기에 다시 시도합니다.")]
    private static partial void PruneFailed(ILogger logger, Exception exception);
}

public sealed class DiagnosticHealth
{
    private volatile string? _failure;

    public string? Failure
    {
        get => _failure;
        set => _failure = value;
    }
}
