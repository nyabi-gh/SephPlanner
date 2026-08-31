using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

/// <summary>플러그인의 명령 파이프로 자동 배치 요청을 보내고 실행 결과 한 줄을 받아 온다.</summary>
public static class CommandClient
{
    /// <summary>
    /// 결과 문자열, 연결하지 못했거나 응답이 없으면 null. 결과를 화면에 보여 주지 않으면
    /// 거부·실패가 전부 게임 로그에만 남는 무음 실패가 된다.
    /// </summary>
    public static async Task<string?> SendAsync(ApplyPlanCommand command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", IpcContract.CommandPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(500);

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command) + "\n");
            await pipe.WriteAsync(bytes);
            await pipe.FlushAsync();

            // 플러그인이 게임 메인 스레드의 실행을 기다렸다가 답하므로 여유를 둔다.
            using var reader = new StreamReader(pipe);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (Exception)
        {
            // 게임이 꺼졌거나, 파이프가 없거나, 응답 전에 끊겼다. 호출자가 null 로 구분한다.
            return null;
        }
    }
}
