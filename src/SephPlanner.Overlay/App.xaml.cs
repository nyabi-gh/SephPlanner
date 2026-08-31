using System.IO;
using System.Windows;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 게임 위에 떠 있는 창이라, 예외 한 번에 WPF 크래시 대화상자가 게임을 가리면 안 된다.
        // 파일에 남기고 계속 돈다.
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log(args.Exception);
            args.SetObserved();
        };
    }

    private static void Log(Exception exception)
    {
        try
        {
            var path = Path.Combine(IpcContract.DataDirectory, "overlay-errors.log");
            Directory.CreateDirectory(IpcContract.DataDirectory);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // 로그조차 못 남기는 상황(디스크 꽉 참 등)에서 또 던지면 무한 루프가 된다.
        }
    }
}
