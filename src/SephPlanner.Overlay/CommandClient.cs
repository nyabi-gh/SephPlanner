using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

/// <summary>플러그인의 명령 파이프로 자동 배치 요청을 보낸다.</summary>
public static class CommandClient
{
    public static bool TrySend(ApplyPlanCommand command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", IpcContract.CommandPipeName, PipeDirection.Out);
            pipe.Connect(500);

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command) + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }
}
