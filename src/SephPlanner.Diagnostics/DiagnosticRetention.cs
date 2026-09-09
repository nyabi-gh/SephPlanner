namespace SephPlanner.Diagnostics;

internal sealed class DiagnosticRetention(DiagnosticStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do { await store.PruneAsync(stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
