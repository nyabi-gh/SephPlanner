using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Overlay;

/// <summary>
/// 플러그인이 여는 명명 파이프에 붙어 스냅샷을 받아온다. 게임이 꺼져 있으면 조용히 재시도한다.
/// </summary>
public sealed class SnapshotClient
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public event Action<GameSnapshot>? SnapshotReceived;
    public event Action<bool>? ConnectionChanged;

    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", IpcContract.PipeName, PipeDirection.In, PipeOptions.Asynchronous);

                // 게임이 아직 안 켜졌으면 타임아웃 후 다시 시도한다.
                await pipe.ConnectAsync(2000, token);
                ConnectionChanged?.Invoke(true);

                using var reader = new StreamReader(pipe);
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line is null) break; // 파이프가 닫혔다.
                    if (line.Length == 0) continue;

                    GameSnapshot? snapshot;
                    try
                    {
                        snapshot = JsonSerializer.Deserialize<GameSnapshot>(line, Options);
                    }
                    catch (JsonException)
                    {
                        continue; // 프로토콜이 어긋난 한 줄 때문에 연결을 끊지는 않는다.
                    }

                    if (snapshot is not null) SnapshotReceived?.Invoke(snapshot);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            ConnectionChanged?.Invoke(false);
            try { await Task.Delay(1000, token); } catch (OperationCanceledException) { return; }
        }
    }
}
